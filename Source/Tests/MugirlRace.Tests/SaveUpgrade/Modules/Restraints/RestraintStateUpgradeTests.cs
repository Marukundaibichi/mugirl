using System.Globalization;
using System.Xml;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade.Modules.Restraints;

public sealed class RestraintStateUpgradeTests
{
    [Theory]
    [InlineData(-3)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void OmittedLockCountUsesTheOldDefDefault(int keys)
    {
        XmlDocument document = Upgrade("<thing Class=\"Mugirl.SlaveApparel\"><def>Mugirl_Slaveboots</def><id>Mugirl_Slaveboots1</id><isLocked>False</isLocked></thing>",
            new TestCatalog().WithInteger("ThingDef", "Mugirl_SlaveBoots", "needkeynumber", keys));
        Assert.Equal(keys.ToString(CultureInfo.InvariantCulture), document.SelectSingleNode("//mugirlRestraintLockCount")!.InnerText);
        Assert.Equal("False", document.SelectSingleNode("//mugirlRestraintLocked")!.InnerText);
    }

    [Theory]
    [InlineData("0", "True")]
    [InlineData("4", "False")]
    [InlineData("-2", "True")]
    public void CountAndLockedFlagRemainIndependent(string count, string locked)
    {
        XmlDocument document = Upgrade("<thing Class=\"Mugirl.SlaveApparel\"><def>Mugirl_Shackles</def><id>Mugirl_Shackles1</id><lockCount>" + count + "</lockCount><isLocked>" + locked + "</isLocked></thing>");
        Assert.Equal(count, document.SelectSingleNode("//mugirlRestraintLockCount")!.InnerText);
        Assert.Equal(locked, document.SelectSingleNode("//mugirlRestraintLocked")!.InnerText);
    }

    [Fact]
    public void FirstSharedBrainwashValueIsCopiedOnlyToRealComponents()
    {
        XmlDocument document = Upgrade("<thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><hediffSet><hediffs><li><def>Mugirl_BrainWashing_Level5</def><age>15</age><age>99</age><triggered>True</triggered><triggered>False</triggered><mugirlAddTraitTriggered>False</mugirlAddTraitTriggered><performancePlayer><age>321</age></performancePlayer></li><li><def>ForeignHediff</def><age>44</age><triggered>True</triggered></li></hediffs></hediffSet></thing>");
        Assert.Equal("15", document.SelectSingleNode("//mugirlAddMentalStateAge")!.InnerText);
        Assert.Equal("15", document.SelectSingleNode("//mugirlAddTraitAge")!.InnerText);
        Assert.Equal("False", document.SelectSingleNode("//mugirlAddTraitTriggered")!.InnerText);
        Assert.Null(document.SelectSingleNode("//mugirlAddThoughtAge"));
        Assert.Equal("321", document.SelectSingleNode("//mugirlBrainwashPerformance/age")!.InnerText);
        Assert.Equal("44", document.SelectSingleNode("//hediffs/li[2]/age")!.InnerText);
    }

    [Fact]
    public void ExistingCanonicalLockAndBrainwashFieldsWin()
    {
        XmlDocument document = Upgrade("<thing Class=\"Mugirl.SlaveApparel\"><def>Mugirl_Shackles</def><id>Mugirl_Shackles1</id><lockCount>4</lockCount><isLocked>True</isLocked><mugirlRestraintLockCount>0</mugirlRestraintLockCount><mugirlRestraintLocked>False</mugirlRestraintLocked></thing>");
        Assert.Equal("0", document.SelectSingleNode("//mugirlRestraintLockCount")!.InnerText);
        Assert.Equal("False", document.SelectSingleNode("//mugirlRestraintLocked")!.InnerText);
        Assert.Null(document.SelectSingleNode("//lockCount"));
    }

    [Fact]
    public void ComponentTimersAndShockDictionaryRemainExact()
    {
        XmlDocument document = Upgrade("<thing><def>Mugirl_MagneticShackles</def><id>Mugirl_MagneticShackles1</id><lastManualUseTick>-999999</lastManualUseTick><isActive>True</isActive><currentCycleInterval>21743</currentCycleInterval><currentBindDurationTicks>12345</currentBindDurationTicks><nextStateTick>234567</nextStateTick></thing><thing><def>Mugirl_ShockCollar</def><id>Mugirl_ShockCollar2</id><ticks>17</ticks><lastManualUseTick>-99999</lastManualUseTick></thing><thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl3</id><hediffSet><hediffs><li><def>Mugirl_EleShock</def><jitterTicksLeft>9</jitterTicksLeft><filthTimers><keys><li>MugirlMilkFilth</li></keys><values><li>11</li></values></filthTimers></li></hediffs></hediffSet></thing>");
        Assert.Equal("21743", document.SelectSingleNode("//mugirlShacklesCycleInterval")!.InnerText);
        Assert.Equal("12345", document.SelectSingleNode("//mugirlShacklesBindDurationTicks")!.InnerText);
        Assert.Equal("234567", document.SelectSingleNode("//mugirlShacklesNextStateTick")!.InnerText);
        Assert.Equal("17", document.SelectSingleNode("//mugirlShockCheckTicks")!.InnerText);
        Assert.Equal("-99999", document.SelectSingleNode("//mugirlShockLastManualUseTick")!.InnerText);
        Assert.Equal("<keys><li>MugirlMilkFilth</li></keys><values><li>11</li></values>", document.SelectSingleNode("//mugirlShockFilthTimers")!.InnerXml);
    }

    [Fact]
    public void OmittedMagneticScheduleKeepsTheOldUnscheduledSentinel()
    {
        XmlDocument document = Upgrade("<thing Class=\"Mugirl.AdvancedSlaveApparel\"><def>Mugirl_MagneticShackles</def><id>Mugirl_MagneticShackles1</id><lockCount>2</lockCount><currentCycleInterval>12345</currentCycleInterval></thing>");
        Assert.Equal("-1", document.SelectSingleNode("//mugirlShacklesNextStateTick")!.InnerText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<lockedApparel IsNull=\"True\" />")]
    [InlineData("<lockedApparel />")]
    public void MissingWornLockListIsMaterializedWithoutMixedContent(string locks)
    {
        XmlDocument document = Upgrade("<thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><apparel><wornApparel><innerList><li Class=\"Mugirl.SlaveApparel\"><def>Mugirl_Shackles</def><id>Mugirl_Shackles2</id><lockCount>2</lockCount></li></innerList></wornApparel>" + locks + "</apparel></thing>");
        Assert.Equal("<li>Thing_Mugirl_Shackles2</li>", document.SelectSingleNode("//lockedApparel")!.InnerXml);
        Assert.False(((XmlElement)document.SelectSingleNode("//lockedApparel")!).HasAttribute("IsNull"));
    }

    private static XmlDocument Upgrade(string body, IDefCatalog? catalog = null)
    {
        XmlDocument document = SaveXml.Save(body);
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default, catalog);
        Assert.False(session.Report.Failed, session.Report.FailedStep + ": " + session.Report.Error);
        SaveXml.AssertNoMixedContent(document.DocumentElement!);
        string once = document.OuterXml;
        Assert.False(SaveXml.Upgrade(document, UpgradeTables.Default, catalog).Report.Failed);
        Assert.Equal(once, document.OuterXml);
        return document;
    }
}
