using System.Runtime.CompilerServices;

namespace Mugirl.Mathematics;

/// <summary>
/// 只通过字段赋值（不调用 UnityStubs）构造交给 RimWorld 绘制 API 的 <c>UnityEngine.Matrix4x4</c>，
/// 并在它与 Waylith 使用的 <c>System.Numerics.Matrix4x4</c> 布局之间转换。
/// </summary>
/// <remarks>
/// <para>
/// Unity 矩阵使用列向量（<c>p' = M · p</c>）；字段 <c>m&lt;行&gt;&lt;列&gt;</c>，平移在 <c>m03, m13, m23</c>。Waylith 把同样的数放进
/// <c>System.Numerics.Matrix4x4</c>，<c>M(r+1)(c+1) = m(r)(c)</c>，即平移在 <c>M14, M24, M34</c>（见 UnityStubs <c>Matrix4x4.ToNumerics</c>、
/// <c>Waylith.Math.Matrix4x4Extensions.TRS</c> 与 <c>Waylith.Rendering.MeshDrawCommand</c>）。这是标准 System.Numerics 约定
/// （行向量、<c>Vector3.Transform(p, M)</c>、平移在 <c>M41..M43</c>）的转置：<c>Matrix4x4.CreateTranslation/CreateFromQuaternion/CreateScale</c>
/// 的结果不转置就交给 Waylith，绘制结果不可见且没有任何报错。
/// </para>
/// <para>乘法类成员（<see cref="Multiply(in UMatrix4x4, in UMatrix4x4)"/>、<see cref="MultiplyPoint"/> 等）照抄 Unity 2022.3 托管公式，逐位一致。</para>
/// </remarks>
public static class UnityMatrix
{
    /// <summary>
    /// 平移 · 旋转 · 缩放，按 UnityStubs/Waylith 的方式布局：先归一化旋转（退化时用单位旋转），每个旋转列乘以对应的缩放分量，
    /// 平移写入最后一列。替代 Unity 的 <c>Matrix4x4.TRS</c>。
    /// </summary>
    /// <param name="position">平移。</param>
    /// <param name="rotation">旋转；不必是单位四元数。1.6 的 Unity 原生 TRS 不归一化，传入非单位四元数时 1.6 带额外缩放，1.7 没有。</param>
    /// <param name="scale">在旋转之前施加的逐轴缩放。</param>
    public static UMatrix4x4 TRS(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        // 旋转项采用 System.Numerics CreateFromQuaternion 的运算顺序（UnityStubs 对它转置）：1 - 2(y² + z²)、2(xy + zw) ……
        // Unity 托管的 Matrix4x4.Rotate 写作 1 - (2y² + 2z²)；两者除乘积为次正规数外逐位相同，次正规时以 1.7 原版的顺序为准。
        Quaternion q = UnityQuaternion.NormalizeSafe(rotation);
        float xx = q.X * q.X;
        float yy = q.Y * q.Y;
        float zz = q.Z * q.Z;
        float xy = q.X * q.Y;
        float zw = q.Z * q.W;
        float zx = q.Z * q.X;
        float yw = q.Y * q.W;
        float yz = q.Y * q.Z;
        float xw = q.X * q.W;

        UMatrix4x4 m = default;
        m.m00 = (1f - 2f * (yy + zz)) * scale.X;
        m.m10 = 2f * (xy + zw) * scale.X;
        m.m20 = 2f * (zx - yw) * scale.X;
        m.m01 = 2f * (xy - zw) * scale.Y;
        m.m11 = (1f - 2f * (zz + xx)) * scale.Y;
        m.m21 = 2f * (yz + xw) * scale.Y;
        m.m02 = 2f * (zx + yw) * scale.Z;
        m.m12 = 2f * (yz - xw) * scale.Z;
        m.m22 = (1f - 2f * (yy + xx)) * scale.Z;
        m.m03 = position.X;
        m.m13 = position.Y;
        m.m23 = position.Z;
        m.m33 = 1f;
        return m;
    }

    /// <summary>只含平移的矩阵，Unity <c>Matrix4x4.Translate</c>。</summary>
    public static UMatrix4x4 Translate(Vector3 position)
    {
        UMatrix4x4 m = default;
        m.m00 = 1f;
        m.m11 = 1f;
        m.m22 = 1f;
        m.m33 = 1f;
        m.m03 = position.X;
        m.m13 = position.Y;
        m.m23 = position.Z;
        return m;
    }

