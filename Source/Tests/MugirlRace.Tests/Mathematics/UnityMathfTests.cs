using System.Globalization;

namespace Mugirl.Tests.Mathematics;

/// <summary>
/// <see cref="UnityMathf"/> 的语义：Unity 托管公式逐位复现；E41 证明三方（Unity、UnityStubs、本库）一致的成员与 UnityStubs 差分比较。
/// </summary>
public sealed class UnityMathfTests
{
    [Theory]
    [InlineData(5f, 0f, 10f, 5f)]
    [InlineData(-1f, 0f, 10f, 0f)]
    [InlineData(11f, 0f, 10f, 10f)]
    [InlineData(5f, 10f, 0f, 10f)] // min > max：Unity 先比较下界、返回 min，Math.Clamp 会抛异常。
    [InlineData(-5f, 10f, 0f, 10f)]
    [InlineData(20f, 10f, 0f, 0f)]
    public void Clamp_FollowsUnitySemantics(float value, float min, float max, float expected)
        => Bits.Equal(expected, UnityMathf.Clamp(value, min, max));

    [Fact]
    public void Clamp_Int_NeverThrows_LowerBoundFirst()
    {
        Assert.Equal(10, UnityMathf.Clamp(5, 10, 0));
        Assert.Equal(0, UnityMathf.Clamp(20, 10, 0));
        Assert.Equal(7, UnityMathf.Clamp(7, 0, 10));
    }

    [Fact]
    public void Clamp_PassesNaNThrough()
    {
        Assert.True(float.IsNaN(UnityMathf.Clamp(float.NaN, 0f, 1f)));
        Assert.True(float.IsNaN(UnityMathf.Clamp01(float.NaN)));
        Bits.Equal(-0f, UnityMathf.Clamp01(-0f), "Clamp01 keeps -0");
    }

    [Fact]
    public void Lerp_ClampsT()
    {
        Bits.Equal(240f, UnityMathf.Lerp(30f, 240f, 1.5f));
        Bits.Equal(30f, UnityMathf.Lerp(30f, 240f, -1f));
        Bits.Equal(345f, UnityMathf.LerpUnclamped(30f, 240f, 1.5f));
    }

    [Fact]
    public void Max_IsUnityTernary_NotIeeeMax()
    {
        // Unity：a > b ? a : b。NaN 在前时得 b（MathF.Max 会传播 NaN）。
        Bits.Equal(1f, UnityMathf.Max(float.NaN, 1f));
        Assert.True(float.IsNaN(UnityMathf.Max(1f, float.NaN)));
        Bits.Equal(0f, UnityMathf.Max(-0f, 0f), "Max(-0, +0)");
        Bits.Equal(-0f, UnityMathf.Max(0f, -0f), "Max(+0, -0)");
        Bits.Equal(1f, UnityMathf.Min(float.NaN, 1f));
        Assert.True(float.IsNaN(UnityMathf.Min(1f, float.NaN)));
    }

    [Fact]
    public void MaxMin_FloatParams_FollowUnityLoop()
    {
        Bits.Equal(0f, UnityMathf.Max(ReadOnlySpan<float>.Empty), "empty Max");
        Bits.Equal(0f, UnityMathf.Min(ReadOnlySpan<float>.Empty), "empty Min");
        Bits.Equal(3f, UnityMathf.Max(1f, 3f, 2f));
        Bits.Equal(1f, UnityMathf.Min(2f, 1f, 3f));
        Assert.True(float.IsNaN(UnityMathf.Max(float.NaN, 1f, 2f)), "a leading NaN is returned");
        Bits.Equal(2f, UnityMathf.Max(1f, float.NaN, 2f), "later NaN is skipped");
    }

    [Fact]
    public void MaxMin_IntParams_StayExactAboveFloatPrecision()
    {
        // 没有 int 重载时三个 int 会绑定到 float 版本，16777217 经 float 往返变成 16777216。
        Assert.Equal(16777217, UnityMathf.Max(16777217, 1, 2));
        Assert.Equal(-16777217, UnityMathf.Min(-16777217, 1, 2));
        Assert.Equal(5, UnityMathf.Max(1, 5, 3));
        Assert.Equal(-2, UnityMathf.Min(4, -2, 7));
        Assert.Equal(0, UnityMathf.Max(ReadOnlySpan<int>.Empty));
        Assert.Equal(0, UnityMathf.Min(ReadOnlySpan<int>.Empty));
        Assert.Equal(int.MaxValue, UnityMathf.Max(int.MinValue, int.MaxValue));
    }

    [Theory]
    [InlineData(2.5f, 2)]
    [InlineData(3.5f, 4)]
    [InlineData(-2.5f, -2)]
    [InlineData(-0.5f, 0)]
    [InlineData(2147483520f, 2147483520)]
    [InlineData(-2147483648f, int.MinValue)]
    [InlineData(2147483648f, int.MinValue)]
    [InlineData(3E+09f, int.MinValue)]
    [InlineData(float.NaN, int.MinValue)]
    [InlineData(float.PositiveInfinity, int.MinValue)]
    [InlineData(float.NegativeInfinity, int.MinValue)]
    public void RoundToInt_UsesBankersRounding_AndMonoOverflowRule(float value, int expected)
        => Assert.Equal(expected, UnityMathf.RoundToInt(value));

