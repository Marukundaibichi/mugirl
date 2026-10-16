using System.Globalization;

namespace Mugirl.Tests.Mathematics;

/// <summary><see cref="UnityColor"/>：常量取 Unity 2022.3 的精确值；插值与字节互转和 UnityStubs 逐位比较（E41：三方一致）。</summary>
public sealed class UnityColorTests
{
    public static TheoryData<string, float, float, float, float> UnityConstants => new()
    {
        { nameof(UnityColor.White), 1f, 1f, 1f, 1f },
        { nameof(UnityColor.Black), 0f, 0f, 0f, 1f },
        { nameof(UnityColor.Clear), 0f, 0f, 0f, 0f },
        { nameof(UnityColor.Red), 1f, 0f, 0f, 1f },
        { nameof(UnityColor.Green), 0f, 1f, 0f, 1f },
        { nameof(UnityColor.Blue), 0f, 0f, 1f, 1f },
        { nameof(UnityColor.Yellow), 1f, 0.92156863f, 0.015686275f, 1f },
        { nameof(UnityColor.Cyan), 0f, 1f, 1f, 1f },
        { nameof(UnityColor.Magenta), 1f, 0f, 1f, 1f },
        { nameof(UnityColor.Gray), 0.5f, 0.5f, 0.5f, 1f },
        { nameof(UnityColor.Grey), 0.5f, 0.5f, 0.5f, 1f },
    };

    [Theory]
    [MemberData(nameof(UnityConstants))]
    public void Constants_HaveUnityValues(string name, float r, float g, float b, float a)
    {
        Color color = name switch
        {
            nameof(UnityColor.White) => UnityColor.White,
            nameof(UnityColor.Black) => UnityColor.Black,
            nameof(UnityColor.Clear) => UnityColor.Clear,
            nameof(UnityColor.Red) => UnityColor.Red,
            nameof(UnityColor.Green) => UnityColor.Green,
            nameof(UnityColor.Blue) => UnityColor.Blue,
            nameof(UnityColor.Yellow) => UnityColor.Yellow,
            nameof(UnityColor.Cyan) => UnityColor.Cyan,
            nameof(UnityColor.Magenta) => UnityColor.Magenta,
            nameof(UnityColor.Gray) => UnityColor.Gray,
            _ => UnityColor.Grey,
        };
        Bits.Equal(r, color.r, name + ".r");
        Bits.Equal(g, color.g, name + ".g");
        Bits.Equal(b, color.b, name + ".b");
        Bits.Equal(a, color.a, name + ".a");
    }

    [Fact]
    public void Yellow_DiffersFromUnityStubsOnPurpose()
    {
        // UnityStubs 的 Color.yellow 是 (1, 0.922, 0.016, 1)，这正是 UnityColor 自带常量的原因。
        Assert.False(Bits.Same(Color.yellow.g, UnityColor.Yellow.g));
        Bits.Equal(47f / 51f, UnityColor.Yellow.g);
    }

    [Fact]
    public void Arithmetic_IsComponentWise()
    {
        Color a = new(0.5f, 0.25f, 1f, 0.8f);
        Color b = new(0.5f, 2f, 0f, 0.5f);
        Color product = UnityColor.Multiply(a, b);
        Assert.Equal((0.25f, 0.5f, 0f, 0.4f), (product.r, product.g, product.b, product.a));
        Color scaled = UnityColor.Scaled(a, 2f);
        Assert.Equal((1f, 0.5f, 2f, 1.6f), (scaled.r, scaled.g, scaled.b, scaled.a));
        Color faded = UnityColor.WithAlpha(a, 0.1f);
        Assert.Equal((0.5f, 0.25f, 1f, 0.1f), (faded.r, faded.g, faded.b, faded.a));
    }

    [Fact]
    public void Equality_HelpersFollowTheirDocumentedRules()
    {
        Color a = new(0.5f, 0.5f, 0.5f, 1f);
        Color nearlyA = new(0.5f + 5E-06f, 0.5f, 0.5f, 1f);
        Assert.True(UnityColor.ApproximatelyEqual(a, nearlyA));
        Assert.False(UnityColor.ExactlyEqual(a, nearlyA));
        Color nan = new(float.NaN, 0f, 0f, 1f);
        Assert.False(UnityColor.ApproximatelyEqual(nan, nan));
        Assert.True(UnityColor.ExactlyEqual(nan, nan));
        Assert.Equal(nan.Equals(nan), UnityColor.ExactlyEqual(nan, nan));
    }

    [Fact]
    public void LerpAndByteConversions_MatchUnityStubsBitForBit()
    {
        Random random = new(71);
        Mismatches mismatches = new("UnityColor vs UnityStubs Color/Color32");
        float[] channels = Samples.Floats(72, 2f);
        foreach (float channel in channels)
        {
            Color a = new(channel, Samples.Uniform(random, -0.5f, 1.5f), Samples.Uniform(random, 0f, 1f), Samples.Uniform(random, 0f, 1f));
            Color b = new(Samples.Uniform(random, 0f, 1f), channel, Samples.Uniform(random, 0f, 1f), Samples.Uniform(random, 0f, 1f));
            float t = Samples.Uniform(random, -0.5f, 1.5f);
            string Input() => string.Create(CultureInfo.InvariantCulture, $"a=({a.r:R}, {a.g:R}, {a.b:R}, {a.a:R}) t={t:R}");
            CheckColor(mismatches, Color.Lerp(a, b, t), UnityColor.Lerp(a, b, t), Input);
            CheckColor(mismatches, Color.LerpUnclamped(a, b, t), UnityColor.LerpUnclamped(a, b, t), Input);

            Color32 stubsBytes = a;
            Color32 mineBytes = UnityColor.ToBytes(a);
            mismatches.Check(stubsBytes.r, mineBytes.r, Input);
            mismatches.Check(stubsBytes.g, mineBytes.g, Input);
            mismatches.Check(stubsBytes.b, mineBytes.b, Input);
            mismatches.Check(stubsBytes.a, mineBytes.a, Input);

            Color32 bytes = new((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            CheckColor(mismatches, bytes, UnityColor.FromBytes(bytes), Input);
        }

        mismatches.AssertNone();
    }

    private static void CheckColor(Mismatches mismatches, Color expected, Color actual, Func<string> input)
    {
        mismatches.Check(expected.r, actual.r, input);
        mismatches.Check(expected.g, actual.g, input);
        mismatches.Check(expected.b, actual.b, input);
        mismatches.Check(expected.a, actual.a, input);
    }
}
