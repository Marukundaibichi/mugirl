using System.Linq;
using System.Xml;
using Mugirl.Features.Incidents;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade.Modules.Incidents;

public sealed class IncidentStateUpgradeTests
{
    [Theory]
    [InlineData("Mugirl.MapComponent_MugirlPunisher", "", true)]
    [InlineData("Mugirl.Features.Incidents.MapComponent_Punisher", "<punishers />", true)]
    [InlineData("Mugirl.Features.Incidents.MapComponent_Punisher", "", false)]
    [InlineData("Other.MapComponent_MugirlPunisher", "<punishers />", false)]
    public void MissingLootDefaultsRequireAnIdentifiedLegacyMapOwner(string type, string oldData, bool expected)
    {
        XmlDocument document = SaveXml.Save($"<maps><li><components><li Class=\"{type}\">{oldData}</li></components></li></maps>");
        SaveUpgradeSession result = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(result.Report.Failed, result.Report.Error);
        XmlElement owner = (XmlElement)document.SelectSingleNode("//maps/li/components/li")!;
        Assert.Equal(expected, owner["heldLoot"] is not null);
        Assert.Equal(expected, owner["lootBursts"] is not null);
        if (expected)
        {
            Assert.Equal("<heldLoot><innerList /></heldLoot>", owner["heldLoot"]!.OuterXml);
            Assert.Equal("<lootBursts />", owner["lootBursts"]!.OuterXml);
        }
        string once = document.OuterXml;
        SaveUpgradeSession again = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(again.Report.Failed, again.Report.Error);
        Assert.Equal(0, again.Report.TotalActions);
        Assert.Equal(once, document.OuterXml);
    }

    [Theory]
    [InlineData("<punisherHeldLoot IsNull=\"true\" /><punisherLootBursts IsNull=\"True\" />")]
    [InlineData("<punisherHeldLoot><innerList><li Class=\"Thing\"><def>Gold</def><id>Gold42</id></li></innerList></punisherHeldLoot><punisherLootBursts><li><startTick>77</startTick></li></punisherLootBursts>")]
    [InlineData("<heldLoot><innerList><li Class=\"Thing\"><def>Gold</def><id>Gold42</id></li></innerList></heldLoot><lootBursts IsNull=\"True\" />")]
    public void ExistingNullAndNonemptyLootAreNeverReplacedByDefaults(string loot)
    {
        XmlDocument document = SaveXml.Save("<maps><li><components><li Class=\"Mugirl.MapComponent_MugirlPunisher\"><punishers />" + loot + "</li></components></li></maps>");
        XmlElement owner = (XmlElement)document.SelectSingleNode("//maps/li/components/li")!;
        XmlElement held = (owner["punisherHeldLoot"] ?? owner["heldLoot"])!;
        XmlElement bursts = (owner["punisherLootBursts"] ?? owner["lootBursts"])!;
        string heldBody = held.InnerXml, burstBody = bursts.InnerXml;
        string heldNull = held.GetAttribute("IsNull"), burstsNull = bursts.GetAttribute("IsNull");
        XmlNode? payload = held.SelectSingleNode("innerList/li");
        SaveUpgradeSession result = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(result.Report.Failed, result.Report.Error);
        Assert.Equal(heldBody, owner["heldLoot"]!.InnerXml);
        Assert.Equal(burstBody, owner["lootBursts"]!.InnerXml);
        Assert.Equal(heldNull, owner["heldLoot"]!.GetAttribute("IsNull"));
        Assert.Equal(burstsNull, owner["lootBursts"]!.GetAttribute("IsNull"));
        Assert.Same(payload, owner.SelectSingleNode("heldLoot/innerList/li"));
    }

    [Fact]
    public void SameNamedDataOutsideMapComponentsGetsNoLootDefaults()
    {
        XmlDocument document = SaveXml.Save("<foreign><li Class=\"Mugirl.MapComponent_MugirlPunisher\"><punishers /></li></foreign>");
        Assert.False(SaveXml.Upgrade(document, UpgradeTables.Default).Report.Failed);
        Assert.Null(document.SelectSingleNode("//heldLoot"));
        Assert.Null(document.SelectSingleNode("//lootBursts"));
    }

    [Fact]
    public void CurrentSchemaDoesNotRunTheOneTimeLootMaterialization()
    {
        XmlDocument document = SaveXml.Save("<maps><li><components><li Class=\"Mugirl.MapComponent_MugirlPunisher\"><punishers /></li></components></li></maps>", stamp: SaveSchema.Current);
        string before = document.OuterXml;
        Assert.False(SaveXml.Upgrade(document, UpgradeTables.Default).Report.Failed);
        Assert.Equal(before, document.OuterXml);
    }

