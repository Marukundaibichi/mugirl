using System.Runtime.CompilerServices;

namespace Mugirl.Mathematics;

/// <summary>
/// 以 Unity 语义操作 <c>System.Numerics</c> 的 <see cref="Vector3"/> 与 <see cref="Vector2"/>（模组内部的向量类型）。
/// </summary>
/// <remarks>
/// <para>
/// <c>System.Numerics</c> 的 <c>+</c>、二元 <c>-</c>、一元 <c>-</c>、<c>* float</c>、<c>/ float</c> 以及向量逐分量 <c>*</c> 都是逐分量 IEEE 运算，
/// 与 Unity 运算符（含 <c>Vector2.Scale</c>/<c>Vector3.Scale</c>）逐位相同，直接使用即可。下列成员覆盖 <c>System.Numerics</c> 与 Unity 不同之处：
/// </para>
/// <list type="bullet">
/// <item><see cref="Vector3.Normalize(Vector3)"/> 对短向量除以零得 NaN；Unity 在模长不大于 1e-5 时返回零向量。</item>
/// <item><see cref="Vector3.Lerp(Vector3, Vector3, float)"/> 不钳制，按 <c>a·(1-t) + b·t</c> 计算，CPU 支持 FMA 时融合乘加。</item>
/// <item><see cref="Vector3.Min(Vector3, Vector3)"/>/<c>Max</c> 按 IEEE 处理 NaN 与有符号零；Unity 逐分量用 <c>Mathf.Min</c>/<c>Max</c> 的三元式。</item>
/// <item><c>==</c> 是精确比较；Unity 的 <c>==</c> 把距离小于 1e-5 的向量视为相等，且含 NaN 时永不相等。</item>
/// <item>一元 <c>-</c> 安全：Unity、UnityStubs 与 System.Numerics 都只翻转符号位（反编译器把 IL <c>neg</c> 显示成 <c>0f - x</c>，实际不是）。</item>
/// </list>
/// <para>模长与点积按标量写出，保证在任何 CPU 上都按 Unity 的求和顺序计算。</para>
/// </remarks>
public static class UnityVector
{
    /// <summary>Unity 的 <c>Vector3.kEpsilon</c>：模长不大于该值的向量归一化为零向量。</summary>
    public const float NormalizeEpsilon = 1E-05f;

    /// <summary>Unity <c>==</c> 视为相等的平方距离上限（<c>9.9999994E-11f</c>，即 float 的 (1e-5)²）。</summary>
    public const float EqualityToleranceSquared = 9.9999994E-11f;

    /// <summary>Unity <c>Vector3.right</c>（+X）。</summary>
    public static Vector3 Right => Vector3.UnitX;

    /// <summary>Unity <c>Vector3.left</c>（-X）。</summary>
    public static Vector3 Left => new(-1f, 0f, 0f);

    /// <summary>Unity <c>Vector3.up</c>（+Y），RimWorld 的高度轴。</summary>
    public static Vector3 Up => Vector3.UnitY;

    /// <summary>Unity <c>Vector3.down</c>（-Y）。</summary>
    public static Vector3 Down => new(0f, -1f, 0f);

    /// <summary>Unity <c>Vector3.forward</c>（+Z），RimWorld 地图的北方。</summary>
    public static Vector3 Forward => Vector3.UnitZ;

    /// <summary>Unity <c>Vector3.back</c>（-Z）。</summary>
    public static Vector3 Back => new(0f, 0f, -1f);

    // ------------------------------------------------------------------ Vector3

    /// <summary>点积，按 Unity 顺序从左到右求和 <c>x·x' + y·y' + z·z'</c>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>平方模长，Unity 的 <c>sqrMagnitude</c>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SqrMagnitude(Vector3 v) => v.X * v.X + v.Y * v.Y + v.Z * v.Z;

    /// <summary>模长，Unity 的 <c>magnitude</c>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Magnitude(Vector3 v) => MathF.Sqrt(SqrMagnitude(v));

    /// <summary>两点距离（Unity <c>Vector3.Distance</c>）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(Vector3 a, Vector3 b) => Magnitude(a - b);

    /// <summary>
    /// 与 <paramref name="v"/> 同向的单位向量；模长不大于 1e-5（含 NaN 分量）时为 <see cref="Vector3.Zero"/>。Unity 的 <c>normalized</c>；
    /// 实例方法 <c>v.Normalize()</c> 写成 <c>v = UnityVector.Normalized(v)</c>。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Normalized(Vector3 v)
    {
        float magnitude = Magnitude(v);
        return magnitude > NormalizeEpsilon ? v / magnitude : Vector3.Zero;
    }

