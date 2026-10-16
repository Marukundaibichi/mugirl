using Microsoft.Extensions.Logging;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

/// <summary>日志记号的切分与“任何错误都失败”策略。</summary>
public sealed class LogPolicyTests
{
    private static readonly HashSet<string> Tokens = new(StringComparer.Ordinal) { "Mugirl_OldShackles", "MooGirl.CompMooMilkable", "CompMooMilkable" };

    [Theory]
    [InlineData("Could not find def Mugirl_OldShackles", "Mugirl_OldShackles")]
    [InlineData("Thing_Mugirl_OldShackles123 is missing", "Mugirl_OldShackles")]
    [InlineData("Mugirl_OldShackles45 destroyed", "Mugirl_OldShackles")]
    [InlineData("Could not find class MooGirl.CompMooMilkable", "MooGirl.CompMooMilkable")]
    [InlineData("type Other.CompMooMilkable", "CompMooMilkable")]
    public void FindsIdentifierTokens(string text, string expected) => Assert.Equal(expected, LogPolicy.FirstToken(text, Tokens));

    [Theory]
    [InlineData("Mugirl_OldShacklesExtended was loaded")]
    [InlineData("nothing to see")]
    public void DoesNotMatchSubstrings(string text) => Assert.Null(LogPolicy.FirstToken(text, Tokens));

    [Fact]
    public void AnyErrorFailsUnlessAllowlistedOrForeign()
    {
        var policy = new LogPolicy(["^Known vanilla noise"], ["OtherMod_Thing"]);
        policy.AddModTokens(["Mugirl_OldShackles"]);

        Assert.Equal(LogVerdict.Failure, policy.Classify(Log(LogLevel.Error, "something broke"), SuiteLogPolicy.AnyError, out _));
        Assert.Equal(LogVerdict.Allowed, policy.Classify(Log(LogLevel.Error, "Known vanilla noise here"), SuiteLogPolicy.AnyError, out _));
        Assert.Equal(LogVerdict.Foreign, policy.Classify(Log(LogLevel.Error, "Could not resolve OtherMod_Thing"), SuiteLogPolicy.AnyError, out _));
        Assert.Equal(LogVerdict.Failure, policy.Classify(Log(LogLevel.Warning, "Could not find def Mugirl_OldShackles"), SuiteLogPolicy.AnyError, out _));
        Assert.Equal(LogVerdict.Failure, policy.Classify(Log(LogLevel.Warning, "Could not find class Foo"), SuiteLogPolicy.AnyError, out _));
        Assert.Equal(LogVerdict.Ignored, policy.Classify(Log(LogLevel.Warning, "a cold wind blows"), SuiteLogPolicy.AnyError, out _));
    }

    [Fact]
    public void ForeignTokensThatNameTheModAreRejected()
    {
        var policy = new LogPolicy([], ["Mugirl_OldShackles"]);
        policy.AddModTokens(["Mugirl_OldShackles"]);

        Assert.Equal(new[] { "Mugirl_OldShackles" }, policy.RejectedForeignTokens);
    }

    private static CapturedLog Log(LogLevel level, string message) => new(level, "Verse", message, null, "test", DuringStep: true);
}
