using System.Collections.Generic;
using System.Linq;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade;

/// <summary>标签表与所属元素选择器：物品组件（旧版关系与 Def 目录）、健康状态组件、类、组件列表、路径。</summary>
public sealed class LabelSelectorTests
{
    private static readonly string CompMilk = Fakes.Name<Fakes.CompMilk>();

    [Fact]
    public void ThingCompUsesTheLegacyDefCompRelation()
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.RenameType("Mugirl.CompOldMilk", typeof(Fakes.CompMilk), "renamed comp");
            m.DeclareLegacyComps("ThingDef", "Mugirl", "Mugirl.CompOldMilk");
            m.RenameLabel(OwnerSelector.ThingComp(typeof(Fakes.CompMilk)), "milkFullness", "mugirlMilkFullness", "shared element prefix");
        });

        // 当前 Def 目录说 Mugirl 没有这个组件（组件已移走）：旧版关系优先。另一个种族的同名标签属于别的 Mod，不动。
        var catalog = new TestCatalog().With("ThingDef", "Mugirl").With("ThingDef", "Human", "Other.CompMilk");
        string result = SaveXml.Upgraded(
            "<li Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><milkFullness>0.5</milkFullness></li>"
            + "<li Class=\"Pawn\"><def>Human</def><id>Human2</id><milkFullness>0.7</milkFullness></li>",
            tables,
            catalog);

        Assert.Equal(
            "<li Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><mugirlMilkFullness>0.5</mugirlMilkFullness></li>"
            + "<li Class=\"Pawn\"><def>Human</def><id>Human2</id><milkFullness>0.7</milkFullness></li>",
            result);
    }

    [Fact]
    public void ThingCompFallsBackToTheCatalogForThirdPartyDefs()
    {
        UpgradeTables tables = SaveXml.Tables(m => m.RenameLabel(OwnerSelector.ThingComp(CompMilk), "milkFullness", "mugirlMilkFullness", "shared element prefix"));
        var catalog = new TestCatalog().With("ThingDef", "WhiteFluff_Race", CompMilk, "WhiteFluff.CompWhiteFluffMilk");

        string result = SaveXml.Upgraded("<thing><def>WhiteFluff_Race</def><id>WhiteFluff_Race9</id><milkFullness>1</milkFullness></thing>", tables, catalog);

        Assert.Equal("<thing><def>WhiteFluff_Race</def><id>WhiteFluff_Race9</id><mugirlMilkFullness>1</mugirlMilkFullness></thing>", result);
    }

    [Fact]
    public void ThingCompResolvesRenamedDefsThroughTheDefTable()
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.RenameDef("ThingDef", "Mugirl_OldCow", "Mugirl_Cow", "renamed");
            m.RenameLabel(OwnerSelector.ThingComp(CompMilk), "milk", "mugirlMilk", "prefix");
        });
        var catalog = new TestCatalog().With("ThingDef", "Mugirl_Cow", CompMilk);

        var document = SaveXml.Save("<thing><def>Mugirl_OldCow</def><id>Mugirl_OldCow3</id><milk>1</milk></thing>");
        SaveXml.Upgrade(document, tables, catalog);
        Assert.Equal("<thing><def>Mugirl_OldCow</def><id>Mugirl_Cow3</id><mugirlMilk>1</mugirlMilk></thing>", SaveXml.Game(document));

        // 再次升级同一 DOM：ID 已是新 defName 加编号，物品仍按结构识别，没有任何改动。
        SaveUpgradeSession again = SaveXml.Upgrade(document, tables, catalog);
        Assert.Equal("<thing><def>Mugirl_OldCow</def><id>Mugirl_Cow3</id><mugirlMilk>1</mugirlMilk></thing>", SaveXml.Game(document));
        Assert.Equal(0, again.Report.TotalActions);
    }

    [Fact]
    public void HediffCompMatchesOnlyHediffsWhoseDefHasTheComp()
    {
        // 字段顺序与 1.7.4977 的 Hediff.ExposeData 相同：loadID、ageTicks、visible、severity 等在 def 之前，组件标签在最后。
        string comp = Fakes.Name<Fakes.HediffCompLactation>();
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.DeclareLegacyComps("HediffDef", "Mugirl_Lactation", comp);
            m.RenameLabel(OwnerSelector.HediffComp(comp), "ticks", "lactationTicks", "clearer name");
        });
        string result = SaveXml.Upgraded(
            "<healthTracker><hediffSet><hediffs>"
            + "<li Class=\"HediffWithComps\"><loadID>4</loadID><ageTicks>120</ageTicks><visible>True</visible><severity>0.5</severity><def>Mugirl_Lactation</def><ticks>5</ticks></li>"
            + "<li Class=\"HediffWithComps\"><loadID>5</loadID><ageTicks>7</ageTicks><def>Flu</def><ticks>6</ticks></li>"
            + "</hediffs></hediffSet></healthTracker>"
            + "<other><li><loadID>6</loadID><def>Mugirl_Lactation</def><ticks>7</ticks></li></other>",
            tables);

        Assert.Equal(
            "<healthTracker><hediffSet><hediffs>"
            + "<li Class=\"HediffWithComps\"><loadID>4</loadID><ageTicks>120</ageTicks><visible>True</visible><severity>0.5</severity><def>Mugirl_Lactation</def><lactationTicks>5</lactationTicks></li>"
            + "<li Class=\"HediffWithComps\"><loadID>5</loadID><ageTicks>7</ageTicks><def>Flu</def><ticks>6</ticks></li>"
            + "</hediffs></hediffSet></healthTracker>"
            + "<other><li><loadID>6</loadID><def>Mugirl_Lactation</def><ticks>7</ticks></li></other>",
            result);
    }

    [Fact]
    public void HediffsWithLegacyDefsCountAsLegacyEvidence()
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.RenameDef("HediffDef", "Mugirl_OldLactation", "Mugirl_Lactating", "renamed");
            m.MaterializeDefault(OwnerSelector.Path(OwnerSelector.Document("healthTracker/hediffSet"), "hediffs/li"), "rate", "1.5", LegacyEvidence.LegacyDef, "default changed");
        });
        string result = SaveXml.Upgraded(
            "<healthTracker><hediffSet><hediffs>"
            + "<li><loadID>1</loadID><severity>0.5</severity><def>Mugirl_OldLactation</def></li>"
            + "<li><loadID>2</loadID><severity>0.5</severity><def>Mugirl_Lactating</def></li>"
            + "</hediffs></hediffSet></healthTracker>",
            tables);

        Assert.Equal(
            "<healthTracker><hediffSet><hediffs>"
            + "<li><loadID>1</loadID><severity>0.5</severity><def>Mugirl_OldLactation</def><rate>1.5</rate></li>"
            + "<li><loadID>2</loadID><severity>0.5</severity><def>Mugirl_Lactating</def></li>"
            + "</hediffs></hediffSet></healthTracker>",
            result);
    }

    [Fact]
    public void ClassAndComponentSelectorsMatchMappedClassesInTheirLists()
    {
        string component = Fakes.Name<Fakes.NewComponent>();
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.RenameType("Mugirl.OldComponent", typeof(Fakes.NewComponent), "renamed");
            m.RenameLabel(OwnerSelector.GameComponent(typeof(Fakes.NewComponent)), "gameFlag", "flag", "game list");
            m.RenameLabel(OwnerSelector.MapComponent(typeof(Fakes.NewComponent)), "mapFlag", "flag", "map list");
            m.RenameLabel(OwnerSelector.WorldComponent(typeof(Fakes.NewComponent)), "worldFlag", "flag", "world list");
            m.RenameLabel(OwnerSelector.Class(typeof(Fakes.NewComponent)), "anyFlag", "flag", "anywhere");
        });
        string result = SaveXml.Upgraded(
            "<components><li Class=\"Mugirl.OldComponent\"><gameFlag>1</gameFlag><mapFlag>1</mapFlag></li></components>"
            + "<world><components><li Class=\"Mugirl.OldComponent\"><worldFlag>1</worldFlag></li></components></world>"
            + "<maps><li><components><li Class=\"Mugirl.OldComponent\"><mapFlag>1</mapFlag></li></components></li></maps>"
            + "<parts><li Class=\"Mugirl.OldComponent\"><anyFlag>1</anyFlag><gameFlag>1</gameFlag></li></parts>",
            tables);

        Assert.Equal(
            "<components><li Class=\"" + component + "\"><flag>1</flag><mapFlag>1</mapFlag></li></components>"
            + "<world><components><li Class=\"" + component + "\"><flag>1</flag></li></components></world>"
            + "<maps><li><components><li Class=\"" + component + "\"><flag>1</flag></li></components></li></maps>"
            + "<parts><li Class=\"" + component + "\"><flag>1</flag><gameFlag>1</gameFlag></li></parts>",
            result);
    }

    [Fact]
    public void PathAndDocumentSelectorsFollowNestedObjectsWithoutClass()
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.RenameLabel(OwnerSelector.Path(OwnerSelector.Class("Mugirl.Network"), "records/li"), "amt", "amount", "clearer name");
            m.DropLabel(OwnerSelector.Document("maps/li/storyState"), "legacyCounter", "derived from ticks now");
        });
        string result = SaveXml.Upgraded(
            "<li Class=\"Mugirl.Network\"><records><li><amt>3</amt></li></records><other><li><amt>4</amt></li></other></li>"
            + "<maps><li><storyState><legacyCounter>9</legacyCounter><keep>1</keep></storyState></li></maps><storyState><legacyCounter>9</legacyCounter></storyState>",
            tables);

        Assert.Equal(
            "<li Class=\"Mugirl.Network\"><records><li><amount>3</amount></li></records><other><li><amt>4</amt></li></other></li>"
            + "<maps><li><storyState><keep>1</keep></storyState></li></maps><storyState><legacyCounter>9</legacyCounter></storyState>",
            result);
    }

    [Fact]
    public void ConsumedLabelsAreLeftForTheirStep()
    {
        var step = new RecordingStep("Test.Consume", "label:Class(Mugirl.Network)/oldList");
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.ConsumeLabel(OwnerSelector.Class("Mugirl.Network"), "oldList", "Test.Consume", "restructured");
            m.Step(step);
        });
        string result = SaveXml.Upgraded("<li Class=\"Mugirl.Network\"><oldList><li>1</li></oldList></li>", tables);

        Assert.Equal("<li Class=\"Mugirl.Network\"><oldList><li>1</li></oldList></li>", result);
        Assert.Equal(1, step.SeenElements);
    }

    [Fact]
    public void AmbiguousOwnersAndExistingTargetsFailThePlan()
    {
        UpgradeTables ambiguous = SaveXml.Tables(m =>
        {
            m.RenameLabel(OwnerSelector.Class("Mugirl.A"), "x", "y", "one");
            m.RenameLabel(OwnerSelector.Document("li"), "x", "z", "two");
        });
        SaveUpgradeSession first = SaveXml.Upgrade(SaveXml.Save("<li Class=\"Mugirl.A\"><x>1</x></li>"), ambiguous);
        Assert.Equal(UpgradeState.Failed, first.Report.State);
        Assert.Equal(LabelTableStep.StepId, first.Report.FailedStep);

        UpgradeTables rename = SaveXml.Tables(m => m.RenameLabel(OwnerSelector.Class("Mugirl.A"), "x", "y", "one"));
        SaveUpgradeSession second = SaveXml.Upgrade(SaveXml.Save("<li Class=\"Mugirl.A\"><x>1</x><y>2</y></li>"), rename);
        Assert.Equal(UpgradeState.Failed, second.Report.State);
        Assert.Contains("already has <y>", second.Report.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlappingSelectorsCanDelegateOnePhysicalLabelToTheSameStep()
    {
        var step = new RecordingStep("Test.Shared", "label:Class(Mugirl.A)/oldList", "label:Document(li)/oldList");
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.ConsumeLabel(OwnerSelector.Class("Mugirl.A"), "oldList", step.Id, "same physical field");
            m.ConsumeLabel(OwnerSelector.Document("li"), "oldList", step.Id, "same physical field");
            m.Step(step);
        });
        const string body = "<li Class=\"Mugirl.A\"><oldList>4210</oldList></li>";
        Assert.Equal(body, SaveXml.Upgraded(body, tables));
        Assert.Equal(1, step.SeenElements);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SharedLabelsCannotHaveDifferentConsumersOrMixedDispositions(bool secondConsumer)
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.ConsumeLabel(OwnerSelector.Class("Mugirl.A"), "oldList", "Test.First", "first");
            m.Step(new RecordingStep("Test.First", "label:Class(Mugirl.A)/oldList"));
            if (secondConsumer)
            {
                m.ConsumeLabel(OwnerSelector.Document("li"), "oldList", "Test.Second", "second");
                m.Step(new RecordingStep("Test.Second", "label:Document(li)/oldList"));
            }
            else
            {
                m.DropLabel(OwnerSelector.Document("li"), "oldList", "different disposition");
            }
        });
        var document = SaveXml.Save("<li Class=\"Mugirl.A\"><oldList>4210</oldList></li>");
        string before = SaveXml.Game(document);
        SaveUpgradeSession session = SaveXml.Upgrade(document, tables);
        Assert.True(session.Report.Failed);
        Assert.Equal(before, SaveXml.Game(document));
    }

    [Fact]
    public void ModuleStepsSeeLabelsUnderTheirCurrentNames()
    {
        var step = new NameProbeStep("Test.Probe");
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.RenameLabel(OwnerSelector.Class("Mugirl.A"), "before", "after", "renamed");
            m.Step(step);
        });
        SaveXml.Upgraded("<li Class=\"Mugirl.A\"><before>1</before></li>", tables);

        Assert.Equal((0, 1), (step.Before, step.After));
    }

    /// <summary>只记录看到的元素、不做编辑的步骤。</summary>
    private sealed class RecordingStep(string id, params string[] consumes) : DomUpgradeStep(id, "test", consumes)
    {
        internal int SeenElements { get; private set; }

        internal override void Plan(UpgradeContext context, EditPlan plan) => SeenElements = context.Index.ElementsNamed("oldList").Count();
    }

    /// <summary>统计框架改名前后两个标签名下的元素。</summary>
    private sealed class NameProbeStep(string id) : DomUpgradeStep(id, "test")
    {
        internal int Before { get; private set; }

        internal int After { get; private set; }

        internal override IEnumerable<string> IndexNames => ["before", "after"];

        internal override void Plan(UpgradeContext context, EditPlan plan)
        {
            Before = context.Index.ElementsNamed("before").Count(context.IsLive);
            After = context.Index.ElementsNamed("after").Count(context.IsLive);
        }
    }
}
