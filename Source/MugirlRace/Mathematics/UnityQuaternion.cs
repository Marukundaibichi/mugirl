using System.Runtime.CompilerServices;

namespace Mugirl.Mathematics;

/// <summary>
/// Unity 语义的旋转运算，把 <c>System.Numerics</c> 的 <see cref="Quaternion"/> 当作纯数据（<c>X, Y, Z, W</c> 对应 Unity 的 <c>x, y, z, w</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 需要 Unity 兼容结果时不要用 <c>System.Numerics.Quaternion</c> 的 <c>*</c> 或 <see cref="Vector3.Transform(Vector3, Quaternion)"/>：
/// .NET 10 的乘积中 Y、Z 分量的求和顺序与 Unity 不同（约四分之一的乘积最低位不同）；<c>Transform</c> 是夹心积 <c>q·v·q*</c>，
/// 对单位四元数与 Unity 的旋转矩阵公式差在最低位，对非单位四元数还会按 |q|² 缩放。UnityStubs 两者都用，所以 1.7 原版此处已与 1.6 不同。
/// </para>
/// <para>
/// 约定（与 Unity 相同）：左手系，Y 向上；<c>a * b</c> 先施加 <c>b</c> 再施加 <c>a</c>；
/// <see cref="Euler(float, float, float)"/> 依次绕 Z、X、Y 旋转（<c>q = qY · qX · qZ</c>）。
/// </para>
/// <para>
/// Unity 2022.3 中为托管实现的成员（<see cref="Multiply"/>、<see cref="Rotate"/>）照抄 Unity 公式。
/// Unity 中为原生实现的成员（<see cref="AngleAxis"/>、<see cref="Euler(float, float, float)"/>、<see cref="EulerAngles"/>、
/// <see cref="LookRotation(Vector3, Vector3)"/>）跟随 RimWorld 1.7 的 UnityStubs/Waylith 公式，与 1.7 原版逐位一致
/// （与 Unity 原生相差 ≤ 4.8e-7）；它们都不涉及融合乘加，结果与 CPU 无关。
/// </para>
/// </remarks>
public static class UnityQuaternion
{
    /// <summary>平方长度不大于该值（1e-12）时，UnityStubs/Waylith 视为退化并改用单位四元数。</summary>
    public const float DegenerateLengthSquared = 1E-12f;

    /// <summary>轴的平方长度不大于该值时 <see cref="AngleAxis"/> 返回单位四元数（UnityStubs：<c>axis.sqrMagnitude &lt;= 1e-5</c>）。</summary>
    public const float DegenerateAxisLengthSquared = 1E-05f;

    /// <summary>单位旋转，Unity <c>Quaternion.identity</c>。</summary>
    public static Quaternion Identity => Quaternion.Identity;

    /// <summary>
    /// 绕 <paramref name="axis"/>（内部归一化）旋转 <paramref name="angleDegrees"/> 度。轴的平方长度 ≤ 1e-5 时返回单位四元数。
    /// 与 UnityStubs（<c>Quaternion.CreateFromAxisAngle(axis.normalized, angle * Deg2Rad)</c>）逐位一致。
    /// </summary>
    public static Quaternion AngleAxis(float angleDegrees, Vector3 axis)
    {
        if (UnityVector.SqrMagnitude(axis) <= DegenerateAxisLengthSquared)
        {
            return Quaternion.Identity;
        }

        Vector3 unitAxis = UnityVector.Normalized(axis);
        float halfAngle = angleDegrees * UnityMathf.Deg2Rad * 0.5f;
        (float sin, float cos) = MathF.SinCos(halfAngle);
        return new Quaternion(unitAxis.X * sin, unitAxis.Y * sin, unitAxis.Z * sin, cos);
    }

    /// <summary>
    /// 绕 Y（高度）轴旋转，等价于 <c>AngleAxis(angleDegrees, Vector3.up)</c> 与 Verse 的 <c>GenMath.ToQuat</c>。
    /// 正角度俯视顺时针（北 → 东）。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion AngleAxisY(float angleDegrees)
    {
        (float sin, float cos) = MathF.SinCos(angleDegrees * UnityMathf.Deg2Rad * 0.5f);
        // 0f * sin 保留与 UnityStubs 相同的有符号零与 NaN 传播。
        return new Quaternion(0f * sin, 1f * sin, 0f * sin, cos);
    }

