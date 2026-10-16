namespace Mugirl.Tests.Support;

/// <summary>差分测试的输入：先给出边界值，再给出固定种子的随机值，保证每次运行完全相同。</summary>
internal static class Samples
{
    /// <summary>每个差分测试的随机样本数。</summary>
    public const int RandomCount = 40_000;

    /// <summary>NaN、±∞、±0、次正规数、1e-5 附近、整数转换边界等容易暴露语义差异的值。</summary>
    public static ReadOnlySpan<float> Specials =>
    [
        0f, -0f, 1f, -1f, 0.5f, -0.5f, 1.5f, 2.5f, -2.5f, 3.5f,
        float.NaN, float.PositiveInfinity, float.NegativeInfinity,
        float.Epsilon, -float.Epsilon, 8 * float.Epsilon, 1.17549435E-38f,
        1E-05f, -1E-05f, 1.0000001E-05f, 9.9999994E-11f, 1E-06f, 1E-15f,
        float.MaxValue, float.MinValue, 2147483648f, -2147483648f, 2147483520f, 3E+09f, 16777217f,
        90f, 180f, 270f, 360f, -360f, 720f, 1080f, -1080f,
    ];

    /// <summary>
    /// 先给出 <see cref="Specials"/>，再给出 <paramref name="count"/> 个随机值：一半在 [-<paramref name="range"/>, <paramref name="range"/>] 内均匀分布，
    /// 一半为随机符号、数量级在 1e-8 到 <paramref name="range"/> 之间对数均匀分布的值。
    /// </summary>
    public static float[] Floats(int seed, float range, int count = RandomCount)
    {
        Random random = new(seed);
        ReadOnlySpan<float> specials = Specials;
        float[] values = new float[specials.Length + count];
        specials.CopyTo(values);
        for (int i = specials.Length; i < values.Length; i++)
        {
            values[i] = Next(random, range);
        }

        return values;
    }

    /// <summary>一个随机值，分布同 <see cref="Floats"/> 的随机部分。</summary>
    public static float Next(Random random, float range)
    {
        if (random.Next(2) == 0)
        {
            return (float)((random.NextDouble() * 2.0 - 1.0) * range);
        }

        double logMin = Math.Log(1E-08);
        double logMax = Math.Log(range);
        double magnitude = Math.Exp(logMin + random.NextDouble() * (logMax - logMin));
        return (float)(random.Next(2) == 0 ? magnitude : -magnitude);
    }

    /// <summary>在 [<paramref name="min"/>, <paramref name="max"/>) 内均匀分布的有限值。</summary>
    public static float Uniform(Random random, float min, float max) => (float)(min + random.NextDouble() * (max - min));
}