    [Fact]
    public void MixedLootContainersFailWithoutOverwritingEitherAssetTree()
    {
        XmlDocument document = SaveXml.Save("<maps><li><components><li Class=\"Mugirl.Features.Incidents.MapComponent_Punisher\"><punishers /><punisherHeldLoot IsNull=\"True\" /><heldLoot><innerList /></heldLoot></li></components></li></maps>");
        string before = document.OuterXml;
        SaveUpgradeSession result = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.True(result.Report.Failed);
        Assert.Equal(PunisherRecordsUpgrade.StepId, result.Report.FailedStep);
        Assert.Equal(before, document.OuterXml);
    }

    [Theory]
    [InlineData("Incidents.StorylineState")]
    [InlineData("Incidents.MigrationRecords")]
    [InlineData("Incidents.PunisherRecords")]
    public void EveryStructureStepIsRegistered(string id) => Assert.Contains(UpgradeTables.Default.DomSteps, step => step.Id == id);

    [Fact]
    public void StorylinePreservesIndependentFlagsCountdownsAndStaleInvestorUntilItsOriginalCheck()
    {
        XmlDocument document = SaveXml.Save("""
            <components><li Class="Mugirl.MugirlStoryState"><openingCrashCheckTimer>-7</openingCrashCheckTimer>
            <courierRaidTriggered>False</courierRaidTriggered><courierRaidQuestStarted>True</courierRaidQuestStarted><courierRaidTimer>299640</courierRaidTimer>
            <fusionInvestmentAccepted>False</fusionInvestmentAccepted><fusionInvestmentPending>True</fusionInvestmentPending><fusionInvestmentTimer>321</fusionInvestmentTimer>
            <fusionInvestmentInvestorActive>True</fusionInvestmentInvestorActive><fusionInvestmentInvestor>null</fusionInvestmentInvestor><fusionInvestmentNextOfferTick>125</fusionInvestmentNextOfferTick></li></components>
            <questManager><quests><li><root>Mugirl_CourierContact</root></li></quests></questManager>
            """);
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(session.Report.Failed, session.Report.Error);
        XmlElement owner = (XmlElement)document.SelectSingleNode("//components/li")!;
        Assert.Equal("-7", owner["opening"]!["checkRemaining"]!.InnerText);
        Assert.Equal("False", owner["courier"]!["triggered"]!.InnerText);
        Assert.Equal("True", owner["courier"]!["questStarted"]!.InnerText);
        Assert.Equal("299640", owner["courier"]!["delayRemaining"]!.InnerText);
        Assert.Equal("False", owner["investment"]!["accepted"]!.InnerText);
        Assert.Equal("True", owner["investment"]!["pending"]!.InnerText);
        Assert.Equal("321", owner["investment"]!["resultRemaining"]!.InnerText);
        Assert.Equal("True", owner["investment"]!["investorActive"]!.InnerText);
        Assert.Equal("null", owner["investment"]!["investor"]!.InnerText);
        Assert.Equal("125", owner["investment"]!["nextOfferTick"]!.InnerText);
    }

