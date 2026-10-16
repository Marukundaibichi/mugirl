using System.Runtime.CompilerServices;

namespace Mugirl.Mathematics;

/// <summary>
/// 与 Unity 2022.3 <c>UnityEngine.Mathf</c>（<c>UnityEngine.CoreModule.dll</c> 中的托管 IL）语义一致的标量数学，
/// 只依赖 .NET 基础库。
/// </summary>
/// <remarks>
/// <para>
/// 1.7 不再调用 <c>UnityEngine.Mathf</c>（UnityStubs 实现，部分语义已变）。同名 BCL 成员也不能直接替换：
/// <see cref="Math.Clamp(float, float, float)"/> 在 <c>min &gt; max</c> 时抛异常，<see cref="float.Lerp"/> 不钳制且公式不同，
/// <see cref="MathF.Max(float, float)"/> 传播 NaN。<c>Waylith.Math.Mathf</c> 同样不行：它的 <c>SmoothStep</c> 是 HLSL 边界函数，
/// <c>Clamp</c> 会抛异常，整数转换是饱和转换。
/// </para>
/// <para>
/// 下列成员逐个运算照抄 Unity 公式，在 1.7 运行时上与 Unity 托管实现逐位一致（每步都舍入到 float）。
/// Unity 中为原生实现的成员（<see cref="PerlinNoise(float, float)"/>）改为跟随 UnityStubs，并在注释中说明。
/// </para>
/// </remarks>
public static class UnityMathf
{
    /// <summary>单精度 π（3.14159274）。</summary>
    public const float PI = (float)Math.PI;

    /// <summary>角度转弧度系数，<c>PI / 180</c> 按单精度折叠（0.0174532924）。</summary>
    public const float Deg2Rad = PI / 180f;

    /// <summary>弧度转角度系数，与 Unity 源码写法相同（57.29578）。</summary>
    public const float Rad2Deg = 57.29578f;

    /// <summary>正无穷。</summary>
    public const float Infinity = float.PositiveInfinity;

    /// <summary>负无穷。</summary>
    public const float NegativeInfinity = float.NegativeInfinity;

    /// <summary>
    /// Unity 的 <c>Mathf.Epsilon</c>：最小的正次正规数。Unity 只在开启 flush-to-zero 时改用 <c>float.MinNormal</c>，
    /// .NET 托管代码不会开启。
    /// </summary>
    public const float Epsilon = float.Epsilon;

    // ------------------------------------------------------------------ 基本函数

    /// <summary>绝对值。NaN 仍为 NaN，-0 变为 +0。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Abs(float value) => MathF.Abs(value);

    /// <summary>绝对值。与 Unity 相同，<see cref="int.MinValue"/> 抛 <see cref="OverflowException"/>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Abs(int value) => Math.Abs(value);

    /// <summary>平方根。与 Unity 的 <c>(float)Math.Sqrt(f)</c> 逐位一致：开方不存在二次舍入问题。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sqrt(float value) => MathF.Sqrt(value);

    /// <summary>正弦，按 Unity 的方式以双精度计算后舍入一次。UnityStubs 改用 <see cref="MathF.Sin"/>，约 0.1% 的输入差 1 ulp。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sin(float radians) => (float)Math.Sin(radians);

    /// <summary>余弦，双精度计算后舍入一次。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Cos(float radians) => (float)Math.Cos(radians);

    /// <summary>正切，双精度计算后舍入一次。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Tan(float radians) => (float)Math.Tan(radians);

    /// <summary><paramref name="y"/>/<paramref name="x"/> 的反正切，双精度计算后舍入一次。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);

    /// <summary>反余弦，双精度计算后舍入一次；超出 [-1, 1] 得 NaN。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Acos(float value) => (float)Math.Acos(value);

    /// <summary>反正弦，双精度计算后舍入一次；超出 [-1, 1] 得 NaN。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Asin(float value) => (float)Math.Asin(value);

    /// <summary>反正切，双精度计算后舍入一次。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Atan(float value) => (float)Math.Atan(value);