    /// <summary>只含缩放的矩阵，Unity <c>Matrix4x4.Scale</c>。</summary>
    public static UMatrix4x4 Scale(Vector3 scale)
    {
        UMatrix4x4 m = default;
        m.m00 = scale.X;
        m.m11 = scale.Y;
        m.m22 = scale.Z;
        m.m33 = 1f;
        return m;
    }

    /// <summary>单位矩阵，Unity <c>Matrix4x4.identity</c>。</summary>
    public static UMatrix4x4 Identity
    {
        get
        {
            UMatrix4x4 m = default;
            m.m00 = 1f;
            m.m11 = 1f;
            m.m22 = 1f;
            m.m33 = 1f;
            return m;
        }
    }

    /// <summary>
    /// 矩阵乘积 <c>lhs · rhs</c>（先施加 <paramref name="rhs"/>），Unity <c>Matrix4x4 * Matrix4x4</c> 的托管公式，逐项从左到右求和。
    /// </summary>
    public static UMatrix4x4 Multiply(in UMatrix4x4 lhs, in UMatrix4x4 rhs)
    {
        UMatrix4x4 r = default;
        r.m00 = lhs.m00 * rhs.m00 + lhs.m01 * rhs.m10 + lhs.m02 * rhs.m20 + lhs.m03 * rhs.m30;
        r.m01 = lhs.m00 * rhs.m01 + lhs.m01 * rhs.m11 + lhs.m02 * rhs.m21 + lhs.m03 * rhs.m31;
        r.m02 = lhs.m00 * rhs.m02 + lhs.m01 * rhs.m12 + lhs.m02 * rhs.m22 + lhs.m03 * rhs.m32;
        r.m03 = lhs.m00 * rhs.m03 + lhs.m01 * rhs.m13 + lhs.m02 * rhs.m23 + lhs.m03 * rhs.m33;
        r.m10 = lhs.m10 * rhs.m00 + lhs.m11 * rhs.m10 + lhs.m12 * rhs.m20 + lhs.m13 * rhs.m30;
        r.m11 = lhs.m10 * rhs.m01 + lhs.m11 * rhs.m11 + lhs.m12 * rhs.m21 + lhs.m13 * rhs.m31;
        r.m12 = lhs.m10 * rhs.m02 + lhs.m11 * rhs.m12 + lhs.m12 * rhs.m22 + lhs.m13 * rhs.m32;
        r.m13 = lhs.m10 * rhs.m03 + lhs.m11 * rhs.m13 + lhs.m12 * rhs.m23 + lhs.m13 * rhs.m33;
        r.m20 = lhs.m20 * rhs.m00 + lhs.m21 * rhs.m10 + lhs.m22 * rhs.m20 + lhs.m23 * rhs.m30;
        r.m21 = lhs.m20 * rhs.m01 + lhs.m21 * rhs.m11 + lhs.m22 * rhs.m21 + lhs.m23 * rhs.m31;
        r.m22 = lhs.m20 * rhs.m02 + lhs.m21 * rhs.m12 + lhs.m22 * rhs.m22 + lhs.m23 * rhs.m32;
        r.m23 = lhs.m20 * rhs.m03 + lhs.m21 * rhs.m13 + lhs.m22 * rhs.m23 + lhs.m23 * rhs.m33;
        r.m30 = lhs.m30 * rhs.m00 + lhs.m31 * rhs.m10 + lhs.m32 * rhs.m20 + lhs.m33 * rhs.m30;
        r.m31 = lhs.m30 * rhs.m01 + lhs.m31 * rhs.m11 + lhs.m32 * rhs.m21 + lhs.m33 * rhs.m31;
        r.m32 = lhs.m30 * rhs.m02 + lhs.m31 * rhs.m12 + lhs.m32 * rhs.m22 + lhs.m33 * rhs.m32;
        r.m33 = lhs.m30 * rhs.m03 + lhs.m31 * rhs.m13 + lhs.m32 * rhs.m23 + lhs.m33 * rhs.m33;
        return r;
    }

