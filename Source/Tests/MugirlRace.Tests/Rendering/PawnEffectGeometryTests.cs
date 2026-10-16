using System.Globalization;
using Mugirl.Rendering;

namespace Mugirl.Tests.Rendering;

/// <summary>
/// 描边与运动拖尾的 CPU 参数：1.7 由 MeshPool 元数据（尺寸、镜像）推出四边形，1.6 从网格 UV 与顶点推出。两者在 Verse
/// MeshMakerPlanes 的全部网格形态（镜像、backLift、twist）与随机绘制矩阵、贴图缩放偏移、描边宽度、位移下逐位一致。
/// </summary>
public sealed class PawnEffectGeometryTests
{
    private const int Cases = 20_000;

    private static readonly (bool Flipped, bool BackLift, bool Twist)[] MeshVariants =
    [
        (false, false, false), (true, false, false), (false, true, false), (true, true, false), (false, false, true), (true, false, true),
    ];

    [Fact]
    public void Quad_FromMetaData_MatchesLegacyOutlineAndBlurQuads()
    {
        Random random = new(4101);
        Mismatches mismatches = new("PawnLayerQuad vs 1.6 QuadInfo/Quad");
        for (int i = 0; i < Cases; i++)
        {
            float width = Size(random);
            float height = Size(random);
            (bool flipped, bool backLift, bool twist) = MeshVariants[random.Next(MeshVariants.Length)];
            (Vector3[] vertices, Vector2[] uv) = PlaneMeshGeometry.Create(width, height, flipped, backLift, twist);
            string Input() => Describe(width, height, flipped, backLift, twist);

            bool supported = PawnLayerQuad.TryFromMetaData(width, height, flipped, out PawnLayerQuad quad);
            Legacy16PawnEffects.OutlineQuad? outline = Legacy16PawnEffects.OutlineQuadInfo(vertices, uv);
            mismatches.Check(outline is not null, supported, Input);
            if (outline is not null && supported)
            {
                Check(mismatches, new Vector3(quad.SpanU, 0f, 0f), outline.USpan, Input);
                Check(mismatches, new Vector3(0f, 0f, quad.SpanV), outline.VSpan, Input);
                Check(mismatches, PawnLayerQuad.UvMin, outline.UvMin, Input);
                Check(mismatches, PawnLayerQuad.UvMax, outline.UvMax, Input);
            }

            Legacy16PawnEffects.BlurQuad? blur = Legacy16PawnEffects.BlurQuadInfo(vertices, uv);
            if (blur is not null)
            {
                mismatches.Check(true, supported, Input);
                Check(mismatches, new Vector3(quad.SpanU, 0f, 0f), blur.U, Input);
                Check(mismatches, new Vector3(0f, 0f, quad.SpanV), blur.V, Input);
                Check(mismatches, PawnLayerQuad.UvMin, blur.Min, Input);
                Check(mismatches, PawnLayerQuad.UvMax, blur.Max, Input);
            }
        }

        mismatches.AssertNone();
    }

    [Fact]
    public void Quad_RejectsMeshesWithoutArea()
    {
        Assert.False(PawnLayerQuad.TryFromMetaData(0f, 1f, flipped: false, out _));
        Assert.False(PawnLayerQuad.TryFromMetaData(1f, -1f, flipped: false, out _));
        Assert.False(PawnLayerQuad.TryFromMetaData(float.NaN, 1f, flipped: true, out _));
        Assert.True(PawnLayerQuad.TryFromMetaData(1.5f, 1.5f, flipped: true, out PawnLayerQuad quad));
        Bits.Equal(-1.5f, quad.SpanU, "flipped meshes run U along -X");
        Bits.Equal(1.5f, quad.SpanV, "V runs along +Z");
    }

