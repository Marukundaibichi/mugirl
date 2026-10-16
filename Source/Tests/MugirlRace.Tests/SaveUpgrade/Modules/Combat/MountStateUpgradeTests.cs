using System.IO;
using System.Runtime.CompilerServices;
using System.Xml;
using Mugirl.Features.Mounting;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade.Modules.Combat;

public sealed class MountStateUpgradeTests
{
    [Fact]
    public void ProductionMountGoldenMatchesAndIsIdempotent()
    {
        string root = Path.Combine(GoldenRoot(), "Combat.MountState");
        var actual = new XmlDocument();
        var expected = new XmlDocument();
        actual.Load(Path.Combine(root, "before.xml"));
        expected.Load(Path.Combine(root, "after.xml"));
        XmlNode payload = actual.SelectSingleNode("//innerContainer/innerList/li")!;
        SaveUpgradeSession session = SaveXml.Upgrade(actual, UpgradeTables.Default);
        Assert.False(session.Report.Failed, session.Report.Error);
        Assert.Equal(SaveXml.Game(expected), SaveXml.Game(actual));
        Assert.Same(payload, actual.SelectSingleNode("//mugirlMountRiders/innerList/li"));
        Assert.Equal("Thing_Silver7", actual.SelectSingleNode("//aimTarget")!.InnerText);
        SaveXml.Upgrade(actual, UpgradeTables.Default);
        Assert.Equal(SaveXml.Game(expected), SaveXml.Game(actual));
    }

    [Fact]
    public void OtherComponentsWithSameOldLabelsAreNotTouched()
    {
        const string body = "<thing Class=\"ThingWithComps\"><def>Foreign</def><id>Foreign1</id><innerContainer><innerList/></innerContainer><fireAtWill>False</fireAtWill><turretAimTicksTotal>93</turretAimTicksTotal></thing>";
        XmlDocument document = SaveXml.Save(body);
        string before = SaveXml.Game(document);
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default, new TestCatalog().With("ThingDef", "Foreign", "Other.CompContainer"));
        Assert.False(session.Report.Failed, session.Report.Error);
        Assert.Equal(before, SaveXml.Game(document));
    }

    [Fact]
    public void ThirdPartyDefWithMountUsesTheSameMigrationAndNoFireDefaultIsInvented()
    {
        XmlDocument document = SaveXml.Save("<thing Class=\"Pawn\"><def>ForeignMount</def><id>ForeignMount1</id><innerContainer><innerList/></innerContainer><turretAimTicksTotal>93</turretAimTicksTotal></thing>");
        var catalog = new TestCatalog().With("ThingDef", "ForeignMount", typeof(CompMount).FullName!);
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default, catalog);
        Assert.False(session.Report.Failed, session.Report.Error);
        Assert.NotNull(document.SelectSingleNode("//mugirlMountRiders"));
        Assert.Equal("93", document.SelectSingleNode("//mugirlMountTurret/aimTicksTotal")!.InnerText);
        Assert.Null(document.SelectSingleNode("//mugirlMountFireAtWill"));
    }

    [Fact]
    public void NewlyAddedMountCompWithoutAnySavedMountLabelsNeedsNoDomEdits()
    {
        XmlDocument document = SaveXml.Save("<thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id></thing>");
        string before = SaveXml.Game(document);
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(session.Report.Failed, session.Report.Error);
        Assert.Equal(before, SaveXml.Game(document));
        Assert.Equal(0, SaveXml.Actions(session, "Combat.MountState"));
    }

    [Fact]
    public void MixedStateFailsWithoutOverwritingTheCanonicalTurret()
    {
        XmlDocument document = SaveXml.Save("<thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><turretAimTicksTotal>10</turretAimTicksTotal><mugirlMountTurret><aimTicksTotal>93</aimTicksTotal></mugirlMountTurret></thing>");
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.True(session.Report.Failed);
        Assert.Contains("Mixed flat and grouped", session.Report.Error, StringComparison.Ordinal);
        Assert.Equal("93", document.SelectSingleNode("//mugirlMountTurret/aimTicksTotal")!.InnerText);
    }

    private static string GoldenRoot([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "Golden"));
}
