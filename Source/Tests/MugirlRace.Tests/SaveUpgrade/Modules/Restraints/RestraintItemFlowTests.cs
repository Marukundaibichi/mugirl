using System.Xml;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade.Modules.Restraints;

public sealed class RestraintItemFlowTests
{
    [Theory]
    [InlineData(0, 3, 7)]
    [InlineData(0, 5, 7)]
    [InlineData(0, 7, 7)]
    [InlineData(1, 2, 6)]
    [InlineData(1, 4, 6)]
    [InlineData(1, 6, 6)]
    [InlineData(2, 4, 8)]
    [InlineData(2, 6, 8)]
    [InlineData(2, 8, 8)]
    [InlineData(3, 3, 7)]
    [InlineData(3, 5, 7)]
    [InlineData(3, 7, 7)]
    public void PositiveDelaySelectsOneSemanticStageAcrossEveryLayout(int kind, int current, int expected)
    {
        var state = new RestraintItemFlow.Snapshot(current, -1, 17, false, false, RestraintItemFlow.Destination.Unknown);
        Assert.Equal(new RestraintItemFlow.Result(expected, -1), RestraintItemFlow.Resolve((RestraintItemFlow.Kind)kind, state));
    }

    [Theory]
    [InlineData(0, 2, 1, 3)]
    [InlineData(0, 2, 2, 6)]
    [InlineData(0, 3, 1, 3)]
    [InlineData(0, 4, 2, 6)]
    [InlineData(0, 6, 2, 6)]
    [InlineData(1, 2, 1, 3)]
    [InlineData(1, 3, 1, 3)]
    [InlineData(2, 3, 1, 4)]
    [InlineData(2, 3, 2, 7)]
    [InlineData(2, 4, 1, 4)]
    [InlineData(2, 5, 2, 7)]
    [InlineData(2, 7, 2, 7)]
    [InlineData(3, 2, 1, 3)]
    [InlineData(3, 2, 2, 6)]
    public void SavedPathTargetDisambiguatesMovement(int kind, int current, int destination, int expected)
    {
        var state = new RestraintItemFlow.Snapshot(current, -1, -11, false, true, (RestraintItemFlow.Destination)destination);
        Assert.Equal(expected, RestraintItemFlow.Resolve((RestraintItemFlow.Kind)kind, state).Current);
    }

    [Theory]
    [InlineData(3, 4, 8)]
    [InlineData(3, 5, 9)]
    [InlineData(3, 6, 9)]
    [InlineData(5, 7, 9)]
    public void PendingTerminalNoopNeverReplaysTheEffect(int current, int next, int expectedNext)
    {
        var state = new RestraintItemFlow.Snapshot(current, next, 1, true, false, RestraintItemFlow.Destination.Unknown);
        Assert.Equal(new RestraintItemFlow.Result(7, expectedNext), RestraintItemFlow.Resolve(RestraintItemFlow.Kind.Crack, state));
    }

    [Theory]
    [InlineData(2, -1, -4, false, true, 3)]
    [InlineData(3, -1, 0, true, false, 0)]
    [InlineData(4, -1, -1, false, false, 0)]
    [InlineData(2, 1, -4, false, true, 2)]
    public void AmbiguousOrUnperformedInventoryTransfersFailClosed(int current, int next, int ticks, bool wantNext, bool moving, int destination)
    {
        var state = new RestraintItemFlow.Snapshot(current, next, ticks, wantNext, moving, (RestraintItemFlow.Destination)destination);
        Assert.Throws<InvalidOperationException>(() => RestraintItemFlow.Resolve(RestraintItemFlow.Kind.Crack, state));
    }

    [Fact]
    public void DomPreservesDurationTargetsHeldObjectsAndCurrentMarkerIsIdempotent()
    {
        XmlDocument document = SaveXml.Save(Body("3", "17", "<nextToilIndex>5</nextToilIndex><wantBeginNextToil>True</wantBeginNextToil>"));
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(session.Report.Failed, session.Report.Error);
        XmlElement driver = (XmlElement)document.SelectSingleNode("//curDriver")!;
        Assert.Equal("7", driver["curToilIndex"]!.InnerText);
        Assert.Equal("9", driver["nextToilIndex"]!.InnerText);
        Assert.Equal("17", driver["ticksLeftThisToil"]!.InnerText);
        Assert.Equal("True", driver["wantBeginNextToil"]!.InnerText);
        Assert.Equal("1", driver["itemFlowVersion"]!.InnerText);
        Assert.Equal("Thing_Steel2", document.SelectSingleNode("//curJob/targetA")!.InnerText);
        Assert.Equal("<li><def>Steel</def><id>Steel2</id><stackCount>1</stackCount></li>", document.SelectSingleNode("//carryTracker/innerContainer/innerList")!.InnerXml);
        string once = document.OuterXml;
        Assert.False(SaveXml.Upgrade(document, UpgradeTables.Default).Report.Failed);
        Assert.Equal(once, document.OuterXml);
    }