    /// <summary>幂，双精度计算后舍入一次。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Pow(float value, float power) => (float)Math.Pow(value, power);

    /// <summary>e 的 <paramref name="power"/> 次幂，双精度计算后舍入一次。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Exp(float power) => (float)Math.Exp(power);

    /// <summary>自然对数，双精度计算后舍入一次。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Log(float value) => (float)Math.Log(value);

    /// <summary>以 <paramref name="newBase"/> 为底的对数，双精度计算后舍入一次（Unity <c>Mathf.Log(f, p)</c>）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Log(float value, float newBase) => (float)Math.Log(value, newBase);

    /// <summary>以 10 为底的对数，双精度计算后舍入一次。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Log10(float value) => (float)Math.Log10(value);

    /// <summary>不大于 <paramref name="value"/> 的最大整数（精确，与 <c>(float)Math.Floor</c> 相同）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Floor(float value) => MathF.Floor(value);

    /// <summary>不小于 <paramref name="value"/> 的最小整数。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Ceil(float value) => MathF.Ceiling(value);

    /// <summary>四舍六入五成双（银行家舍入），与 Unity 相同。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Round(float value) => MathF.Round(value);

    /// <summary><paramref name="value"/> ≥ 0（含 -0）时为 <c>1</c>，否则为 <c>-1</c>；NaN 得 <c>-1</c>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sign(float value) => value >= 0f ? 1f : -1f;

    // ------------------------------------------------------------------ float → int

    /// <summary>
    /// 按 Mono 的转换规则截断 float → int：NaN、±∞ 与 [-2³¹, 2³¹) 之外的值一律得 <see cref="int.MinValue"/>。
    /// 1.7 运行时上的 <c>(int)</c> 转换是饱和转换（NaN 得 0，+∞ 得 <see cref="int.MaxValue"/>）。规则与 UnityStubs 私有的
    /// <c>MonoToInt</c> 相同。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToInt32Mono(float value)
        => value >= -2147483648f && value < 2147483648f ? (int)value : int.MinValue;

    /// <summary>银行家舍入到 <see cref="int"/>；NaN 与越界值得 <see cref="int.MinValue"/>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int RoundToInt(float value) => ToInt32Mono(MathF.Round(value));

    /// <summary>向下取整到 <see cref="int"/>；NaN 与越界值得 <see cref="int.MinValue"/>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FloorToInt(float value) => ToInt32Mono(MathF.Floor(value));

    /// <summary>向上取整到 <see cref="int"/>；NaN 与越界值得 <see cref="int.MinValue"/>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CeilToInt(float value) => ToInt32Mono(MathF.Ceiling(value));

    // ------------------------------------------------------------------ 最值与钳制

    /// <summary>
    /// <c>a &gt; b ? a : b</c>。与 <see cref="MathF.Max(float, float)"/>（以及 UnityStubs）不同，NaN 不对称：
    /// <c>Max(NaN, 1) == 1</c>，<c>Max(1, NaN)</c> 为 NaN；<c>Max(-0, +0)</c> 为 +0，<c>Max(+0, -0)</c> 为 -0。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Max(float a, float b) => a > b ? a : b;

    /// <summary><c>a &lt; b ? a : b</c>；NaN 与有符号零的行为见 <see cref="Max(float, float)"/>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Min(float a, float b) => a < b ? a : b;

    /// <summary>两个整数中较大者。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Max(int a, int b) => a > b ? a : b;

    /// <summary>两个整数中较小者。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Min(int a, int b) => a < b ? a : b;

