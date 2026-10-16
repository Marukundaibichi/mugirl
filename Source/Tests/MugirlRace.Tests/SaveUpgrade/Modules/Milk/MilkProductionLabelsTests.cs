using System.Linq;
using System.Xml;
using Mugirl.Features.Milk;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade.Modules.Milk;

public sealed class MilkProductionLabelsTests
{
    [Theory]
    [InlineData(false, "0.25", "0.75")]
    [InlineData(true, "0.25", "0.75")]
    [InlineData(false, "0.25", "0.25")]
    [InlineData(true, "0.25", "0.25")]
    public void PrivateStateIdentifiesMilkIndependentlyOfComponentOrderAndValue(bool foreignFirst, string own, string foreign)
    {
        string ours = "<milkFullness>" + own + "</milkFullness><milkFullness_lastResourceUpdateTick>990</milkFullness_lastResourceUpdateTick><milkFullness_MilkThreshold>1</milkFullness_MilkThreshold>";
        string other = "<milkFullness>" + foreign + "</milkFullness><AllowMilking>False</AllowMilking><CanBeFedBy />";
        XmlDocument document = SaveXml.Save(Pawn(foreignFirst ? other + ours : ours + other));
        XmlElement[] originals = document.SelectNodes("//milkFullness")!.Cast<XmlElement>().ToArray();
        XmlElement untouched = originals[foreignFirst ? 0 : 1];
        string beforeOther = untouched.OuterXml;
        AssertUpgraded(document);
        XmlElement pawn = (XmlElement)document.SelectSingleNode("//thing")!;
        Assert.Equal(own, pawn["mugirlMilkFullness"]!.InnerText);
        Assert.Same(untouched, Assert.Single(pawn.ChildNodes.OfType<XmlElement>(), child => child.Name == "milkFullness"));
        Assert.Equal(beforeOther, untouched.OuterXml);
        Assert.Equal("1", pawn["mugirlMilkThreshold"]!.InnerText);
        Assert.Equal("990", pawn["mugirlMilkAccrualContinuation"]!["anchorTick"]!.InnerText);
        AssertIdempotent(document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OmittedOwnFullnessStaysZeroInsteadOfTakingForeignMilk(bool foreignFirst)
    {
        const string Ours = "<milkFullness_lastResourceUpdateTick>990</milkFullness_lastResourceUpdateTick>";
        const string Other = "<separator /><milkFullness>0.75</milkFullness><CanBeFedBy />";
        XmlDocument document = SaveXml.Save(Pawn(foreignFirst ? Other + Ours : Ours + Other));
        AssertUpgraded(document);
        Assert.Equal("0", document.SelectSingleNode("//mugirlMilkFullness")!.InnerText);
        Assert.Equal("0.75", document.SelectSingleNode("//milkFullness")!.InnerText);
        AssertIdempotent(document);
    }

    [Theory]
    [InlineData("<milkFullness>0.2</milkFullness><milkFullness>0.2</milkFullness>")]
    [InlineData("<milkFullness>0.2</milkFullness><milkFullness>0.8</milkFullness>")]
    [InlineData("<milkFullness>0.8</milkFullness><CanBeFedBy />")]
    [InlineData("<milkFullness>0.8</milkFullness><AllowMilking>False</AllowMilking>")]
    [InlineData("<milkFullness>0.2</milkFullness><milkFullness_lastResourceUpdateTick>90</milkFullness_lastResourceUpdateTick><milkFullness_lastResourceUpdateTick>91</milkFullness_lastResourceUpdateTick>")]
    [InlineData("<milkFullness>0.2</milkFullness><milkFullness_fullNotified>True</milkFullness_fullNotified><foreign /><milkFullness_MilkThreshold>1</milkFullness_MilkThreshold>")]
    [InlineData("<milkFullness>0.2</milkFullness><milkFullness_lastResourceUpdateTick>90</milkFullness_lastResourceUpdateTick><mugirlMilkFullness>0.8</mugirlMilkFullness>")]
    public void AmbiguousProductionDataFailsWithoutChangingEitherValue(string fields)
    {
        XmlDocument document = SaveXml.Save(Pawn(fields));
        string before = document.OuterXml;
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.Equal(MilkProductionLabelsStep.StepId, session.Report.FailedStep);
        Assert.Equal(before, document.OuterXml);
    }

    [Fact]
    public void AUniqueLegacyValueWithoutOptionalStateStillUpgrades()
    {
        XmlDocument document = SaveXml.Save(Pawn("<milkFullness>0.25</milkFullness>"));
        AssertUpgraded(document);
        Assert.Null(document.SelectSingleNode("//milkFullness"));
        Assert.Equal("0.25", document.SelectSingleNode("//mugirlMilkFullness")!.InnerText);
        AssertIdempotent(document);
    }

    [Fact]
    public void ThirdPartyRaceWithOurComponentUsesTheSameBlockRule()
    {
        XmlDocument document = SaveXml.Save(Pawn("<milkFullness>0.25</milkFullness><milkFullness_MilkThreshold>1</milkFullness_MilkThreshold><milkFullness>0.75</milkFullness><CanBeFedBy />", "TestRace"));
        var catalog = new TestCatalog().With("ThingDef", "TestRace", typeof(CompMilkProduction).FullName!);
        Assert.False(SaveXml.Upgrade(document, UpgradeTables.Default, catalog).Report.Failed);
        Assert.Equal("0.25", document.SelectSingleNode("//mugirlMilkFullness")!.InnerText);
        Assert.Equal("0.75", document.SelectSingleNode("//milkFullness")!.InnerText);
    }

    [Fact]
    public void RacesWithoutOurComponentKeepAllDuplicateLabels()
    {
        XmlDocument document = SaveXml.Save(Pawn("<milkFullness>0.25</milkFullness><milkFullness_MilkThreshold>1</milkFullness_MilkThreshold><milkFullness>0.75</milkFullness>", "OtherRace"));
        string before = document.OuterXml;
        AssertUpgraded(document);
        Assert.Equal(before, document.OuterXml);
    }

    [Theory]
    [InlineData("<mugirlMilkFullness>0.25</mugirlMilkFullness>")]
    [InlineData("<mugirlMilkAccrualContinuation IsNull=\"True\" />")]
    public void CurrentProductionStateLeavesForeignLegacyNamesUntouched(string current)
    {
        XmlDocument document = SaveXml.Save(Pawn(current + "<milkFullness>0.75</milkFullness><CanBeFedBy />"));
        AssertIdempotent(document);
    }

    private static string Pawn(string fields, string def = "Mugirl") =>
        "<thing Class=\"Pawn\"><def>" + def + "</def><id>" + def + "1</id>" + fields + "</thing>";

    private static void AssertUpgraded(XmlDocument document)
    {
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(session.Report.Failed, session.Report.Error);
        SaveXml.AssertNoMixedContent(document.DocumentElement!);
    }

    private static void AssertIdempotent(XmlDocument document)
    {
        string before = document.OuterXml;
        SaveUpgradeSession again = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(again.Report.Failed, again.Report.Error);
        Assert.Equal(0, again.Report.TotalActions);
        Assert.Equal(before, document.OuterXml);
    }
}
