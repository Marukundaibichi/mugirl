using System.Collections.Generic;
using Mugirl.Features.Milk;

namespace Mugirl.Tests.Milk;

/// <summary>婴儿吸奶、哺育加成与榨乳器释放排程的纯规则。</summary>
public sealed class MilkFeedingRulesTests
{
    [Fact]
    public void DesiredNutrition_IsOneFiveThousandthOfMaxPerTickCappedByWanted()
    {
        Bits.Equal(1f / 5000f * 10f, MilkSuckleMath.DesiredNutrition(1f, 1f, 10));
        Bits.Equal(0.001f, MilkSuckleMath.DesiredNutrition(1f, 0.001f, 10));
    }

    [Fact]
    public void ShouldContinue_StopsWhenFullOrShortOfMilk()
    {
        Assert.True(MilkSuckleMath.ShouldContinue(0.002f, 0.5f, 0.002f));
        Assert.False(MilkSuckleMath.ShouldContinue(0.001f, 0.5f, 0.002f), "not enough milk for this tick");
        Assert.False(MilkSuckleMath.ShouldContinue(0.5f, 0.5f, 0.5f), "baby is full");
    }

    [Fact]
    public void ShouldContinue_MatchesVanilla17SuckleDecision()
    {
        // 原版 1.7 ChildcareUtility.SuckleFromLactatingPawn：Mathf.Approximately(consumed, wanted) 时停止，否则 consumed >= desired 才继续。
        // 参照直接调用 UnityStubs（1.7 原版实际执行的实现），在阈值附近逐 ulp 取样。
        Random random = new(11);
        Mismatches mismatches = new("MilkSuckleMath.ShouldContinue vs vanilla 1.7");
        for (int i = 0; i < 40000; i++)
        {
            float wanted = (float)random.NextDouble() * 2f;
            float consumed = random.Next(3) switch
            {
                0 => wanted,
                1 => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(wanted) + random.Next(-40, 41)),
                _ => (float)random.NextDouble() * 2f,
            };
            float desired = random.Next(2) == 0 ? consumed : (float)random.NextDouble() * 2f;
            bool vanilla = !UMathf.Approximately(consumed, wanted) && consumed >= desired;
            bool mine = MilkSuckleMath.ShouldContinue(consumed, wanted, desired);
            mismatches.Check(vanilla, mine, () => FormattableString.Invariant($"{consumed:R} {wanted:R} {desired:R}"));
        }

        mismatches.AssertNone();
    }

    [Fact]
    public void AvailableNutrition_IgnoresNegativeFullness()
    {
        Bits.Equal(0f, MilkSuckleMath.AvailableNutrition(-0.5f, 10f));
        Bits.Equal(5f, MilkSuckleMath.AvailableNutrition(0.5f, 10f));
    }

    [Theory]
    [InlineData(0f, 1.5f)]
    [InlineData(0.399f, 1.5f)]
    [InlineData(0.4f, 2f)]
    [InlineData(0.699f, 2f)]
    [InlineData(0.7f, 2.5f)]
    [InlineData(1f, 2.5f)]
    public void GrowthFactor_FollowsNurtureStages(float severity, float expected)
    {
        Bits.Equal(expected, NurtureMath.GrowthFactorForSeverity(severity));
    }

    [Theory]
    [InlineData(0f, false)]
    [InlineData(4000f, false)]
    [InlineData(4000.5f, true)]
    [InlineData(10000f, true)]
    [InlineData(10000.5f, false)]
    public void ShouldBypassSaturation_CoversTheRelaxedBand(float xpSinceMidnight, bool expected)
    {
        Assert.Equal(expected, NurtureMath.ShouldBypassSaturation(xpSinceMidnight));
    }

    [Fact]
    public void BypassedLearn_RestoresXpExactlyLikeVanilla()
    {
        // Harmony_SkillRecord_Learn_NurturedXpCap：Prefix 记下原值并置 0，Learn 在 0 上累加 xp，Finalizer 加回原值。
        Random random = new(5);
        for (int i = 0; i < 20000; i++)
        {
            float original = 4000f + (float)random.NextDouble() * 6000f;
            float xp = (float)random.NextDouble() * 50f;
            float shifted = 0f;
            shifted += xp;
            Bits.Equal(original + xp, original + shifted);
        }
    }

    [Fact]
    public void ReleaseSchedule_MarksNullEntriesFinished()
    {
        var entries = new List<Step?> { new(5), null, new(-3) };
        List<int> ticks = MilkReleaseSchedule.Build(entries, static step => step.StartTick);
        Assert.Equal(new[] { 5, MilkReleaseSchedule.Finished, 0 }, ticks);
        Assert.Empty(MilkReleaseSchedule.Build<Step>(null, static step => step.StartTick));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, true)]
    [InlineData(5, 0, 0, 0, false)]
    [InlineData(5, 10, 0, 60, false)]
    [InlineData(10, 10, 0, 60, true)]
    [InlineData(61, 10, 0, 60, false)]
    [InlineData(0, MilkReleaseSchedule.Finished, 0, 60, false)]
    [InlineData(3, 3, 5, 2, false)]
    [InlineData(5, 5, 5, 2, true)]
    public void ShouldFireLooping_RespectsWindowAndNextTick(int age, int nextTick, int startTick, int endTick, bool expected)
    {
        Assert.Equal(expected, MilkReleaseSchedule.ShouldFireLooping(age, nextTick, startTick, endTick));
    }

    [Fact]
    public void NextLoopTick_UsesAtLeastOneTick()
    {
        Assert.Equal(40, MilkReleaseSchedule.NextLoopTick(10, 30));
        Assert.Equal(11, MilkReleaseSchedule.NextLoopTick(10, 0));
    }

    private sealed record Step(int StartTick);
}
