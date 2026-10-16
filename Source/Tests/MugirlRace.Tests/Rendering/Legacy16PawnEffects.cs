using System.Linq;

namespace Mugirl.Tests.Rendering;

/// <summary>
/// 1.6 <c>ExtraOutlineRenderer.QuadInfo/Draw</c> 与 <c>LanceMotionBlur.Quad/Draw</c> 的 CPU 计算，按 1.6 源码逐句转写为显式的 Unity 公式
/// （Vector3.magnitude 为 double 开方后转 float，Matrix4x4.MultiplyVector 为托管公式，Mathf.Min/Max 为三元式）。作为移植的参照。
/// </summary>
internal static class Legacy16PawnEffects
{
    public sealed record OutlineQuad(Vector2 UvMin, Vector2 UvMax, Vector3 USpan, Vector3 VSpan);

    public sealed record BlurQuad(Vector2 Min, Vector2 Max, Vector3 U, Vector3 V);

    /// <summary>1.6 <c>QuadInfo(Mesh)</c>（四顶点、可读网格）。无效时返回 null。</summary>
    public static OutlineQuad? OutlineQuadInfo(Vector3[] vertices, Vector2[] uv)
    {
        Vector3 boundsMin = vertices.Aggregate(new Vector3(float.MaxValue), (a, v) => new Vector3(Min(a.X, v.X), Min(a.Y, v.Y), Min(a.Z, v.Z)));
        Vector3 boundsMax = vertices.Aggregate(new Vector3(float.MinValue), (a, v) => new Vector3(Max(a.X, v.X), Max(a.Y, v.Y), Max(a.Z, v.Z)));
        Vector3 size = boundsMax - boundsMin;
        if (size.X <= 0f || size.Z <= 0f || size.Y > 0.01f)
        {
            return null;
        }

        Vector2 uvMin = new(float.MaxValue);
        Vector2 uvMax = new(float.MinValue);
        for (int i = 0; i < 4; i++)
        {
            uvMin = new Vector2(Min(uvMin.X, uv[i].X), Min(uvMin.Y, uv[i].Y));
            uvMax = new Vector2(Max(uvMax.X, uv[i].X), Max(uvMax.Y, uv[i].Y));
        }

        if (uvMax.X <= uvMin.X || uvMax.Y <= uvMin.Y)
        {
            return null;
        }

        int origin = -1, uCorner = -1, vCorner = -1;
        for (int i = 0; i < 4; i++)
        {
            if (uv[i].Y == uvMin.Y)
            {
                if (uv[i].X == uvMin.X)
                {
                    origin = i;
                }
                else
                {
                    uCorner = i;
                }
            }
            else if (uv[i].X == uvMin.X)
            {
                vCorner = i;
            }
        }

        if (origin < 0 || uCorner < 0 || vCorner < 0)
        {
            return null;
        }

        Vector3 uSpan = vertices[uCorner] - vertices[origin];
        Vector3 vSpan = vertices[vCorner] - vertices[origin];
        uSpan.Y = vSpan.Y = 0f;
        return new OutlineQuad(uvMin, uvMax, uSpan, vSpan);
    }

    /// <summary>1.6 <c>ExtraOutlineRenderer.Draw</c> 中的参数计算。</summary>
    public static (Vector4 PaddingU, Vector4 PaddingV, Vector4 UvRect, Vector4 UvRadius, Vector4 St)? Outline(
        OutlineQuad quad, in UMatrix4x4 matrix, Vector2 tiling, Vector2 offset, float width)
    {
        Vector3 worldU = MultiplyVector(matrix, quad.USpan);
        Vector3 worldV = MultiplyVector(matrix, quad.VSpan);
        float sizeU = Magnitude(worldU), sizeV = Magnitude(worldV);
        if (sizeU < 0.0001f || sizeV < 0.0001f)
        {
            return null;
        }

        Vector3 paddingU = worldU * (width / sizeU);
        Vector3 paddingV = worldV * (width / sizeV);
        Vector2 a = Scale(quad.UvMin, tiling) + offset;
        Vector2 b = Scale(quad.UvMax, tiling) + offset;
        Vector2 min = new(Min(a.X, b.X), Min(a.Y, b.Y));
        Vector2 max = new(Max(a.X, b.X), Max(a.Y, b.Y));
        return (
            new Vector4(paddingU.X, paddingU.Y, paddingU.Z, 0f),
            new Vector4(paddingV.X, paddingV.Y, paddingV.Z, 0f),
            new Vector4(min.X, min.Y, max.X, max.Y),
            new Vector4(width / sizeU * (max.X - min.X), width / sizeV * (max.Y - min.Y), 0f, 0f),
            new Vector4(tiling.X, tiling.Y, offset.X, offset.Y));
    }

