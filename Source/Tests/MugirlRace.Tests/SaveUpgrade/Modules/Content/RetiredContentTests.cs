using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade.Modules.Content;

public sealed class RetiredContentTests
{
    [Fact]
    public void TypedRetirementDoesNotTouchOtherDefRootsOrPlayerText()
    {
        UpgradeTables tables = UpgradeTables.Default;
        Assert.Null(tables.FindDef("HeadShapeDef", "lovinblush"));
        Assert.NotNull(tables.FindDef("EmotionShapeDef", "lovinblush"));
        const string body = "<playerText>Mugirl_BulldogTailAssistedCut</playerText><label>Human10_left armored foreclaw_Mugirl_BulldogTailAssistedCutManeuver</label>";
        Assert.Equal(body, SaveXml.Upgraded(body, tables));
    }

    [Fact]
    public void UnsupportedRetiredVerbLocationFailsWithoutDiscardingIt()
    {
        const string body = "<unrecognized Class=\"Mugirl.Verb_MeleeAttack_SegmentedTailAssist\"><loadID>keep-this-identity</loadID></unrecognized>";
        var document = SaveXml.Save(body);
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.True(session.Report.Failed);
        Assert.Contains("unrecognized", SaveXml.Game(document), StringComparison.Ordinal);
        Assert.False(session.Verified);
    }
}
