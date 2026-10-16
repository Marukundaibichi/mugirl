using System;
using System.Globalization;
using System.Linq;
using System.Xml;
using Mugirl.Features.Corporate;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade.Modules.Corporate;

public sealed class CorporateStructureTests
{
    [Theory]
    [InlineData("Corporate.NetworkStructure")]
    [InlineData("Corporate.ContactStructure")]
    [InlineData("Corporate.ReceiptDefaults")]
    [InlineData("Corporate.SupportGraph")]
    public void EveryCorporateStepHasProductionRegistration(string id) => Assert.Contains(UpgradeTables.Default.DomSteps, step => step.Id == id);

    [Fact]
    public void RegroupPreservesExactFloatTextHistoryLabelsReferencesAndIndependentCounters()
    {
        const string original = """
            <components><li Class="Mugirl.CorporateNetwork"><corporateSequence>20</corporateSequence><nextMissionId>7</nextMissionId>
            <corporateLoanPrincipal>315.0001</corporateLoanPrincipal><corporateLoanInterest>0.0002</corporateLoanInterest>
            <corporateLoanSignedDailyRate>0.019999998</corporateLoanSignedDailyRate><corporateVault><innerList><li Class="Thing"><def>Steel</def><id>Steel23</id><hitPoints>37</hitPoints></li></innerList></corporateVault>
            <corporateRecords><li><detail>  原文 &amp; 空白  </detail><key>Foreign.Unknown</key></li></corporateRecords>
            <corporateOrders><li><savedLabel>魔菇布</savedLabel><goods><li>Thing_Steel23</li><li>null</li></goods><creditedTurnover>17</creditedTurnover></li></corporateOrders>
            <corporateMissions><li><kind>Unrecognized</kind><salesGoal>-12</salesGoal><targets><li>null</li></targets><tile>91788,0</tile></li></corporateMissions></li></components>
            """;
        XmlDocument document = SaveXml.Save(original);
        XmlElement item = (XmlElement)document.SelectSingleNode("//corporateVault/innerList/li")!;
        XmlElement goods = (XmlElement)document.SelectSingleNode("//goods")!;
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(session.Report.Failed, session.Report.Error);
        XmlElement owner = (XmlElement)document.SelectSingleNode("//components/li")!;
        Assert.Equal("20", owner["sequence"]!.InnerText);
        Assert.Equal("7", owner["missionBoard"]!["nextId"]!.InnerText);
        Assert.Equal("315.0001", owner["loan"]!["principal"]!.InnerText);
        Assert.Equal("0.0002", owner["loan"]!["interest"]!.InnerText);
        Assert.Equal("0.019999998", owner["loan"]!["dailyRate"]!.InnerText);
        Assert.Equal("  原文 & 空白  ", owner.SelectSingleNode("history/entries/li/detail")!.InnerText);
        Assert.Equal("Foreign.Unknown", owner.SelectSingleNode("history/entries/li/key")!.InnerText);
        Assert.Equal("魔菇布", owner.SelectSingleNode("trade/orders/li/savedLabel")!.InnerText);
        Assert.Equal("Unrecognized", owner.SelectSingleNode("missionBoard/missions/li/kind")!.InnerText);
        Assert.Equal("-12", owner.SelectSingleNode("missionBoard/missions/li/salesGoal")!.InnerText);
        Assert.Equal("91788,0", owner.SelectSingleNode("missionBoard/missions/li/tile")!.InnerText);
        Assert.Same(item, owner.SelectSingleNode("vault/innerList/li"));
        Assert.Same(goods, owner.SelectSingleNode("trade/orders/li/goods"));
        Assert.Equal("Thing_Steel23", goods.FirstChild!.InnerText);
        Assert.Equal("null", goods.LastChild!.InnerText);
        string once = SaveXml.Game(document);
        SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.Equal(once, SaveXml.Game(document));
    }