    [Fact]
    public void UnprovenBusyTransitionDoesNotApplyPartialIndexEdits()
    {
        XmlDocument document = SaveXml.Save(Body("3", "0", "<wantBeginNextToil>True</wantBeginNextToil>"));
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.True(session.Report.Failed);
        Assert.Equal(RestraintItemJobStep.StepId, session.Report.FailedStep);
        Assert.Equal("3", document.SelectSingleNode("//curToilIndex")!.InnerText);
        Assert.Null(document.SelectSingleNode("//itemFlowVersion"));
    }

    [Fact]
    public void CanonicalLayoutAndVanillaUseItemBranchRetainAllIndices()
    {
        XmlDocument canonical = SaveXml.Save(Body("8", "0", "<itemFlowVersion>1</itemFlowVersion><nextToilIndex>9</nextToilIndex><wantBeginNextToil>True</wantBeginNextToil>"));
        Assert.False(SaveXml.Upgrade(canonical, UpgradeTables.Default).Report.Failed);
        Assert.Equal("8", canonical.SelectSingleNode("//curToilIndex")!.InnerText);
        Assert.Equal("9", canonical.SelectSingleNode("//nextToilIndex")!.InnerText);
        XmlDocument vanilla = SaveXml.Save("<thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><jobs><curJob><def>GiveSlaveApparelGear</def><targetB>null</targetB></curJob><curDriver Class=\"Mugirl.JobDriver_UseItemOn\"><curToilIndex>4</curToilIndex><ticksLeftThisToil>23</ticksLeftThisToil></curDriver></jobs></thing>");
        Assert.False(SaveXml.Upgrade(vanilla, UpgradeTables.Default).Report.Failed);
        Assert.Equal("4", vanilla.SelectSingleNode("//curToilIndex")!.InnerText);
        Assert.Equal("23", vanilla.SelectSingleNode("//ticksLeftThisToil")!.InnerText);
        Assert.Equal("1", vanilla.SelectSingleNode("//itemFlowVersion")!.InnerText);
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    public void UnstartedWorkRetainsTheVanillaSentinel(int next, int expected)
    {
        var state = new RestraintItemFlow.Snapshot(-1, next, 0, true, false, RestraintItemFlow.Destination.Unknown);
        Assert.Equal(new RestraintItemFlow.Result(-1, expected), RestraintItemFlow.Resolve(RestraintItemFlow.Kind.Crack, state));
    }

    [Fact]
    public void UnstartedExplicitLaterJumpAndUnknownLayoutVersionAreRejected()
    {
        var state = new RestraintItemFlow.Snapshot(-1, 1, 0, true, false, RestraintItemFlow.Destination.Unknown);
        Assert.Throws<InvalidOperationException>(() => RestraintItemFlow.Resolve(RestraintItemFlow.Kind.Crack, state));
        XmlDocument document = SaveXml.Save(Body("7", "23", "<itemFlowVersion>2</itemFlowVersion>"));
        Assert.True(SaveXml.Upgrade(document, UpgradeTables.Default).Report.Failed);
        Assert.Equal("7", document.SelectSingleNode("//curToilIndex")!.InnerText);
    }

    [Fact]
    public void UseItemOnCellTargetStillUsesTheVanillaNoThingBranch()
    {
        XmlDocument document = SaveXml.Save("<thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><jobs><curJob><def>GiveSlaveApparelGear</def><targetB>(4, 0, 7)</targetB></curJob><curDriver Class=\"Mugirl.JobDriver_UseItemOn\"><curToilIndex>4</curToilIndex><ticksLeftThisToil>23</ticksLeftThisToil></curDriver></jobs></thing>");
        Assert.False(SaveXml.Upgrade(document, UpgradeTables.Default).Report.Failed);
        Assert.Equal("4", document.SelectSingleNode("//curToilIndex")!.InnerText);
        Assert.Equal("(4, 0, 7)", document.SelectSingleNode("//curJob/targetB")!.InnerText);
    }

    private static string Body(string current, string ticks, string extra)
        => "<thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><carryTracker><innerContainer><innerList><li><def>Steel</def><id>Steel2</id><stackCount>1</stackCount></li></innerList></innerContainer></carryTracker><jobs><curJob><def>CrackBondageGear</def><targetA>Thing_Steel2</targetA><targetB>Thing_Mugirl_ShockCollar3</targetB></curJob><curDriver Class=\"Mugirl.JobDriver_CrackBondageGear\"><curToilIndex>" + current + "</curToilIndex><ticksLeftThisToil>" + ticks + "</ticksLeftThisToil>" + extra + "</curDriver></jobs></thing><thing Class=\"Mugirl.AdvancedSlaveApparel\"><def>Mugirl_ShockCollar</def><id>Mugirl_ShockCollar3</id><lockCount>2</lockCount></thing>";
}