    /// <summary>四维向量变换，Unity <c>Matrix4x4 * Vector4</c>。</summary>
    public static Vector4 Multiply(in UMatrix4x4 m, Vector4 v) => new(
        m.m00 * v.X + m.m01 * v.Y + m.m02 * v.Z + m.m03 * v.W,
        m.m10 * v.X + m.m11 * v.Y + m.m12 * v.Z + m.m13 * v.W,
        m.m20 * v.X + m.m21 * v.Y + m.m22 * v.Z + m.m23 * v.W,
        m.m30 * v.X + m.m31 * v.Y + m.m32 * v.Z + m.m33 * v.W);

    /// <summary>
    /// Unity <c>MultiplyPoint</c>：完整的点变换，包括齐次除法（先求 <c>1 / w</c> 再逐分量相乘，与 Unity 相同）。
    /// 仿射矩阵用 <see cref="MultiplyPoint3x4"/> 更快，且结果相同（w 恰为 1 时）。
    /// </summary>
    public static Vector3 MultiplyPoint(in UMatrix4x4 m, Vector3 p)
    {
        float x = m.m00 * p.X + m.m01 * p.Y + m.m02 * p.Z + m.m03;
        float y = m.m10 * p.X + m.m11 * p.Y + m.m12 * p.Z + m.m13;
        float z = m.m20 * p.X + m.m21 * p.Y + m.m22 * p.Z + m.m23;
        float w = m.m30 * p.X + m.m31 * p.Y + m.m32 * p.Z + m.m33;
        w = 1f / w;
        return new Vector3(x * w, y * w, z * w);
    }

    /// <summary>Unity <c>MultiplyPoint3x4</c>：只施加 <paramref name="m"/> 的仿射部分（标量、Unity 运算顺序）。</summary>
    public static Vector3 MultiplyPoint3x4(in UMatrix4x4 m, Vector3 p) => new(
        m.m00 * p.X + m.m01 * p.Y + m.m02 * p.Z + m.m03,
        m.m10 * p.X + m.m11 * p.Y + m.m12 * p.Z + m.m13,
        m.m20 * p.X + m.m21 * p.Y + m.m22 * p.Z + m.m23);

    /// <summary>Unity <c>MultiplyVector</c>：只施加左上 3×3（方向向量，不含平移）。</summary>
    public static Vector3 MultiplyVector(in UMatrix4x4 m, Vector3 v) => new(
        m.m00 * v.X + m.m01 * v.Y + m.m02 * v.Z,
        m.m10 * v.X + m.m11 * v.Y + m.m12 * v.Z,
        m.m20 * v.X + m.m21 * v.Y + m.m22 * v.Z);

    /// <summary>
    /// 同一矩阵的 Waylith <c>System.Numerics.Matrix4x4</c> 布局（<c>M(r+1)(c+1) = m(r)(c)</c>），与 UnityStubs 的
    /// <c>Matrix4x4.ToNumerics</c> 相同。用于 Waylith 原生绘制 API。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix4x4 ToWaylithLayout(this UMatrix4x4 m) => new(
        m.m00, m.m01, m.m02, m.m03,
        m.m10, m.m11, m.m12, m.m13,
        m.m20, m.m21, m.m22, m.m23,
        m.m30, m.m31, m.m32, m.m33);

    /// <summary><see cref="ToWaylithLayout"/> 的逆转换。</summary>
    public static UMatrix4x4 FromWaylithLayout(in Matrix4x4 m)
    {
        UMatrix4x4 r = default;
        r.m00 = m.M11; r.m01 = m.M12; r.m02 = m.M13; r.m03 = m.M14;
        r.m10 = m.M21; r.m11 = m.M22; r.m12 = m.M23; r.m13 = m.M24;
        r.m20 = m.M31; r.m21 = m.M32; r.m22 = m.M33; r.m23 = m.M34;
        r.m30 = m.M41; r.m31 = m.M42; r.m32 = m.M43; r.m33 = m.M44;
        return r;
    }

    /// <summary>
    /// 同一变换的标准 System.Numerics 行向量约定（Waylith 布局的转置），用于 <see cref="Vector3.Transform(Vector3, Matrix4x4)"/> 等 BCL 矩阵 API。
    /// 注意 BCL 变换可能使用 FMA，与 <see cref="MultiplyPoint3x4"/> 相差约 2 ulp。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix4x4 ToNumericsConvention(this UMatrix4x4 m) => new(
        m.m00, m.m10, m.m20, m.m30,
        m.m01, m.m11, m.m21, m.m31,
        m.m02, m.m12, m.m22, m.m32,
        m.m03, m.m13, m.m23, m.m33);
}