    [Theory]
    [InlineData(0, 0, 100, 0)]
    [InlineData(3, 1, 10, 3)]
    [InlineData(3, 9, 10, 10)]
    [InlineData(3, 1, -10, -3)]
    [InlineData(2147483647, 2147483647, 2147483647, 2147483647)]
    public void OldOrderLedgerUsesSignedIntegerTruncationAndDoesNotOverflowIntermediate(int quantity, int delivered, int paid, int expected)
    {
        string actual = SaveXml.Upgraded($"<components><li Class=\"Mugirl.CorporateNetwork\"><corporateOrders><li><quantity>{quantity}</quantity><deliveredQuantity>{delivered}</deliveredQuantity><paid>{paid}</paid><creditedTurnover>-1</creditedTurnover></li></corporateOrders></li></components>", UpgradeTables.Default);
        Assert.Contains($"<creditedTurnover>{expected}</creditedTurnover>", actual, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1, null, 0)]
    [InlineData(420000, null, 1)]
    [InlineData(420000, -1, 1)]
    [InlineData(420000, 0, 0)]
    [InlineData(420000, 2, 2)]
    public void OldSupportSentinelPreservesAlreadyUsedWeek(int week, int? calls, int expected)
    {
        string callXml = calls is null ? "" : $"<corporateSupportCallsUsed>{calls}</corporateSupportCallsUsed>";
        string actual = SaveXml.Upgraded($"<components><li Class=\"Mugirl.CorporateNetwork\"><corporateSupportWeek>{week}</corporateSupportWeek>{callXml}</li></components>", UpgradeTables.Default);
        Assert.Contains($"<supportCallsUsed>{expected}</supportCallsUsed>", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiptIsMaterializedBeforeDeferredRepricingAndExplicitReceiptIsKept()
    {
        XmlDocument document = SaveXml.Save("""
            <components><li Class="Mugirl.CorporateNetwork"><corporatePeopleOffers>
            <li><id>1</id><paid>True</paid><price>4710</price><paidAmount>-1</paidAmount></li>
            <li><id>2</id><paid>True</paid><price>4710</price><paidAmount>0</paidAmount><priceVersion>0</priceVersion></li>
            <li><id>3</id><price>4710</price><priceVersion>1</priceVersion></li>
            </corporatePeopleOffers></li></components>
            """);
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(session.Report.Failed, session.Report.Error);
        XmlNodeList offers = document.SelectNodes("//people/offers/li")!;
        Assert.Equal("4710", offers[0]!["paidAmount"]!.InnerText);
        Assert.Equal("0", offers[1]!["paidAmount"]!.InnerText);
        Assert.Equal("Mugirl.SaveUpgrade.CorporateOfferPriceCompletion", offers[0]!["continuation"]!.GetAttribute("Class"));
        Assert.Equal("True", offers[2]!["continuation"]!.GetAttribute("IsNull"));
        Assert.Empty(document.SelectNodes("//priceVersion")!.Cast<XmlNode>());
    }

    [Fact]
    public void ExplicitReceiptClassesOutsideNetworkListsUseTheSameDefaults()
    {
        XmlDocument document = SaveXml.Save("""
            <extension><order Class="Mugirl.CorporateOrder"><quantity>3</quantity><paid>10</paid><deliveredQuantity>1</deliveredQuantity></order>
            <offer Class="Mugirl.CorporatePersonOffer"><paid>True</paid><price>4710</price><priceVersion>1</priceVersion></offer></extension>
            """);
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(session.Report.Failed, session.Report.Error);
        Assert.Equal("3", document.SelectSingleNode("//order/creditedTurnover")!.InnerText);
        Assert.Equal("4710", document.SelectSingleNode("//offer/paidAmount")!.InnerText);
        Assert.Equal("True", ((XmlElement)document.SelectSingleNode("//offer/continuation")!).GetAttribute("IsNull"));
        string once = SaveXml.Game(document);
        SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.Equal(once, SaveXml.Game(document));
    }

    [Fact]
    public void MixedShapesFailInsteadOfOverwritingEitherLedger()
    {
        XmlDocument document = SaveXml.Save("<components><li Class=\"Mugirl.CorporateNetwork\"><corporateLoanPrincipal>300</corporateLoanPrincipal><loan><principal>500</principal></loan></li></components>");
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.True(session.Report.Failed);
        Assert.Contains("Mixed flat and grouped", session.Report.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, 2, 2)]
    [InlineData(0, 0, 2)]
    [InlineData(0, 1, 0)]
    [InlineData(1, 1, 0)]
    public void GraphUpgradeDoesNotResetToilOrGuessTriggerIndexes(int? version, int toil, int trigger)
    {
        string versionXml = version is null ? "" : $"<supportGraphVersion>{version}</supportGraphVersion>";
        XmlDocument document = SaveXml.Save($"<maps><li><lordManager><lords><li><loadID>52</loadID><lordJob Class=\"Mugirl.LordJob_CorporateSupport\"><fallback>(3, 0, 5)</fallback>{versionXml}</lordJob><curLordToilIdx>{toil}</curLordToilIdx><ticksInToil>91</ticksInToil><triggerData><keys><li>{trigger}</li></keys><values><li><ticksPassed>19999</ticksPassed></li></values></triggerData></li></lords></lordManager></li></maps>");
        string graph = document.SelectSingleNode("//triggerData")!.OuterXml;
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(session.Report.Failed, session.Report.Error);
        Assert.Equal(toil.ToString(CultureInfo.InvariantCulture), document.SelectSingleNode("//curLordToilIdx")!.InnerText);
        Assert.Equal("91", document.SelectSingleNode("//ticksInToil")!.InnerText);
        Assert.Equal(graph, document.SelectSingleNode("//triggerData")!.OuterXml);
        XmlElement continuation = (XmlElement)document.SelectSingleNode("//lordJob/continuation")!;
        Assert.Equal(version == 1 ? "True" : "", continuation.GetAttribute("IsNull"));
        string once = SaveXml.Game(document);
        SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.Equal(once, SaveXml.Game(document));
    }

    [Fact]
    public void HistoryIsBoundedWithoutRetranslatingUnknownKeysOrNotes()
    {
        var history = new CorporateHistory();
        for (int i = 0; i < 205; i++)
        {
            history.Add(i, "Foreign.Unknown", "  已保存 文本  ", -i);
        }

        Assert.Equal(200, history.Entries.Count);
        Assert.Equal(5, history.Entries[0].tick);
        Assert.Equal(204, history.Entries[^1].tick);
        Assert.All(history.Entries, row => Assert.Equal("  已保存 文本  ", row.detail));
    }

    [Fact]
    public void MissionIdsRemainIndependentAndUnknownKindPayloadSurvives()
    {
        var board = new CorporateMissionBoard { nextId = 7 };
        CorporateMission mission = board.Create((CorporateMissionKind)91, 1200);
        Assert.Equal(7, mission.id);
        Assert.Equal(8, board.nextId);
        Assert.Equal(421200, mission.acceptByTick);
        Assert.Equal((CorporateMissionKind)91, mission.kind);
        board.missions.Add(mission);
        Assert.Same(mission, board.Find(7));
    }
}
