using System.Collections.Generic;
using Mugirl.Features.Incidents;

namespace Mugirl.Tests.Incidents;

/// <summary><see cref="EventPawnTags"/>：questTags 中的事件标记（存档数据，字符串不能改）。</summary>
public sealed class EventPawnTagsTests
{
    [Fact]
    public void TagStrings_AreTheSavedValues()
    {
        Assert.Equal("Mugirl_Migration", EventPawnTags.MigrationTag);
        Assert.Equal("Mugirl_RunawayFarm", EventPawnTags.RunawayFarmTag);
        Assert.Equal("Mugirl_FusionInvestor", EventPawnTags.FusionInvestorTag);
    }

    [Fact]
    public void Parse_NullOrUnrelatedTags_IsNone()
    {
        Assert.Equal(EventPawnFlags.None, EventPawnTags.Parse(null));
        Assert.Equal(EventPawnFlags.None, EventPawnTags.Parse(new List<string>()));
        Assert.Equal(EventPawnFlags.None, EventPawnTags.Parse(new List<string> { "Quest12.RunawayFarm_Protected", "Mugirl_RunawayReward" }));
    }

    [Fact]
    public void Parse_CombinesAllMarkersInOnePass()
    {
        var tags = new List<string> { "Quest3.Something", "Mugirl_FusionInvestor", "Mugirl_Migration", "Mugirl_RunawayFarm" };
        Assert.Equal(
            EventPawnFlags.Migration | EventPawnFlags.RunawayFarm | EventPawnFlags.FusionInvestor,
            EventPawnTags.Parse(tags));
    }

    [Fact]
    public void Parse_IsOrdinalLikeListContains()
    {
        Assert.Equal(EventPawnFlags.None, EventPawnTags.Parse(new List<string> { "mugirl_migration", "MUGIRL_RUNAWAYFARM" }));
    }

    [Fact]
    public void Passive_CoversMigrationAndRunawayOnly()
    {
        Assert.NotEqual(EventPawnFlags.None, EventPawnFlags.Passive & EventPawnFlags.Migration);
        Assert.NotEqual(EventPawnFlags.None, EventPawnFlags.Passive & EventPawnFlags.RunawayFarm);
        Assert.Equal(EventPawnFlags.None, EventPawnFlags.Passive & EventPawnFlags.FusionInvestor);
    }
}