    [Fact]
    public void FloorCeilToInt_UseMonoOverflowRule()
    {
        Assert.Equal(-1, UnityMathf.FloorToInt(-0.5f));
        Assert.Equal(0, UnityMathf.CeilToInt(-0.5f));
        Assert.Equal(int.MinValue, UnityMathf.FloorToInt(float.NaN));
        Assert.Equal(int.MinValue, UnityMathf.CeilToInt(float.PositiveInfinity));
        Assert.Equal(int.MinValue, UnityMathf.ToInt32Mono(1E+10f));
        Assert.Equal(123, UnityMathf.ToInt32Mono(123.9f));
    }

    [Fact]
    public void Sign_TreatsNegativeZeroAsPositive_AndNaNAsNegative()
    {
        Bits.Equal(1f, UnityMathf.Sign(-0f));
        Bits.Equal(-1f, UnityMathf.Sign(float.NaN));
        Bits.Equal(-1f, UnityMathf.Sign(-3f));
    }

    [Fact]
    public void InverseLerp_ClampsAndReturnsZeroForEqualEnds()
    {
        Bits.Equal(0.5f, UnityMathf.InverseLerp(0f, 10f, 5f));
        Bits.Equal(1f, UnityMathf.InverseLerp(0f, 10f, 20f));
        Bits.Equal(0f, UnityMathf.InverseLerp(5f, 5f, 1f));
        Bits.Equal(0f, UnityMathf.InverseLerp(float.PositiveInfinity, float.PositiveInfinity, 1f), "UnityStubs gives NaN here");
    }

    [Fact]
    public void SmoothStep_UsesUnityFormula()
    {
        Bits.Equal(0.5f, UnityMathf.SmoothStep(0f, 1f, 0.5f));
        Bits.Equal(10f, UnityMathf.SmoothStep(10f, 20f, -1f));
        Bits.Equal(20f, UnityMathf.SmoothStep(10f, 20f, 2f));
        Random random = new(11);
        Mismatches mismatches = new("SmoothStep vs Unity 2022.3 formula");
        for (int i = 0; i < Samples.RandomCount; i++)
        {
            float from = Samples.Uniform(random, -50f, 50f);
            float to = Samples.Uniform(random, -50f, 50f);
            float t = Samples.Uniform(random, -0.25f, 1.25f);
            float c = t < 0f ? 0f : t > 1f ? 1f : t;
            c = -2f * c * c * c + 3f * c * c;
            float expected = to * c + from * (1f - c);
            mismatches.Check(expected, UnityMathf.SmoothStep(from, to, t), () => Describe(from, to, t));
        }

        mismatches.AssertNone();
    }

    [Fact]
    public void Repeat_IsClampedToLength()
    {
        Bits.Equal(0f, UnityMathf.Repeat(-1E-45f, 360f), "UnityStubs returns -1e-45 here");
        Bits.Equal(10f, UnityMathf.Repeat(370f, 360f));
        Bits.Equal(350f, UnityMathf.Repeat(-10f, 360f));
        Bits.Equal(0f, UnityMathf.Repeat(5f, -3f), "negative length clamps to 0");
        Bits.Equal(1f, UnityMathf.PingPong(3f, 2f));
    }

    [Fact]
    public void Angles_WrapLikeUnity()
    {
        Bits.Equal(-20f, UnityMathf.DeltaAngle(10f, 350f));
        Bits.Equal(180f, UnityMathf.DeltaAngle(0f, 180f));
        Bits.Equal(-179f, UnityMathf.DeltaAngle(0f, 181f));
        Bits.Equal(360f, UnityMathf.LerpAngle(350f, 10f, 0.5f), "LerpAngle does not wrap its result");
        Bits.Equal(15f, UnityMathf.MoveTowardsAngle(10f, 30f, 5f));
        Bits.Equal(30f, UnityMathf.MoveTowardsAngle(10f, 30f, 25f));
        Bits.Equal(12f, UnityMathf.MoveTowards(10f, 20f, 2f));
        Bits.Equal(20f, UnityMathf.MoveTowards(10f, 20f, 15f));
    }

    [Fact]
    public void Approximately_IsStrict_InclusiveVariantMatchesStubs()
    {
        // 差值正好等于阈值 8·Epsilon：Unity 用 < 得 false，UnityStubs 用 <= 得 true。
        float b = 8 * float.Epsilon;
        Assert.False(UnityMathf.Approximately(0f, b));
        Assert.True(UnityMathf.ApproximatelyInclusive(0f, b));
        Assert.True(UMathf.Approximately(0f, b));
        Assert.True(UnityMathf.Approximately(1f, 1.0000001f));
        Assert.False(UnityMathf.Approximately(float.NaN, float.NaN));
    }