    /// <summary>1.6 <c>LanceMotionBlur.Quad(Mesh)</c>。无效时返回 null。</summary>
    public static BlurQuad? BlurQuadInfo(Vector3[] vertices, Vector2[] uv)
    {
        Vector2 min = new(float.MaxValue);
        Vector2 max = new(float.MinValue);
        for (int i = 0; i < 4; i++)
        {
            min = new Vector2(Min(min.X, uv[i].X), Min(min.Y, uv[i].Y));
            max = new Vector2(Max(max.X, uv[i].X), Max(max.Y, uv[i].Y));
        }

        int origin = -1, uCorner = -1, vCorner = -1;
        for (int i = 0; i < 4; i++)
        {
            // 1.6 的 Vector2 == 带 1e-5 容差；这里的 UV 只有 0 与 1，与精确比较相同。
            if (uv[i].X == min.X && uv[i].Y == min.Y)
            {
                origin = i;
            }
            else if (uv[i].X == max.X && uv[i].Y == min.Y)
            {
                uCorner = i;
            }
            else if (uv[i].X == min.X && uv[i].Y == max.Y)
            {
                vCorner = i;
            }
        }

        if (origin < 0 || uCorner < 0 || vCorner < 0)
        {
            return null;
        }

        Vector3 u = vertices[uCorner] - vertices[origin];
        Vector3 v = vertices[vCorner] - vertices[origin];
        u.Y = v.Y = 0f;
        return SqrMagnitude(u) > 0.0001f && SqrMagnitude(v) > 0.0001f ? new BlurQuad(min, max, u, v) : null;
    }

    /// <summary>1.6 <c>LanceMotionBlur.Draw</c> 中每层的参数计算。</summary>
    public static (Vector4 St, Vector4 UvRect, Vector4 BlurUv, Vector4 Travel, Vector4 PaddingU, Vector4 PaddingV)? Blur(
        BlurQuad quad, in UMatrix4x4 matrix, Vector2 tiling, Vector2 offset, Vector3 travel)
    {
        Vector3 worldU = MultiplyVector(matrix, quad.U);
        Vector3 worldV = MultiplyVector(matrix, quad.V);
        worldU.Y = worldV.Y = 0f;
        float determinant = worldU.X * worldV.Z - worldU.Z * worldV.X;
        if (Math.Abs(determinant) < 0.0001f)
        {
            return null;
        }

        float alongU = (travel.X * worldV.Z - travel.Z * worldV.X) / determinant;
        float alongV = (worldU.X * travel.Z - worldU.Z * travel.X) / determinant;
        Vector2 uvSpan = Scale(quad.Max - quad.Min, tiling);
        Vector2 a = Scale(quad.Min, tiling) + offset;
        Vector2 b = Scale(quad.Max, tiling) + offset;
        Vector2 uvMin = new(Min(a.X, b.X), Min(a.Y, b.Y));
        Vector2 uvMax = new(Max(a.X, b.X), Max(a.Y, b.Y));
        Vector3 paddingU = worldU * Math.Abs(alongU) * 0.5f;
        Vector3 paddingV = worldV * Math.Abs(alongV) * 0.5f;
        return (
            new Vector4(tiling.X, tiling.Y, offset.X, offset.Y),
            new Vector4(uvMin.X, uvMin.Y, uvMax.X, uvMax.Y),
            new Vector4(alongU * uvSpan.X, alongV * uvSpan.Y, 0f, 0f),
            new Vector4(travel.X, travel.Y, travel.Z, 0f),
            new Vector4(paddingU.X, paddingU.Y, paddingU.Z, 0f),
            new Vector4(paddingV.X, paddingV.Y, paddingV.Z, 0f));
    }

    // Unity 2022.3 Matrix4x4.MultiplyVector（托管）。
    private static Vector3 MultiplyVector(in UMatrix4x4 m, Vector3 v) => new(
        m.m00 * v.X + m.m01 * v.Y + m.m02 * v.Z,
        m.m10 * v.X + m.m11 * v.Y + m.m12 * v.Z,
        m.m20 * v.X + m.m21 * v.Y + m.m22 * v.Z);

    // Unity Vector3.magnitude：(float)Math.Sqrt(x*x + y*y + z*z)。
    private static float Magnitude(Vector3 v) => (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    private static float SqrMagnitude(Vector3 v) => v.X * v.X + v.Y * v.Y + v.Z * v.Z;

    private static Vector2 Scale(Vector2 a, Vector2 b) => new(a.X * b.X, a.Y * b.Y);

    private static float Min(float a, float b) => a < b ? a : b;

    private static float Max(float a, float b) => a > b ? a : b;
}