    /// <summary>线性插值，<paramref name="t"/> 钳制到 [0, 1]，逐分量按 <c>a + (b - a)·t</c> 计算。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => LerpUnclamped(a, b, UnityMathf.Clamp01(t));

    /// <summary>不钳制的线性插值，逐分量 <c>a + (b - a)·t</c>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t)
        => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    /// <summary>逐分量最小值，Unity <c>Vector3.Min</c>（每个分量为 <c>a &lt; b ? a : b</c>）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Min(Vector3 a, Vector3 b)
        => new(UnityMathf.Min(a.X, b.X), UnityMathf.Min(a.Y, b.Y), UnityMathf.Min(a.Z, b.Z));

    /// <summary>逐分量最大值，Unity <c>Vector3.Max</c>（每个分量为 <c>a &gt; b ? a : b</c>）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Max(Vector3 a, Vector3 b)
        => new(UnityMathf.Max(a.X, b.X), UnityMathf.Max(a.Y, b.Y), UnityMathf.Max(a.Z, b.Z));

    /// <summary>
    /// 把 <paramref name="current"/> 向 <paramref name="target"/> 移动至多 <paramref name="maxDistanceDelta"/>（Unity 2022.3 公式）。
    /// 只在两点重合、或剩余距离在非负步长之内时返回 <paramref name="target"/>。UnityStubs 另外在距离 ≤ 1e-5 时直接返回目标。
    /// </summary>
    public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDistanceDelta)
    {
        float dx = target.X - current.X;
        float dy = target.Y - current.Y;
        float dz = target.Z - current.Z;
        float sqrDistance = dx * dx + dy * dy + dz * dz;
        if (sqrDistance == 0f || (maxDistanceDelta >= 0f && sqrDistance <= maxDistanceDelta * maxDistanceDelta))
        {
            return target;
        }

        float distance = MathF.Sqrt(sqrDistance);
        return new Vector3(
            current.X + dx / distance * maxDistanceDelta,
            current.Y + dy / distance * maxDistanceDelta,
            current.Z + dz / distance * maxDistanceDelta);
    }

    /// <summary>
    /// 两个方向之间的无符号夹角（度，Unity 2022.3 公式，单精度）。两模长之积小于 1e-15 时得 0。
    /// 平方模长之积按单精度计算，模长约 1e19 以上时与 Unity 一样溢出（UnityStubs 此处用双精度）。
    /// </summary>
    public static float Angle(Vector3 from, Vector3 to)
    {
        float denominator = MathF.Sqrt(SqrMagnitude(from) * SqrMagnitude(to));
        if (denominator < 1E-15f)
        {
            return 0f;
        }

        float cosine = UnityMathf.Clamp(Dot(from, to) / denominator, -1f, 1f);
        return (float)Math.Acos(cosine) * 57.29578f;
    }

    /// <summary>
    /// 从 <paramref name="from"/> 到 <paramref name="to"/> 的有符号夹角（度），符号取 <c>axis · (from × to)</c>；
    /// <paramref name="axis"/> 为向上时，在 RimWorld 的左手 Y 向上坐标系中顺时针（俯视）为正。
    /// </summary>
    public static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis)
    {
        float angle = Angle(from, to);
        float crossX = from.Y * to.Z - from.Z * to.Y;
        float crossY = from.Z * to.X - from.X * to.Z;
        float crossZ = from.X * to.Y - from.Y * to.X;
        return angle * UnityMathf.Sign(axis.X * crossX + axis.Y * crossY + axis.Z * crossZ);
    }

    /// <summary>叉积（与 Unity 及 <see cref="Vector3.Cross"/> 分量公式相同）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Cross(Vector3 a, Vector3 b)
        => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    /// <summary>Unity 的 <c>Vector3 ==</c>：两点距离小于 1e-5 为真；任一分量为 NaN 得假。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ApproximatelyEqual(Vector3 a, Vector3 b) => SqrMagnitude(a - b) < EqualityToleranceSquared;

    /// <summary>Unity 的 <c>v == Vector3.zero</c>：模长小于 1e-5 为真。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsApproximatelyZero(Vector3 v) => SqrMagnitude(v) < EqualityToleranceSquared;

    /// <summary>
    /// [0, 360) 内的朝向角（度）：0 为 +Z（北），90 为 +X（东）；没有水平分量的向量得 0。
    /// 与 RimWorld 1.7 Verse 的 <c>Vector3Utility.AngleFlat</c>（<c>LookRotation(v).eulerAngles.y</c>）逐位一致。
    /// </summary>
    public static float AngleFlat(Vector3 v)
        => v.X == 0f && v.Z == 0f ? 0f : UnityQuaternion.EulerAngles(UnityQuaternion.LookRotation(v)).Y;

    /// <summary>
    /// 绕 +Y 旋转 <paramref name="v"/> <paramref name="angleDegrees"/> 度（俯视顺时针），与 RimWorld 1.7 Verse 的
    /// <c>Vector3Utility.RotatedBy(Vector3, float)</c> 逐位一致。1.7 原版经 UnityStubs 把 <c>AngleAxis * v</c> 算成
    /// System.Numerics 的夹心积，这里照此复现；需要 Unity 的矩阵公式时用 <see cref="UnityQuaternion.Rotate"/>。
    /// </summary>
    // Vector3.Transform(Vector3, Quaternion) 在 BannedSymbols.txt 中禁用，因为它不是 Unity 的 q * v；
    // 但它正是 1.7 原版执行的运算，所以这两个辅助方法是唯一允许的调用点。