    [Fact]
    public void Outline_MatchesLegacyDrawBitForBit()
    {
        Random random = new(4102);
        Mismatches mismatches = new("ExtraOutlineGeometry vs 1.6 MugirlExtraOutline.Draw");
        for (int i = 0; i < Cases; i++)
        {
            (PawnLayerQuad quad, Vector3[] vertices, Vector2[] uv, string mesh) = RandomQuad(random);
            UMatrix4x4 matrix = RandomMatrix(random, out string matrixText);
            Vector2 tiling = RandomTiling(random);
            Vector2 offset = new(Samples.Uniform(random, -1f, 1f), Samples.Uniform(random, -1f, 1f));
            float width = Samples.Uniform(random, 0.25f, 3f) * 0.012f;
            string Input() => $"{mesh} {matrixText} tiling {tiling} offset {offset} width {width.ToString("R", CultureInfo.InvariantCulture)}";

            var expected = Legacy16PawnEffects.Outline(Legacy16PawnEffects.OutlineQuadInfo(vertices, uv)!, matrix, tiling, offset, width);
            bool computed = ExtraOutlineGeometry.TryCompute(matrix, quad, new Vector4(tiling.X, tiling.Y, offset.X, offset.Y), width, out OutlineLayerParameters actual);
            mismatches.Check(expected is not null, computed, Input);
            if (expected is { } e && computed)
            {
                Check(mismatches, e.St, actual.SrcScaleOffset, Input);
                Check(mismatches, e.PaddingU, actual.PaddingU, Input);
                Check(mismatches, e.PaddingV, actual.PaddingV, Input);
                Check(mismatches, e.UvRect, actual.UvRect, Input);
                Check(mismatches, e.UvRadius, actual.UvRadius, Input);
            }
        }

        mismatches.AssertNone();
    }

    [Fact]
    public void Blur_MatchesLegacyDrawBitForBit()
    {
        Random random = new(4103);
        Mismatches mismatches = new("LanceMotionBlurGeometry vs 1.6 LanceMotionBlur.Draw");
        for (int i = 0; i < Cases; i++)
        {
            (PawnLayerQuad quad, Vector3[] vertices, Vector2[] uv, string mesh) = RandomQuad(random);
            UMatrix4x4 matrix = RandomMatrix(random, out string matrixText);
            Vector2 tiling = RandomTiling(random);
            Vector2 offset = new(Samples.Uniform(random, -1f, 1f), Samples.Uniform(random, -1f, 1f));
            Vector3 travel = new(Samples.Uniform(random, -3f, 3f), Samples.Uniform(random, -0.1f, 0.1f), Samples.Uniform(random, -3f, 3f));
            string Input() => $"{mesh} {matrixText} tiling {tiling} offset {offset} travel {travel}";

            Legacy16PawnEffects.BlurQuad? legacyQuad = Legacy16PawnEffects.BlurQuadInfo(vertices, uv);
            var expected = legacyQuad is null ? null : Legacy16PawnEffects.Blur(legacyQuad, matrix, tiling, offset, travel);
            bool computed = LanceMotionBlurGeometry.TryCompute(matrix, quad, new Vector4(tiling.X, tiling.Y, offset.X, offset.Y), travel, out BlurLayerParameters actual);
            mismatches.Check(expected is not null, computed, Input);
            if (expected is { } e && computed)
            {
                Check(mismatches, e.St, actual.SrcScaleOffset, Input);
                Check(mismatches, e.UvRect, actual.UvRect, Input);
                Check(mismatches, e.BlurUv, actual.BlurUv, Input);
                Check(mismatches, e.Travel, actual.Travel, Input);
                Check(mismatches, e.PaddingU, actual.PaddingU, Input);
                Check(mismatches, e.PaddingV, actual.PaddingV, Input);
            }
        }

        mismatches.AssertNone();
    }

    [Fact]
    public void Degenerate_MatricesAndTinyMeshes_AreSkippedLikeLegacy()
    {
        PawnLayerQuad quad = new(1.5f, 1.5f);
        UMatrix4x4 flat = UnityMatrix.Scale(new Vector3(1f, 1f, 0f));
        Assert.False(ExtraOutlineGeometry.TryCompute(flat, quad, new Vector4(1f, 1f, 0f, 0f), 0.012f, out _));
        Assert.False(LanceMotionBlurGeometry.TryCompute(flat, quad, new Vector4(1f, 1f, 0f, 0f), new Vector3(1f, 0f, 0f), out _));

        // 1.6 Quad.valid：边长平方必须大于 1e-4，即边长大于 0.01。
        Assert.False(LanceMotionBlurGeometry.TryCompute(UnityMatrix.Identity, new PawnLayerQuad(0.01f, 1f), new Vector4(1f, 1f, 0f, 0f), new Vector3(1f, 0f, 0f), out _));
        Assert.True(LanceMotionBlurGeometry.TryCompute(UnityMatrix.Identity, new PawnLayerQuad(0.011f, 1f), new Vector4(1f, 1f, 0f, 0f), new Vector3(1f, 0f, 0f), out _));
    }

