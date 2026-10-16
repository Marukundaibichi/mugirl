using System.Linq;
using System.Xml;
using Mugirl.Features.Mounting;
using Mugirl.Features.WeaponWheel;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade.Modules.Combat;

public sealed class CombatXmlFormattingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndentedWheelListsKeepEveryItemValueAndAreIdempotent(bool preserveSpace)
    {
        string attribute = preserveSpace ? " xml:space='preserve'" : string.Empty;
        XmlDocument document = Document("<thing>\n  <def>Mugirl</def>\n  <id>Mugirl1</id>\n"
            + "  <weaponWheelSlots" + attribute + ">\n    <li>  Thing_Gun7\t </li>\n    <li><![CDATA[ null \n]]></li>\n  </weaponWheelSlots>\n"
            + "  <weaponWheelUnlockedSlots" + attribute + ">\n    <li> True </li>\n    <li>False</li>\n    <li>  True\n</li>\n  </weaponWheelUnlockedSlots>\n"
            + "  <nick>  Thing_Gun7\t </nick>\n</thing>");
        XmlElement thing = document.DocumentElement!["game"]!["thing"]!;
        string[] weapons = Items(thing[CombatWheelSlotRecordsStep.OldWeapons]!).Select(item => item.InnerXml).ToArray();
        string[] unlocks = Items(thing[CombatWheelSlotRecordsStep.OldUnlocks]!).Select(item => item.InnerXml).ToArray();
        string nick = thing["nick"]!.OuterXml;
        var tables = Tables(new CombatWheelSlotRecordsStep());
        var catalog = new TestCatalog().With("ThingDef", "Mugirl", SaveTypeNames.Of(typeof(CompWeaponWheel)));
        SaveUpgradeSession session = SaveXml.Upgrade(document, tables, catalog);

        Assert.False(session.Report.Failed, session.Report.Error);
        XmlElement[] records = Items(thing[CombatWheelSlotRecordsStep.NewRecords]!);
        Assert.Equal(3, records.Length);
        Assert.Equal(weapons, records.Take(2).Select(item => item["weapon"]!.InnerXml));
        Assert.Equal(unlocks, records.Select(item => item["unlocked"]!.InnerXml));
        Assert.Null(records[2]["weapon"]);
        Assert.Equal(nick, thing["nick"]!.OuterXml);
        string once = document.OuterXml;
        SaveUpgradeSession second = SaveXml.Upgrade(document, tables, catalog);
        Assert.False(second.Report.Failed, second.Report.Error);
        Assert.Equal(once, document.OuterXml);
        Assert.Equal(0, SaveXml.Actions(second, CombatWheelSlotRecordsStep.StepId));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("&#160;")]
    [InlineData("<![CDATA[garbage]]>")]
    [InlineData("<![CDATA[  ]]>")]
    [InlineData("<entry>Thing_Gun7</entry>")]
    [InlineData("<!-- unknown annotation -->")]
    [InlineData("<?unknown preserve?>")]
    public void NonFormattingListNodesFailBeforeAnyOwnerIsEdited(string unexpected)
    {
        XmlDocument document = Document("<thing><def>Mugirl</def><id>Mugirl1</id><weaponWheelSlots>\n<li>null</li>\n</weaponWheelSlots></thing>"
            + "<thing><def>Mugirl</def><id>Mugirl2</id><weaponWheelSlots>\n" + unexpected + "\n<li>null</li>\n</weaponWheelSlots></thing>");
        string before = document.OuterXml;
        var tables = Tables(new CombatWheelSlotRecordsStep());
        SaveUpgradeSession session = SaveXml.Upgrade(document, tables, new TestCatalog().With("ThingDef", "Mugirl", SaveTypeNames.Of(typeof(CompWeaponWheel))));
        Assert.True(session.Report.Failed);
        Assert.Equal(CombatWheelSlotRecordsStep.StepId, session.Report.FailedStep);
        Assert.Equal(before, document.OuterXml);
    }

    [Fact]
    public void IndentedNestedMountStateKeepsContainerIdentityAndExactLeafText()
    {
        XmlDocument document = Document("<thing>\n <def>Mugirl</def>\n <id>Mugirl1</id>\n <innerContainer>\n  <innerList>\n"
            + "   <li Class='Pawn'>\n    <def>Mugirl</def>\n    <id>Mugirl2</id>\n    <safetyTickCounter> -7 </safetyTickCounter>\n   </li>\n"
            + "  </innerList>\n </innerContainer>\n <physiologicalTickCounter>  37\t</physiologicalTickCounter>\n"
            + " <turretAimTarget><![CDATA[Thing_Human3]]></turretAimTarget>\n <nick> 37 </nick>\n</thing>");
        XmlElement thing = document.DocumentElement!["game"]!["thing"]!;
        XmlElement contents = thing["innerContainer"]!["innerList"]!;
        XmlElement rider = contents["li"]!;
        string counter = thing["physiologicalTickCounter"]!.InnerXml;
        string target = thing["turretAimTarget"]!.InnerXml;
        string safety = rider["safetyTickCounter"]!.InnerXml;
        var tables = Tables(new MountStateUpgrade());
        var catalog = new TestCatalog().With("ThingDef", "Mugirl", SaveTypeNames.Of(typeof(CompMount)));
        SaveUpgradeSession session = SaveXml.Upgrade(document, tables, catalog);

        Assert.False(session.Report.Failed, session.Report.Error);
        Assert.Same(contents, thing["mugirlMountRiders"]!["innerList"]);
        Assert.Same(rider, contents["li"]);
        Assert.Equal(counter, thing["mugirlMountSchedule"]!["physiology"]!.InnerXml);
        Assert.Equal(target, thing["mugirlMountTurret"]!["aimTarget"]!.InnerXml);
        Assert.Equal(safety, rider["mugirlMountSchedule"]!["safety"]!.InnerXml);
        Assert.Equal(" 37 ", thing["nick"]!.InnerText);
        string once = document.OuterXml;
        Assert.False(SaveXml.Upgrade(document, tables, catalog).Report.Failed);
        Assert.Equal(once, document.OuterXml);
    }

    private static XmlElement[] Items(XmlElement element) => [.. element.ChildNodes.OfType<XmlElement>()];

    private static UpgradeTables Tables(DomUpgradeStep step)
    {
        var builder = new UpgradeTableBuilder();
        builder.Module("Combat", 600).Step(step);
        return builder.Build();
    }

    private static XmlDocument Document(string body)
    {
        var xml = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        xml.LoadXml(SaveXml.Save(body).OuterXml);
        // SaveXml uses its normal no-whitespace parser; reload the original body to retain indentation.
        xml.DocumentElement!["game"]!.InnerXml = body;
        return xml;
    }
}