    [Fact]
    public void NullMigrationMembersAndUnmatchedKillIdsRemainUntilDomainProcessing()
    {
        XmlDocument document = SaveXml.Save("""
            <maps><li><components><li Class="Mugirl.MapComponent_MugirlMigration"><migrationPawns><li>null</li><li>Thing_Mugirl123</li></migrationPawns>
            <nextGrazeTicks><li>371</li></nextGrazeTicks><playerKilledMigrationPawnIds><li>123</li><li>88</li></playerKilledMigrationPawnIds>
            <ticksUntilDeparture>7</ticksUntilDeparture><killWindowTicksRemaining>9</killWindowTicksRemaining><migrationOutcomeFailed>False</migrationOutcomeFailed><dataVersion>1</dataVersion>
            </li></components></li></maps>
            """);
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(session.Report.Failed, session.Report.Error);
        Assert.Equal(2, document.SelectNodes("//migration/members/li")!.Count);
        Assert.Equal("null", document.SelectSingleNode("//migration/members/li[1]/pawn")!.InnerText);
        Assert.Equal("371", document.SelectSingleNode("//migration/members/li[1]/grazeAtTick")!.InnerText);
        Assert.Null(document.SelectSingleNode("//migration/members/li[2]/grazeAtTick"));
        Assert.Equal("12388", document.SelectSingleNode("//migration/playerKillIds")!.InnerText);
        Assert.Equal("False", document.SelectSingleNode("//migration/outcomeFailed")!.InnerText);
        Assert.Equal("7", document.SelectSingleNode("//migration/departureRemaining")!.InnerText);
        Assert.Equal("9", document.SelectSingleNode("//migration/killWindowRemaining")!.InnerText);
        Assert.Equal("1", document.SelectSingleNode("//initialization/firstUnscheduledIndex")!.InnerText);
        Assert.Equal("True", document.SelectSingleNode("//initialization/releaseBikiniLocks")!.InnerText);
        string once = SaveXml.Game(document);
        SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.Equal(once, SaveXml.Game(document));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void BikiniCompletionUsesTheExactPriorVersionPredicate(int? version, bool expected)
    {
        string versionXml = version is null ? "" : $"<dataVersion>{version}</dataVersion>";
        XmlDocument document = SaveXml.Save($"<maps><li><components><li Class=\"Mugirl.MapComponent_MugirlMigration\"><migrationPawns />{versionXml}</li></components></li></maps>");
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(session.Report.Failed, session.Report.Error);
        Assert.Equal(expected, document.SelectSingleNode("//initialization/releaseBikiniLocks")?.InnerText == "True");
        Assert.Null(document.SelectSingleNode("//migration/outcomeFailed"));
    }

    [Fact]
    public void PunisherLeavingFlagAndNullSlotAreNotReconstructedFromTime()
    {
        XmlDocument document = SaveXml.Save("""
            <maps><li><components><li Class="Mugirl.MapComponent_MugirlPunisher"><punishers><li>Thing_Mugirl_PunisherRace1</li><li>null</li></punishers>
            <punisherSpawnedAtTicks><li>999999</li></punisherSpawnedAtTicks><punisherLeavingFlags><li>True</li><li>False</li><li>True</li></punisherLeavingFlags>
            <punisherHeldLoot><innerList><li Class="Thing"><def>Gold</def><id>Gold23</id><hitPoints>37</hitPoints></li></innerList></punisherHeldLoot>
            <punisherLootBursts><li><arcs><li><thing>Thing_Gold23</thing><spin>-13.75</spin></li></arcs></li></punisherLootBursts></li></components></li></maps>
            """);
        XmlNode loot = document.SelectSingleNode("//punisherHeldLoot/innerList/li")!;
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(session.Report.Failed, session.Report.Error);
        Assert.Equal(2, document.SelectNodes("//activePunishers/li")!.Count);
        Assert.Equal("True", document.SelectSingleNode("//activePunishers/li[1]/leaving")!.InnerText);
        Assert.Equal("999999", document.SelectSingleNode("//activePunishers/li[1]/spawnTick")!.InnerText);
        Assert.Equal("null", document.SelectSingleNode("//activePunishers/li[2]/pawn")!.InnerText);
        Assert.Same(loot, document.SelectSingleNode("//heldLoot/innerList/li"));
        Assert.Equal("Thing_Gold23", document.SelectSingleNode("//lootBursts/li/arcs/li/thing")!.InnerText);
        Assert.Equal("-13.75", document.SelectSingleNode("//lootBursts/li/arcs/li/spin")!.InnerText);
    }

    [Fact]
    public void JobsLettersQuestPartsAndRunawayTimerArePreserved()
    {
        const string before = """
            <letters><li Class="Mugirl.ChoiceLetter_MugirlFusionInvestment"><map>Map_0</map><text>原始投资信</text></li></letters>
            <parts><li Class="Mugirl.QuestPart_RunawayFarmCompletion"><ticksUntilNextCheck>137</ticksUntilNextCheck><spawnPart>Quest_1_Part_7</spawnPart></li></parts>
            <jobs><curDriver Class="Mugirl.JobDriver_TalkCourier"><curToilIndex>1</curToilIndex><ticksLeftThisToil>37</ticksLeftThisToil></curDriver></jobs>
            """;
        XmlDocument document = SaveXml.Save(before);
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(session.Report.Failed, session.Report.Error);
        Assert.Equal("Mugirl.Features.Incidents.ChoiceLetter_FusionInvestment", ((XmlElement)document.SelectSingleNode("//letters/li")!).GetAttribute("Class"));
        Assert.Equal("原始投资信", document.SelectSingleNode("//letters/li/text")!.InnerText);
        Assert.Equal("137", document.SelectSingleNode("//ticksUntilNextCheck")!.InnerText);
        Assert.Equal("Quest_1_Part_7", document.SelectSingleNode("//spawnPart")!.InnerText);
        Assert.Equal("1", document.SelectSingleNode("//curToilIndex")!.InnerText);
        Assert.Equal("37", document.SelectSingleNode("//ticksLeftThisToil")!.InnerText);
        foreach (string name in new[] { "Mugirl.JobDriver_Nuzzle", "Mugirl.JobDriver_ReadCourierDiary", "Mugirl.JobDriver_TalkCourier", "Mugirl.JobDriver_TalkFusionInvestor" })
        {
            Assert.Equal(JobDriverPolicy.Preserve, UpgradeTables.Default.JobPolicies.Single(row => row.Type == name).Policy);
        }
    }
}
