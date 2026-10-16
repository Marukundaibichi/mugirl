using System.Collections.Generic;
using System.Globalization;

namespace Mugirl.Tests.Support;

/// <summary>逐位比较浮点结果。任何两个 NaN 视为相同（不同实现产生的 NaN 载荷可能不同）；+0 与 -0 视为不同。</summary>
internal static class Bits
{
    /// <summary>两个值逐位相同，或都是 NaN。</summary>
    public static bool Same(float expected, float actual)
        => (float.IsNaN(expected) && float.IsNaN(actual)) || BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual);

    /// <summary>逐位相同，否则以 <paramref name="context"/> 报告失败。</summary>
    public static void Equal(float expected, float actual, string context = "")
    {
        if (!Same(expected, actual))
        {
            Assert.Fail($"{context}: expected {Describe(expected)}, actual {Describe(actual)}");
        }
    }

    /// <summary>带十六进制位模式的描述，区分 +0 与 -0。</summary>
    public static string Describe(float value)
        => string.Create(CultureInfo.InvariantCulture, $"{value:R} (0x{BitConverter.SingleToInt32Bits(value):X8})");
}

/// <summary>
/// 收集差分测试中的不一致，最后统一断言：报告总数与前几个样本，便于一次看清差异的分布。
/// </summary>
internal sealed class Mismatches(string subject)
{
    private const int MaxReported = 8;
    private readonly List<string> samples = [];
    private int count;
    private int checkedCount;

    /// <summary>比较一个分量；不一致时用 <paramref name="describeInput"/> 生成输入描述（只在失败时调用）。</summary>
    public void Check(float expected, float actual, Func<string> describeInput)
    {
        checkedCount++;
        if (Bits.Same(expected, actual))
        {
            return;
        }

        count++;
        if (samples.Count < MaxReported)
        {
            samples.Add($"{describeInput()}: expected {Bits.Describe(expected)}, actual {Bits.Describe(actual)}");
        }
    }

    /// <summary>比较一个布尔结果。</summary>
    public void Check(bool expected, bool actual, Func<string> describeInput)
    {
        checkedCount++;
        if (expected == actual)
        {
            return;
        }

        count++;
        if (samples.Count < MaxReported)
        {
            samples.Add($"{describeInput()}: expected {expected}, actual {actual}");
        }
    }

    /// <summary>比较一个整数结果。</summary>
    public void Check(int expected, int actual, Func<string> describeInput)
    {
        checkedCount++;
        if (expected == actual)
        {
            return;
        }

        count++;
        if (samples.Count < MaxReported)
        {
            samples.Add(string.Create(CultureInfo.InvariantCulture, $"{describeInput()}: expected {expected}, actual {actual}"));
        }
    }

    /// <summary>没有任何不一致，且至少比较过一次。</summary>
    public void AssertNone()
    {
        Assert.True(checkedCount > 0, $"{subject}: nothing was compared");
        if (count > 0)
        {
            Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"{subject}: {count} of {checkedCount} comparisons differ.\n  {string.Join("\n  ", samples)}"));
        }
    }
}
