using Mugirl.Features.WeaponWheel;
using Verse;

namespace Mugirl.Tests.WeaponWheel;

/// <summary><see cref="SwordDanceRules"/>：触发保底（第 5 次失败必定触发）与绕到目标身后的落点评分。</summary>
public sealed class SwordDanceRulesTests
{
    [Fact]
    public void ResolveChance_GuaranteesAfterFourFailures()
    {
        int streak = 0;
        for (int i = 0; i < SwordDanceRules.GuaranteeFailures - 1; i++)
        {
            Assert.False(SwordDanceRules.ResolveChance(false, ref streak));
        }
        Assert.Equal(SwordDanceRules.GuaranteeFailures - 1, streak);
        Assert.True(SwordDanceRules.ResolveChance(false, ref streak));
        Assert.Equal(0, streak);

        streak = 3;
        Assert.True(SwordDanceRules.ResolveChance(true, ref streak));
        Assert.Equal(0, streak);
    }

    [Fact]
    public void BehindDirection_IsSignOfOffset()
    {
        Assert.Equal(new IntVec3(1, 0, -1), SwordDanceRules.BehindDirection(new IntVec3(10, 0, 10), new IntVec3(13, 0, 7)));
        Assert.Equal(new IntVec3(0, 0, 1), SwordDanceRules.BehindDirection(new IntVec3(10, 0, 10), new IntVec3(10, 0, 11)));
        Assert.Equal(IntVec3.Zero, SwordDanceRules.BehindDirection(new IntVec3(4, 0, 4), new IntVec3(4, 0, 4)));
    }

    [Fact]
    public void ScoreBehindCell_PrefersExactlyBehindAndRejectsFrontAndSides()
    {
        IntVec3 target = new(20, 0, 20);
        IntVec3 east = new(1, 0, 0);
        int? exact = SwordDanceRules.ScoreBehindCell(target, east, new IntVec3(21, 0, 20));
        int? diagonal = SwordDanceRules.ScoreBehindCell(target, east, new IntVec3(21, 0, 21));
        Assert.Equal(10, exact);
        Assert.Equal(9, diagonal);
        Assert.Null(SwordDanceRules.ScoreBehindCell(target, east, new IntVec3(20, 0, 21)));
        Assert.Null(SwordDanceRules.ScoreBehindCell(target, east, new IntVec3(19, 0, 20)));

        IntVec3 northEast = new(1, 0, 1);
        // 斜向时正后方得分最高，两侧相邻格次之且相等。
        Assert.Equal(20, SwordDanceRules.ScoreBehindCell(target, northEast, new IntVec3(21, 0, 21)));
        Assert.Equal(9, SwordDanceRules.ScoreBehindCell(target, northEast, new IntVec3(21, 0, 20)));
        Assert.Equal(9, SwordDanceRules.ScoreBehindCell(target, northEast, new IntVec3(20, 0, 21)));
    }
}