#pragma warning disable RS0030 // 有意调用被禁用的 API，原因见上。
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 RotatedBy(Vector3 v, float angleDegrees) => Vector3.Transform(v, UnityQuaternion.AngleAxisY(angleDegrees));

    /// <summary>朝向角（度，0 = +Z/北，90 = +X/东）对应的 XZ 平面单位向量；与 1.7 Verse 的 <c>Vector3Utility.HorizontalVectorFromAngle</c> 逐位一致。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 HorizontalVectorFromAngle(float angleDegrees) => Vector3.Transform(Vector3.UnitZ, UnityQuaternion.AngleAxisY(angleDegrees));
#pragma warning restore RS0030

    /// <summary>Y 置零的副本（Verse <c>Vector3Utility.Yto0</c>）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Yto0(this Vector3 v) => new(v.X, 0f, v.Z);

    /// <summary>XZ 平面内的长度，Verse <c>GenGeo.MagnitudeHorizontal</c>（原版以双精度开方后舍入一次，结果逐位相同）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float MagnitudeHorizontal(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    /// <summary>XZ 平面内的平方长度，Verse <c>GenGeo.MagnitudeHorizontalSquared</c>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float MagnitudeHorizontalSquared(Vector3 v) => v.X * v.X + v.Z * v.Z;

    // ------------------------------------------------------------------ Vector2

    /// <summary>点积 <c>x·x' + y·y'</c>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(Vector2 a, Vector2 b) => a.X * b.X + a.Y * b.Y;

    /// <summary>平方模长。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SqrMagnitude(Vector2 v) => v.X * v.X + v.Y * v.Y;

    /// <summary>模长。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Magnitude(Vector2 v) => MathF.Sqrt(SqrMagnitude(v));

    /// <summary>两点距离。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(Vector2 a, Vector2 b) => Magnitude(a - b);

    /// <summary>单位向量；模长不大于 1e-5 时为零向量。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Normalized(Vector2 v)
    {
        float magnitude = Magnitude(v);
        return magnitude > NormalizeEpsilon ? v / magnitude : Vector2.Zero;
    }

    /// <summary>钳制的线性插值 <c>a + (b - a)·t</c>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => LerpUnclamped(a, b, UnityMathf.Clamp01(t));

    /// <summary>不钳制的线性插值 <c>a + (b - a)·t</c>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t)
        => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    /// <summary>逐分量最小值，Unity <c>Vector2.Min</c>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Min(Vector2 a, Vector2 b) => new(UnityMathf.Min(a.X, b.X), UnityMathf.Min(a.Y, b.Y));

    /// <summary>逐分量最大值，Unity <c>Vector2.Max</c>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Max(Vector2 a, Vector2 b) => new(UnityMathf.Max(a.X, b.X), UnityMathf.Max(a.Y, b.Y));

    /// <summary>Unity 的 <c>Vector2 ==</c>：距离小于 1e-5。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ApproximatelyEqual(Vector2 a, Vector2 b) => SqrMagnitude(a - b) < EqualityToleranceSquared;
}