    [Fact]
    public void Transcendentals_RoundOnceFromDouble()
    {
        float[] values = Samples.Floats(3, 1000f);
        Mismatches mismatches = new("transcendental functions vs (float)Math.X");
        foreach (float v in values)
        {
            mismatches.Check((float)Math.Sin(v), UnityMathf.Sin(v), () => "Sin " + Bits.Describe(v));
            mismatches.Check((float)Math.Cos(v), UnityMathf.Cos(v), () => "Cos " + Bits.Describe(v));
            mismatches.Check((float)Math.Atan2(v, 0.75), UnityMathf.Atan2(v, 0.75f), () => "Atan2 " + Bits.Describe(v));
            mismatches.Check((float)Math.Exp(v * 0.01f), UnityMathf.Exp(v * 0.01f), () => "Exp " + Bits.Describe(v));
            mismatches.Check((float)Math.Log(v), UnityMathf.Log(v), () => "Log " + Bits.Describe(v));
        }

        mismatches.AssertNone();
    }

    /// <summary>E41：Unity、UnityStubs 与本库在这些成员上 0 处不同，所以直接与 UnityStubs 逐位比较。</summary>
    [Fact]
    public void SharedMembers_MatchUnityStubsBitForBit()
    {
        float[] values = Samples.Floats(7, 4000f);
        Random random = new(8);
        Mismatches mismatches = new("UnityMathf vs UnityStubs Mathf");
        foreach (float v in values)
        {
            float a = Samples.Next(random, 1000f);
            float b = Samples.Next(random, 1000f);
            string Input() => Describe(v, a, b);
            mismatches.Check(UMathf.Clamp(v, a, b), UnityMathf.Clamp(v, a, b), Input);
            mismatches.Check(UMathf.Clamp01(v), UnityMathf.Clamp01(v), Input);
            mismatches.Check(UMathf.Lerp(a, b, v), UnityMathf.Lerp(a, b, v), Input);
            mismatches.Check(UMathf.LerpUnclamped(a, b, v), UnityMathf.LerpUnclamped(a, b, v), Input);
            mismatches.Check(UMathf.MoveTowards(a, b, v), UnityMathf.MoveTowards(a, b, v), Input);
            mismatches.Check(UMathf.Abs(v), UnityMathf.Abs(v), Input);
            mismatches.Check(UMathf.Sqrt(v), UnityMathf.Sqrt(v), Input);
            mismatches.Check(UMathf.Floor(v), UnityMathf.Floor(v), Input);
            mismatches.Check(UMathf.Ceil(v), UnityMathf.Ceil(v), Input);
            mismatches.Check(UMathf.Round(v), UnityMathf.Round(v), Input);
            mismatches.Check(UMathf.Sign(v), UnityMathf.Sign(v), Input);
            mismatches.Check(UMathf.RoundToInt(v), UnityMathf.RoundToInt(v), Input);
            mismatches.Check(UMathf.FloorToInt(v), UnityMathf.FloorToInt(v), Input);
            mismatches.Check(UMathf.CeilToInt(v), UnityMathf.CeilToInt(v), Input);
            mismatches.Check(UMathf.Approximately(a, v), UnityMathf.ApproximatelyInclusive(a, v), Input);
            int ia = (int)a;
            int ib = (int)b;
            int iv = (int)Math.Clamp(v, -1E+09f, 1E+09f);
            mismatches.Check(UMathf.Clamp(iv, ia, ib), UnityMathf.Clamp(iv, ia, ib), Input);
        }

        mismatches.AssertNone();
    }

    [Fact]
    public void PerlinNoise_MatchesUnityStubsBitForBit()
    {
        Random random = new(21);
        Mismatches mismatches = new("PerlinNoise vs UnityStubs");
        ReadOnlySpan<float> specials = Samples.Specials;
        foreach (float x in specials)
        {
            foreach (float y in specials)
            {
                mismatches.Check(UMathf.PerlinNoise(x, y), UnityMathf.PerlinNoise(x, y), () => Describe(x, y));
            }
        }

        for (int i = 0; i < Samples.RandomCount; i++)
        {
            float x = Samples.Uniform(random, -300f, 300f);
            float y = Samples.Uniform(random, -300f, 300f);
            mismatches.Check(UMathf.PerlinNoise(x, y), UnityMathf.PerlinNoise(x, y), () => Describe(x, y));
        }

        mismatches.AssertNone();
        Bits.Equal(0.5f, UnityMathf.PerlinNoise(0f, 0f), "lattice points sample to 0.5");
    }

    [Fact]
    public void Constants_MatchUnity()
    {
        Bits.Equal(3.14159274f, UnityMathf.PI);
        Bits.Equal(0.0174532924f, UnityMathf.Deg2Rad);
        Bits.Equal(57.29578f, UnityMathf.Rad2Deg);
        Bits.Equal(UMathf.Deg2Rad, UnityMathf.Deg2Rad, "UnityStubs Deg2Rad");
        Bits.Equal(float.Epsilon, UnityMathf.Epsilon);
    }

    private static string Describe(params ReadOnlySpan<float> values)
    {
        string[] parts = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            parts[i] = values[i].ToString("R", CultureInfo.InvariantCulture);
        }

        return "(" + string.Join(", ", parts) + ")";
    }
}
