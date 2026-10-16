using Mugirl.Features.Incidents;
using Verse;

namespace Mugirl.Tests.Incidents;

/// <summary>
/// <see cref="PunisherArcMath"/>：惩罚者战利品飞散弧线。与按 Unity 1.6 公式独立推导的结果逐项比较，
/// 并与 1.6 源码在 1.7 兼容层上的实际结果（UnityStubs 向量运算 + Verse RotatedBy）在视觉容差内比较。
/// </summary>
public sealed class PunisherArcMathTests
{
    private const float BaseAltitude = 7.5f;
    private const float Tolerance = 2E-05f;

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(27, 1f)]
    [InlineData(40, 1f)]
    [InlineData(-3, 0f)]
    public void Progress_ClampsToUnitRange(int age, float expected)
        => Bits.Equal(expected, PunisherArcMath.Progress(age));

    [Fact]
    public void Progress_DividesByLastTick()
        => Bits.Equal(13 / 27f, PunisherArcMath.Progress(13));

    [Fact]
    public void Evaluate_StartsAtOriginAndLandsOnTarget()
    {
        Vector3 origin = new Vector3(10.5f, 0f, 20.5f);
        Vector3 target = new Vector3(14.5f, 0f, 17.5f);

        Vector3 start = PunisherArcMath.Evaluate(origin, target, 0f, 0.3f, 1.1f, BaseAltitude);
        Bits.Equal(origin.X, start.X, "x at t=0");
        Bits.Equal(origin.Z, start.Z, "z at t=0");
        Bits.Equal(BaseAltitude + 0.04f, start.Y, "altitude at t=0");

        // sin(PI) 在单精度下约为 -8.7e-8，落点与目标只差这一点。
        Vector3 end = PunisherArcMath.Evaluate(origin, target, 1f, 0.3f, 1.1f, BaseAltitude);
        Assert.InRange(MathF.Abs(end.X - target.X), 0f, 1E-06f);
        Assert.InRange(MathF.Abs(end.Z - target.Z), 0f, 1E-06f);
    }

    [Fact]
    public void Evaluate_DegeneratePathSwingsAlongPositiveX()
    {
        Vector3 cell = new Vector3(3.5f, 0f, 3.5f);
        Vector3 mid = PunisherArcMath.Evaluate(cell, cell, 0.5f, 0.4f, 0f, BaseAltitude);
        float lift = UnityMathf.Sin(0.5f * UnityMathf.PI);
        Bits.Equal(3.5f + lift * 0.4f, mid.X);
        Bits.Equal(3.5f, mid.Z);
    }

    [Fact]
    public void Evaluate_MatchesUnityFormula()
    {
        var mismatches = new Mismatches("PunisherArcMath.Evaluate vs Unity formula");
        var random = new Random(1729);
        for (int i = 0; i < 4000; i++)
        {
            Vector3 origin = new Vector3(Samples.Uniform(random, 0f, 250f), 0f, Samples.Uniform(random, 0f, 250f));
            Vector3 target = origin + new Vector3(Samples.Uniform(random, -8f, 8f), 0f, Samples.Uniform(random, -8f, 8f));
            float t = Samples.Uniform(random, 0f, 1f);
            float swing = Samples.Uniform(random, -0.42f, 0.42f);
            float height = Samples.Uniform(random, 0.65f, 1.35f);

            Vector3 actual = PunisherArcMath.Evaluate(origin, target, t, swing, height, BaseAltitude);
            Vector3 expected = UnityFormula(origin, target, t, swing, height);
            string Input() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"o={origin} tg={target} t={t:R}");
            mismatches.Check(true, Close(expected.X, actual.X), Input);
            mismatches.Check(true, Close(expected.Z, actual.Z), Input);
            float altitude = BaseAltitude;
            mismatches.Check(altitude + 0.04f + UnityMathf.Sin(t * UnityMathf.PI) * 0.10f, actual.Y, Input);
        }

        mismatches.AssertNone();
    }

    [Fact]
    public void Evaluate_MatchesOldCompatLayerResultVisually()
    {
        var mismatches = new Mismatches("PunisherArcMath.Evaluate vs 1.6 code on UnityStubs");
        var random = new Random(4242);
        for (int i = 0; i < 4000; i++)
        {
            Vector3 origin = new Vector3(Samples.Uniform(random, 0f, 250f), 0f, Samples.Uniform(random, 0f, 250f));
            Vector3 target = origin + new Vector3(Samples.Uniform(random, -8f, 8f), 0f, Samples.Uniform(random, -8f, 8f));
            float t = Samples.Uniform(random, 0f, 1f);
            float swing = Samples.Uniform(random, -0.42f, 0.42f);
            float height = Samples.Uniform(random, 0.65f, 1.35f);

            Vector3 actual = PunisherArcMath.Evaluate(origin, target, t, swing, height, BaseAltitude);
            UVector3 old = OldEvaluate(origin.ToUnity(), target.ToUnity(), t, swing, height);
            string Input() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"o={origin} tg={target} t={t:R}");
            mismatches.Check(true, Close(old.x, actual.X), Input);
            mismatches.Check(true, Close(old.y, actual.Y), Input);
            mismatches.Check(true, Close(old.z, actual.Z), Input);
        }

        mismatches.AssertNone();
    }

    // Unity 1.6：Lerp 逐分量 a + (b - a) * t；normalized = v / |v|；绕 +Y 顺时针 90°（俯视）把 (x, z) 变为 (z, -x)；Sin 为双精度。
    private static Vector3 UnityFormula(Vector3 origin, Vector3 target, float t, float swing, float height)
    {
        float lx = origin.X + (target.X - origin.X) * t;
        float lz = origin.Z + (target.Z - origin.Z) * t;
        float px = target.X - origin.X;
        float pz = target.Z - origin.Z;
        float nx = 1f;
        float nz = 0f;
        if (px * px + pz * pz > 0.001f)
        {
            float magnitude = (float)Math.Sqrt(px * px + pz * pz);
            nx = pz / magnitude;
            nz = -px / magnitude;
        }

        float lift = (float)Math.Sin(t * 3.14159274f);
        return new Vector3(lx + nx * lift * swing, 0f, lz + nz * lift * swing + lift * height);
    }

    // 1.6 源码原样（PunisherLootBurst.EvaluateArcPos），在 1.7 上运行于 UnityStubs 与 Verse。
    private static UVector3 OldEvaluate(UVector3 origin, UVector3 target, float t, float swing, float height)
    {
        UVector3 linear = UVector3.Lerp(origin, target, t);
        UVector3 path = target - origin;
        path.y = 0f;
        UVector3 normal = path.MagnitudeHorizontalSquared() > 0.001f
            ? path.normalized.RotatedBy(90f)
            : new UVector3(1f, 0f, 0f);
        linear += normal * UMathf.Sin(t * UMathf.PI) * swing;
        linear += UVector3.forward * UMathf.Sin(t * UMathf.PI) * height;
        linear.y = BaseAltitude + 0.04f + UMathf.Sin(t * UMathf.PI) * 0.10f;
        return linear;
    }

    private static bool Close(float expected, float actual)
        => MathF.Abs(expected - actual) <= Tolerance * MathF.Max(1f, MathF.Abs(expected));
}