    /// <summary>
    /// 欧拉角（度）构造旋转：先 Z、再 X、最后 Y（<c>qY · qX · qZ</c>）。与 UnityStubs 逐位一致：
    /// UnityStubs 的每次 <c>*</c> 都是 System.Numerics 乘积再归一化，最后再归一化一次。
    /// </summary>
    public static Quaternion Euler(float x, float y, float z)
    {
        Quaternion qx = AngleAxis(x, UnityVector.Right);
        Quaternion qy = AngleAxis(y, UnityVector.Up);
        Quaternion qz = AngleAxis(z, UnityVector.Forward);
        Quaternion yx = NormalizeSafe(StubsProduct(qy, qx));
        Quaternion yxz = NormalizeSafe(StubsProduct(yx, qz));
        return NormalizeSafe(yxz);
    }

    /// <summary>以向量传入角度（度）的 <see cref="Euler(float, float, float)"/>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion Euler(Vector3 eulerDegrees) => Euler(eulerDegrees.X, eulerDegrees.Y, eulerDegrees.Z);

    /// <summary>
    /// Hamilton 积 <c>lhs · rhs</c>（先施加 <paramref name="rhs"/>，再施加 <paramref name="lhs"/>），使用 Unity 2022.3 的标量公式，
    /// 不重新归一化（UnityStubs 会归一化；单位四元数输入时只差舍入误差）。替代 Unity 的 <c>Quaternion * Quaternion</c>。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion Multiply(Quaternion lhs, Quaternion rhs) => new(
        lhs.W * rhs.X + lhs.X * rhs.W + lhs.Y * rhs.Z - lhs.Z * rhs.Y,
        lhs.W * rhs.Y + lhs.Y * rhs.W + lhs.Z * rhs.X - lhs.X * rhs.Z,
        lhs.W * rhs.Z + lhs.Z * rhs.W + lhs.X * rhs.Y - lhs.Y * rhs.X,
        lhs.W * rhs.W - lhs.X * rhs.X - lhs.Y * rhs.Y - lhs.Z * rhs.Z);

    /// <summary>
    /// 用 <paramref name="rotation"/> 旋转 <paramref name="point"/>（Unity 的 <c>rotation * point</c>），使用 Unity 的旋转矩阵公式。
    /// 与 Unity 一样，非单位四元数时不是夹心积，也不按比例缩放。
    /// </summary>
    public static Vector3 Rotate(Quaternion rotation, Vector3 point)
    {
        float x2 = rotation.X * 2f;
        float y2 = rotation.Y * 2f;
        float z2 = rotation.Z * 2f;
        float xx = rotation.X * x2;
        float yy = rotation.Y * y2;
        float zz = rotation.Z * z2;
        float xy = rotation.X * y2;
        float xz = rotation.X * z2;
        float yz = rotation.Y * z2;
        float wx = rotation.W * x2;
        float wy = rotation.W * y2;
        float wz = rotation.W * z2;
        return new Vector3(
            (1f - (yy + zz)) * point.X + (xy - wz) * point.Y + (xz + wy) * point.Z,
            (xy + wz) * point.X + (1f - (xx + zz)) * point.Y + (yz - wx) * point.Z,
            (xz - wy) * point.X + (yz + wx) * point.Y + (1f - (xx + yy)) * point.Z);
    }

    // UnityStubs 的 Quaternion * 调用 System.Numerics 的乘积：Y、Z 分量的求和顺序与 Unity 不同，W 与 X 相同。
    // 乘数为 ±1 的融合乘加与否结果相同，所以与 CPU 无关。这里只为复现 Euler；Mod 的四元数乘法用 Multiply。
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Quaternion StubsProduct(Quaternion lhs, Quaternion rhs) => Quaternion.Multiply(lhs, rhs);

    /// <summary>四分量点积，从左到右求和。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(Quaternion a, Quaternion b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

    /// <summary>
    /// 单位化；平方长度 ≤ 1e-12 时得单位四元数（UnityStubs/Waylith 的 <c>Normalize</c> 规则）。
    /// 与 UnityStubs 相同，含 NaN 分量时结果仍为 NaN。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion NormalizeSafe(Quaternion q)
    {
        // 有意使用 BCL 的 Dot/Normalize：UnityStubs 与 Waylith 调用的正是它们（SIMD 求和顺序与标量从左到右不同，差在最低位），
        // 这样 TRS、Euler、EulerAngles 才与 1.7 原版逐位一致。两者都不涉及融合乘加。
        if (Quaternion.Dot(q, q) <= DegenerateLengthSquared)
        {
            return Quaternion.Identity;
        }

        return Quaternion.Normalize(q);
    }

    /// <summary>
    /// +Z 轴指向 <paramref name="forward"/>、+Y 轴位于 <paramref name="forward"/> 与 <paramref name="upwards"/> 所在平面的旋转，
    /// 与 RimWorld 1.7 UnityStubs 的 <c>Quaternion.LookRotation</c> 逐位一致（Unity 的是原生实现）。退化输入回退到单位旋转或世界轴。
    /// </summary>
    public static Quaternion LookRotation(Vector3 forward, Vector3 upwards)
    {
        const float Tiny = 9.9999994E-11f;
        if (forward.LengthSquared() <= Tiny)
        {
            return Quaternion.Identity;
        }

        // 有意使用 System.Numerics 的 Normalize/Cross/CreateFromRotationMatrix：UnityStubs 执行的就是它们，
        // 而且 Normalize 的输入都已确认长于 Unity 的 1e-5 阈值，不会出现 NaN。
#pragma warning disable RS0030 // 有意调用被禁用的 API，原因见上。
        Vector3 z = Vector3.Normalize(forward);
        Vector3 x = Vector3.Cross(upwards.LengthSquared() <= Tiny ? Vector3.UnitY : Vector3.Normalize(upwards), z);
        if (x.LengthSquared() <= Tiny)
        {
            x = Vector3.Cross(Vector3.UnitY, z);
            if (x.LengthSquared() <= Tiny)
            {
                x = Vector3.UnitX;
            }
        }

        x = Vector3.Normalize(x);
#pragma warning restore RS0030
        Vector3 y = Vector3.Cross(z, x);
        return Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            x.X, x.Y, x.Z, 0f,
            y.X, y.Y, y.Z, 0f,
            z.X, z.Y, z.Z, 0f,
            0f, 0f, 0f, 1f));
    }

    /// <summary>以世界向上为 up 的 <see cref="LookRotation(Vector3, Vector3)"/>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion LookRotation(Vector3 forward) => LookRotation(forward, Vector3.UnitY);

    /// <summary>
    /// 欧拉角（度），各分量回绕到 [0, 360)，使用 RimWorld 1.7 UnityStubs 的公式（Unity 的是原生实现，在 0/360 边界可能差 360）。
    /// <c>Y</c> 是 RimWorld 使用的朝向（0 = 北/+Z，90 = 东/+X）。替代 Unity 的 <c>Quaternion.eulerAngles</c>。
    /// </summary>
    public static Vector3 EulerAngles(Quaternion rotation)
    {
        Quaternion q = NormalizeSafe(rotation);
        float sinPitch = 2f * (q.X * q.W - q.Y * q.Z);
        float pitch;
        float yaw;
        float roll;
        if (MathF.Abs(sinPitch) < 0.9999995f)
        {
            pitch = MathF.Asin(UnityMathf.Clamp(sinPitch, -1f, 1f));
            yaw = MathF.Atan2(2f * (q.X * q.Z + q.Y * q.W), 1f - 2f * (q.X * q.X + q.Y * q.Y));
            roll = MathF.Atan2(2f * (q.X * q.Y + q.Z * q.W), 1f - 2f * (q.X * q.X + q.Z * q.Z));
        }
        else
        {
            pitch = MathF.CopySign(UnityMathf.PI / 2f, sinPitch);
            float a = 2f * (q.X * q.Y - q.Z * q.W);
            float b = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
            yaw = MathF.Atan2(sinPitch > 0f ? a : -a, b);
            roll = 0f;
        }

        return new Vector3(Wrap360(pitch * UnityMathf.Rad2Deg), Wrap360(yaw * UnityMathf.Rad2Deg), Wrap360(roll * UnityMathf.Rad2Deg));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Wrap360(float degrees)
    {
        degrees %= 360f;
        return degrees < 0f ? degrees + 360f : degrees;
    }
}
