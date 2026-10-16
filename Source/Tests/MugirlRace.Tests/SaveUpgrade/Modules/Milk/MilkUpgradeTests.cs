using System.Globalization;
using System.Reflection;
using System.Xml;
using Mugirl.Features.Milk;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade.Modules.Milk;

public sealed class MilkUpgradeTests
{
    [Theory]
    [InlineData(1000, -99999, 60, 0)]
    [InlineData(1000, -1, 60, 0)]
    [InlineData(1000, 990, 60, 10)]
    [InlineData(1000, 1000, 60, 0)]
    [InlineData(1000, 1200, 60, 0)]
    [InlineData(500000, 1000, 60, 60)]
    [InlineData(1000, 998, 0, 1)]
    public void DeferredAnchorPreservesExactWindow(int now, int anchor, int interval, int expected)
        => Assert.Equal(expected, LegacyMilkAccrualContinuation.PendingTicks(now, anchor, interval));

    [Theory]
    [InlineData(-99999, 37)]
    [InlineData(-1, 0)]
    [InlineData(990, 10)]
    public void ApplyingContinuationPreservesSentinelAndOverridesOnlyWhenSpecified(int anchor, int expected)
    {
        var comp = new CompMilkProduction();
        comp.RestoreAccrualWindow(37);
        new LegacyMilkAccrualContinuation(anchor).Apply(comp, 1000, 60);
        Assert.Equal(expected, typeof(CompMilkProduction).GetField("mugirlMilkPendingTicks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(comp));
    }

    [Fact]
    public void OldAccrualAnchorIsNotSettledAtLoadTime()
    {
        XmlDocument document = Upgrade("<maps><li><things><thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl8</id><milkFullness>0.3150001</milkFullness><milkFullness_pendingResourceTicks>37</milkFullness_pendingResourceTicks><milkFullness_lastResourceUpdateTick>990</milkFullness_lastResourceUpdateTick></thing></things></li></maps>");
        XmlElement pawn = (XmlElement)document.SelectSingleNode("//thing")!;
        Assert.Equal("0.3150001", pawn["mugirlMilkFullness"]!.InnerText);
        Assert.Equal("37", pawn["mugirlMilkPendingTicks"]!.InnerText);
        Assert.Equal("990", pawn["mugirlMilkAccrualContinuation"]!["anchorTick"]!.InnerText);
        Assert.Equal(typeof(LegacyMilkAccrualContinuation).FullName, pawn["mugirlMilkAccrualContinuation"]!.GetAttribute("Class"));
        string before = document.OuterXml;
        Assert.False(SaveXml.Upgrade(document, UpgradeTables.Default).Report.Failed);
        Assert.Equal(before, document.OuterXml);
    }

    [Fact]
    public void MilkFieldsNeverCaptureOtherThingFieldsWithSameNames()
    {
        const string body = "<maps><li><things><thing><def>OtherRace</def><milkFullness>0.125</milkFullness><milkFullness_lastResourceUpdateTick>900</milkFullness_lastResourceUpdateTick></thing></things></li></maps>";
        Assert.Equal(body, SaveXml.Game(Upgrade(body)));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("299", 299)]
    [InlineData("-3", -3)]
    public void XenotypeScheduleRetainsRemainingTicks(string oldTicks, int expected)
    {
        XmlDocument document = Upgrade("<components><li Class=\"Mugirl.Mugirl_XenotypeFix_GameComp\"><xenotypeFixed>True</xenotypeFixed><throwGeneFixed>True</throwGeneFixed><dunkGeneFixed>False</dunkGeneFixed><throwSystemGeneFixed>True</throwSystemGeneFixed><checkTimer>" + oldTicks + "</checkTimer></li></components>");
        XmlElement component = (XmlElement)document.SelectSingleNode("//components/li")!;
        Assert.Equal("True", component["xenotypeRepairPending"]!.InnerText);
        Assert.Equal(expected.ToString(CultureInfo.InvariantCulture), component["xenotypeRepairTicks"]!.InnerText);
        Assert.Null(component["checkTimer"]);
        Assert.Null(component["dunkGeneFixed"]);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void XenotypeCompletionIsTheConjunctionOfAllFourFlags(bool third, bool fourth, bool pending)
    {
        XmlDocument document = Upgrade("<components><li Class=\"Mugirl.Mugirl_XenotypeFix_GameComp\"><xenotypeFixed>True</xenotypeFixed><throwGeneFixed>True</throwGeneFixed><dunkGeneFixed>" + third + "</dunkGeneFixed><throwSystemGeneFixed>" + fourth + "</throwSystemGeneFixed></li></components>");
        Assert.Equal(pending.ToString(), document.SelectSingleNode("//xenotypeRepairPending")!.InnerText);
        Assert.Equal("300", document.SelectSingleNode("//xenotypeRepairTicks")!.InnerText);
    }

    [Fact]
    public void DisabledBiotechPausesAndThe301stEffectiveTickRuns()
    {
        int remaining = 300;
        for (int i = 0; i < 1000; i++)
        {
            Assert.False(XenotypeRepairTiming.ShouldRun(ref remaining, true, false));
        }
        Assert.Equal(300, remaining);
        for (int i = 0; i < 300; i++)
        {
            Assert.False(XenotypeRepairTiming.ShouldRun(ref remaining, true, true));
        }
        Assert.Equal(0, remaining);
        Assert.True(XenotypeRepairTiming.ShouldRun(ref remaining, true, true));
        Assert.False(XenotypeRepairTiming.ShouldRun(ref remaining, false, true));
    }

    [Fact]
    public void ReleaseScheduleListsAndFractionalBirthTickArePreservedVerbatim()
    {
        XmlDocument document = Upgrade("<maps><li><things><thing><def>Mugirl_MilkingDevice</def><id>Mugirl_MilkingDevice4</id><storedCharges>9</storedCharges><storedMilkAmount>113</storedMilkAmount><effectTicksRemaining>17</effectTicksRemaining><effectAge>13</effectAge><nextSoundTicks><li>-1</li><li>14</li></nextSoundTicks><nextFleckTicks><li>15</li></nextFleckTicks><nextTextTicks><li>29</li></nextTextTicks></thing><thing Class=\"Pawn\"><def>Mugirl</def><hediffSet><hediffs><li><def>Mugirl_Lactation</def><lastBirthTick>16777218</lastBirthTick></li><li><def>Consumed_MugirlMilk</def><flagAmIThinking>True</flagAmIThinking><checkingCounter>11</checkingCounter></li></hediffs></hediffSet></thing></things></li></maps>");
        Assert.Equal("<li>-1</li><li>14</li>", document.SelectSingleNode("//mugirlReleaseNextSoundTicks")!.InnerXml);
        Assert.Equal("17", document.SelectSingleNode("//mugirlReleaseEffectTicksRemaining")!.InnerText);
        Assert.Equal("13", document.SelectSingleNode("//mugirlReleaseAge")!.InnerText);
        Assert.Equal("16777218", document.SelectSingleNode("//mugirlLactationLastBirthTick")!.InnerText);
        Assert.Equal("11", document.SelectSingleNode("//mugirlThoughtCheckTicks")!.InnerText);
    }

    [Theory]
    [InlineData("Job_MugirlMilkingTargetWait", "Thing_Mugirl1", "1", true)]
    [InlineData("Job_MugirlMilkingTargetWait", "Thing_Mugirl3", "1", false)]
    [InlineData("Wait_MaintainPosture", "Thing_Mugirl1", "1", false)]
    [InlineData("LayDown", "Thing_Bed4", "1", false)]
    [InlineData("Job_MugirlMilkingTargetWait", "Thing_Mugirl1", "0", false)]
    public void GatheringClaimsOnlyProvenPartnerWait(string waitDef, string waitTarget, string toil, bool relinked)
    {
        XmlDocument document = Upgrade("<maps><li><things><thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><jobs><curJob><def>Job_GatherMilk</def><targetA>Thing_Mugirl2</targetA></curJob><curDriver Class=\"Mugirl.JobDriver_GatherMilk\"><curToilIndex>" + toil + "</curToilIndex><nextToilIndex>-1</nextToilIndex><wantBeginNextToil>False</wantBeginNextToil><ticksLeftThisToil>117</ticksLeftThisToil><gatherProgress>315.0001</gatherProgress></curDriver></jobs></thing><thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl2</id><jobs><curJob><def>" + waitDef + "</def><loadID>81</loadID><targetA>" + waitTarget + "</targetA></curJob></jobs></thing></things></li></maps>");
        XmlElement driver = (XmlElement)document.SelectSingleNode("//curDriver")!;
        Assert.Equal(toil, driver["curToilIndex"]!.InnerText);
        Assert.Equal("-1", driver["nextToilIndex"]!.InnerText);
        Assert.Equal("False", driver["wantBeginNextToil"]!.InnerText);
        Assert.Equal("117", driver["ticksLeftThisToil"]!.InnerText);
        Assert.Equal("315.0001", driver["gatherProgress"]!.InnerText);
        Assert.Equal(relinked ? "81" : null, driver["forcedWaitJobLoadId"]?.InnerText);
        Assert.Equal(relinked ? "Thing_Mugirl2" : null, driver["activeGatherTarget"]?.InnerText);
        string once = document.OuterXml;
        Assert.False(SaveXml.Upgrade(document, UpgradeTables.Default).Report.Failed);
        Assert.Equal(once, document.OuterXml);
    }

    [Theory]
    [InlineData("", "True", "-1")]
    [InlineData("<gatherEffectsActive>False</gatherEffectsActive>", "False", null)]
    [InlineData("<forcedWaitJobLoadId>23</forcedWaitJobLoadId>", null, "23")]
    [InlineData("<activeGatherTarget>Thing_Mugirl99</activeGatherTarget>", null, null)]
    public void SelfGatheringInsideAnotherHolderPreservesAnyAlreadySavedState(string existing, string? effects, string? waitId)
    {
        XmlDocument document = Upgrade("<container><innerList><li Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><jobs><curJob><def>Job_GatherMilk</def><targetA>Thing_Mugirl1</targetA></curJob><curDriver Class=\"Mugirl.JobDriver_GatherMilk\"><curToilIndex>1</curToilIndex><gatherProgress>27.5</gatherProgress>" + existing + "</curDriver></jobs></li></innerList></container>");
        XmlElement driver = (XmlElement)document.SelectSingleNode("//curDriver")!;
        Assert.Equal("27.5", driver["gatherProgress"]!.InnerText);
        Assert.Equal(effects, driver["gatherEffectsActive"]?.InnerText);
        Assert.Equal(waitId, driver["forcedWaitJobLoadId"]?.InnerText);
    }

    [Fact]
    public void DuplicateGatheringPawnIdentityFailsInsteadOfGuessing()
    {
        const string Body = "<thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><jobs><curJob><targetA>Thing_Mugirl1</targetA></curJob><curDriver Class=\"Mugirl.JobDriver_GatherMilk\"><curToilIndex>1</curToilIndex></curDriver></jobs></thing><thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><jobs /></thing>";
        SaveUpgradeSession session = SaveXml.Upgrade(SaveXml.Save(Body), UpgradeTables.Default);
        Assert.True(session.Report.Failed);
        Assert.Equal(MilkGatherContinuationStep.StepId, session.Report.FailedStep);
    }

    private static XmlDocument Upgrade(string body)
    {
        XmlDocument document = SaveXml.Save(body);
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(session.Report.Failed, session.Report.FailedStep + ": " + session.Report.Error);
        SaveXml.AssertNoMixedContent(document.DocumentElement!);
        return document;
    }
}