    /// <summary>
    /// 最大值：与 Unity 相同，从左到右用 <c>&gt;</c> 比较，空序列得 0。首位的 NaN 会被返回，之后的 NaN 被跳过。
    /// 参数是 span，<c>Max(a, b, c)</c> 不分配内存。
    /// </summary>
    public static float Max(params ReadOnlySpan<float> values)
    {
        if (values.IsEmpty)
        {
            return 0f;
        }

        float result = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > result)
            {
                result = values[i];
            }
        }

        return result;
    }

    /// <summary>最小值：从左到右用 <c>&lt;</c> 比较，空序列得 0。</summary>
    public static float Min(params ReadOnlySpan<float> values)
    {
        if (values.IsEmpty)
        {
            return 0f;
        }

        float result = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] < result)
            {
                result = values[i];
            }
        }

        return result;
    }

    /// <summary>
    /// 整数最大值，Unity <c>Mathf.Max(params int[])</c>：从左到右比较，空序列得 0。
    /// 没有这个重载时，三个 int 实参会绑定到 float 版本，结果经过 float 往返，超过 2²⁴ 的值会丢精度。
    /// </summary>
    public static int Max(params ReadOnlySpan<int> values)
    {
        if (values.IsEmpty)
        {
            return 0;
        }

        int result = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > result)
            {
                result = values[i];
            }
        }

        return result;
    }

    /// <summary>整数最小值，Unity <c>Mathf.Min(params int[])</c>：从左到右比较，空序列得 0。</summary>
    public static int Min(params ReadOnlySpan<int> values)
    {
        if (values.IsEmpty)
        {
            return 0;
        }

        int result = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] < result)
            {
                result = values[i];
            }
        }

        return result;
    }

    /// <summary>
    /// 把 <paramref name="value"/> 钳制到 [<paramref name="min"/>, <paramref name="max"/>]，从不抛异常：先检查下界，
    /// 所以 <c>min &gt; max</c> 时小于 <paramref name="min"/> 的值得 <paramref name="min"/>，大于 <paramref name="max"/> 的值得
    /// <paramref name="max"/>。NaN 原样返回。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp(float value, float min, float max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    /// <summary>整数钳制，顺序规则同 <see cref="Clamp(float, float, float)"/>；从不抛异常。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    /// <summary>钳制到 [0, 1]。NaN 原样返回，-0 保留。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp01(float value)
    {
        if (value < 0f)
        {
            return 0f;
        }

        return value > 1f ? 1f : value;
    }

    // ------------------------------------------------------------------ 插值

    /// <summary><c>a + (b - a) * Clamp01(t)</c>。不同于 <see cref="float.Lerp"/>：后者不钳制，并计算 <c>a*(1-t) + b*t</c>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

    /// <summary><c>a + (b - a) * t</c>，不钳制。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    /// <see cref="Lerp"/> 的钳制反函数：<c>Clamp01((value - a) / (b - a))</c>，<c>a == b</c> 时得 0。
    /// （UnityStubs 判断 <c>|b - a| &lt; Epsilon</c>，只在两端为同一个无穷时不同。）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float InverseLerp(float a, float b, float value)
        => a != b ? Clamp01((value - a) / (b - a)) : 0f;

    /// <summary>
    /// 在 <paramref name="from"/> 与 <paramref name="to"/> 之间做 Hermite 插值，<c>t</c> 先钳制到 [0, 1]，按 Unity 的运算顺序
    /// <c>-2t³ + 3t²</c> 计算（UnityStubs 写成 <c>t²(3 - 2t)</c>，最低位不同）。
    /// 这是插值，不是 HLSL/Waylith 的 <c>smoothstep(edge0, edge1, x)</c>。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmoothStep(float from, float to, float t)
    {
        t = Clamp01(t);
        t = -2f * t * t * t + 3f * t * t;
        return to * t + from * (1f - t);
    }

    /// <summary>
    /// 与 Unity 相同，把 <paramref name="t"/> 回绕到 [0, <paramref name="length"/>] 并钳制。UnityStubs 省略了钳制：
    /// 可能得到略小于 0 的值（<c>Repeat(-1e-45f, 360f)</c> 为 -1e-45 而不是 0），负长度时完全不同。
    /// 常规角度两者一致（[-1080, 1080] 内 20 万组）。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Repeat(float t, float length)
        => Clamp(t - MathF.Floor(t / length) * length, 0f, length);

    /// <summary>让 <paramref name="t"/> 在 0 与 <paramref name="length"/> 之间往返。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float PingPong(float t, float length)
    {
        t = Repeat(t, length * 2f);
        return length - Abs(t - length);
    }

    /// <summary>从 <paramref name="current"/> 到 <paramref name="target"/> 的最短有符号角度差（度），范围 (-180, 180]。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DeltaAngle(float current, float target)
    {
        float delta = Repeat(target - current, 360f);
        if (delta > 180f)
        {
            delta -= 360f;
        }

        return delta;
    }

    /// <summary>沿最短弧插值角度（度）；<paramref name="t"/> 钳制到 [0, 1]。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float LerpAngle(float a, float b, float t)
    {
        float delta = Repeat(b - a, 360f);
        if (delta > 180f)
        {
            delta -= 360f;
        }

        return a + delta * Clamp01(t);
    }

    /// <summary>把 <paramref name="current"/> 向 <paramref name="target"/> 移动至多 <paramref name="maxDelta"/>；负值表示远离。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float MoveTowards(float current, float target, float maxDelta)
    {
        if (Abs(target - current) <= maxDelta)
        {
            return target;
        }

        return current + Sign(target - current) * maxDelta;
    }

    /// <summary>角度版 <see cref="MoveTowards"/>（度），走最短弧。</summary>
    public static float MoveTowardsAngle(float current, float target, float maxDelta)
    {
        float delta = DeltaAngle(current, target);
        if (-maxDelta < delta && delta < maxDelta)
        {
            return target;
        }

        return MoveTowards(current, current + delta, maxDelta);
    }

    /// <summary>
    /// Unity 的容差比较：<c>|b - a| &lt; max(1e-6 · max(|a|, |b|), 8 · Epsilon)</c>。
    /// 与 Unity 一样用严格的 <c>&lt;</c>（UnityStubs 与 Waylith 用 <c>&lt;=</c>，见 <see cref="ApproximatelyInclusive"/>）。任一参数为 NaN 得 <c>false</c>。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Approximately(float a, float b)
        => Abs(b - a) < Max(1E-06f * Max(Abs(a), Abs(b)), Epsilon * 8f);

    /// <summary>
    /// UnityStubs 版的 <c>Mathf.Approximately</c>（<c>&lt;=</c>），也就是 1.7 原版实际执行的比较。只用于镜像 1.7 原版判断、
    /// 需要与原版结果完全一致的代码（ADR-006 的例外）；其余代码用 <see cref="Approximately"/>。两者只在差值正好落在阈值上或为无穷时不同。
    /// </summary>
    /// <remarks>
    /// UnityStubs 内部的 <c>Max</c> 是 <see cref="MathF.Max(float, float)"/>。这里参与比较的都是绝对值（非负、无 -0），
    /// 含 NaN 时左侧差值也是 NaN、比较必为 false，所以换成 Unity 版 <see cref="Max(float, float)"/> 结果逐位相同。
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ApproximatelyInclusive(float a, float b)
        => Abs(b - a) <= Max(1E-06f * Max(Abs(a), Abs(b)), Epsilon * 8f);

    // ------------------------------------------------------------------ 噪声

    /// <summary>
    /// [0, 1] 内的二维梯度噪声，与 RimWorld 1.7 的 <c>UnityStubs.Mathf.PerlinNoise</c> 逐位一致
    /// （Ken Perlin 改进噪声的置换表、四个对角梯度、五次缓动，结果钳制）。
    /// </summary>
    /// <remarks>
    /// Unity 自己的 <c>Mathf.PerlinNoise</c> 是原生代码，无法在 Unity 之外运行，与 1.6 不一致（1.6 的输出范围约为 [-0.112, 1.047]）；
    /// 1.7 原版调用得到的就是本实现的结果。格点坐标与 UnityStubs 一样使用 1.7 运行时的饱和 float → int 转换。
    /// </remarks>
    public static float PerlinNoise(float x, float y) => UnityPerlinNoise.Sample(x, y);
}