    private static (PawnLayerQuad Quad, Vector3[] Vertices, Vector2[] Uv, string Description) RandomQuad(Random random)
    {
        float width = random.Next(3) == 0 ? 1.5f : Samples.Uniform(random, 0.02f, 3f);
        float height = random.Next(3) == 0 ? width : Samples.Uniform(random, 0.02f, 3f);
        (bool flipped, bool backLift, bool twist) = MeshVariants[random.Next(MeshVariants.Length)];
        (Vector3[] vertices, Vector2[] uv) = PlaneMeshGeometry.Create(width, height, flipped, backLift, twist);
        Assert.True(PawnLayerQuad.TryFromMetaData(width, height, flipped, out PawnLayerQuad quad));
        return (quad, vertices, uv, Describe(width, height, flipped, backLift, twist));
    }

    // 人物图层的绘制矩阵：平移、绕 +Y 旋转（含 90° 的整数倍）、正缩放，偶尔压扁一个轴（退化）。
    private static UMatrix4x4 RandomMatrix(Random random, out string description)
    {
        Vector3 position = new(Samples.Uniform(random, -250f, 250f), Samples.Uniform(random, 0f, 30f), Samples.Uniform(random, -250f, 250f));
        float angle = random.Next(2) == 0 ? 90f * random.Next(4) : Samples.Uniform(random, -360f, 360f);
        Vector3 scale = random.Next(25) == 0
            ? new Vector3(Samples.Uniform(random, 0.5f, 2f), 1f, 0f)
            : new Vector3(Samples.Uniform(random, 0.5f, 2f), 1f, Samples.Uniform(random, 0.5f, 2f));
        description = string.Create(CultureInfo.InvariantCulture, $"pos {position} angle {angle:R} scale {scale}");
        return UnityMatrix.TRS(position, UnityQuaternion.AngleAxisY(angle), scale);
    }

    private static Vector2 RandomTiling(Random random) => random.Next(3) switch
    {
        0 => Vector2.One,
        1 => new Vector2(-1f, 1f),
        _ => new Vector2(Samples.Uniform(random, -2f, 2f), Samples.Uniform(random, -2f, 2f)),
    };

    private static float Size(Random random) => random.Next(10) switch
    {
        0 => 0f,
        1 => 0.005f,
        2 => 0.01f,
        _ => Samples.Uniform(random, 0.001f, 3f),
    };

    private static string Describe(float width, float height, bool flipped, bool backLift, bool twist)
        => string.Create(CultureInfo.InvariantCulture, $"mesh {width:R}x{height:R} flipped {flipped} backLift {backLift} twist {twist}");

    private static void Check(Mismatches mismatches, Vector2 expected, Vector2 actual, Func<string> input)
    {
        mismatches.Check(expected.X, actual.X, input);
        mismatches.Check(expected.Y, actual.Y, input);
    }

    private static void Check(Mismatches mismatches, Vector3 expected, Vector3 actual, Func<string> input)
    {
        mismatches.Check(expected.X, actual.X, input);
        mismatches.Check(expected.Y, actual.Y, input);
        mismatches.Check(expected.Z, actual.Z, input);
    }

    private static void Check(Mismatches mismatches, Vector4 expected, Vector4 actual, Func<string> input)
    {
        mismatches.Check(expected.X, actual.X, input);
        mismatches.Check(expected.Y, actual.Y, input);
        mismatches.Check(expected.Z, actual.Z, input);
        mismatches.Check(expected.W, actual.W, input);
    }
}
