using System.Collections.Generic;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade;

/// <summary>表格校验：每类登记错误都在构建时列出，读档前就失败。</summary>
public sealed class TableValidationTests
{
    private static readonly Dictionary<string, (string Problem, Action<UpgradeModuleBuilder> Register)> Cases = new(StringComparer.Ordinal)
    {
        ["DuplicateType"] = ("has more than one row", m => { m.RenameType("Mugirl.A", typeof(Fakes.NewComponent), "x"); m.DropType("mugirl.a", TypeDropPolicy.NullOut, "y"); }),
        ["TypeChain"] = ("targets a legacy name", m => { m.RenameType("Mugirl.A", typeof(Fakes.NewComponent), "x"); m.RenameType(Fakes.Name<Fakes.NewComponent>(), typeof(Fakes.CompMilk), "chain"); }),
        ["DefChain"] = ("targets a legacy name", m => { m.RenameDef("ThingDef", "A", "B", "x"); m.RenameDef("ThingDef", "B", "C", "chain"); }),
        ["DefRoot"] = ("must name a DefDatabase root", m => m.RenameDef("Def", "A", "B", "x")),
        ["DuplicateDef"] = ("Def 'ThingDef/A' has more than one row", m => { m.RenameDef("ThingDef", "A", "B", "x"); m.DropDef("ThingDef", "A", DefDropPolicy.RemoveThings, "y"); }),
        ["UnknownConsumer"] = ("consumed by unknown step", m => m.ConsumeLabel(OwnerSelector.Class("Mugirl.A"), "x", "Test.Missing", "r")),
        ["UnlistedConsumption"] = ("must list 'label:Class(Mugirl.A)/x'", m => { m.ConsumeLabel(OwnerSelector.Class("Mugirl.A"), "x", "Test.Step", "r"); m.Step(new EmptyStep("Test.Step")); }),
        ["StepPrefix"] = ("must have the form 'Test.<Name>'", m => m.Step(new EmptyStep("Other.Step"))),
        ["DuplicateStep"] = ("is registered more than once", m => { m.Step(new EmptyStep("Test.Same")); m.Step(new EmptyStep("Test.Same")); }),
        ["NoEvidence"] = ("needs positive legacy evidence", m => m.MaterializeDefault(OwnerSelector.Class("Mugirl.A"), "x", "1", LegacyEvidence.None, "r")),
        ["NoEvidenceLabels"] = ("without evidence labels", m => m.MaterializeDefault(OwnerSelector.Class("Mugirl.A"), "x", "1", LegacyEvidence.LegacyLabel, "r")),
        ["AnyOwnerDefault"] = ("needs a concrete owner selector", m => m.MaterializeDefault(OwnerSelector.Any, "x", "1", LegacyEvidence.LegacyClass, "r")),
        ["UnknownContext"] = ("is not a declared text context", m => m.MapValue("Missing", "a", "b")),
        ["DefContextRoot"] = ("must name a Def root exactly when its kind is Def or DefReference", m => m.DeclareValueContext("C", OwnerSelector.Any, "x", ValueContextKind.Def)),
        ["UnknownClaim"] = ("is claimed by unknown step", m => m.ClaimJobDriver("Mugirl.JobDriver_X", "Test.Missing")),
        ["DuplicateLordPolicy"] = ("LordJob 'Mugirl.LordJob_X' has more than one policy", m => { m.ResetLordGraph("Mugirl.LordJob_X"); m.RemoveLords("Mugirl.LordJob_X"); }),
        ["ShortParallelGroup"] = ("needs at least two labels", m => m.DeclareParallelGroup(OwnerSelector.Class("Mugirl.A"), "only")),
        ["LegacyCompsRoot"] = ("must use root ThingDef or HediffDef", m => m.DeclareLegacyComps("PawnKindDef", "A", "C")),
        ["MatchAllPattern"] = ("would match every name", m => m.MooGirlDefPattern("**")),
        ["SettingsSchema"] = ("targets settings schema 0 outside 1..1", m => m.RenameSetting("a", "b", 0)),
        ["FutureSettingsSchema"] = ("targets settings schema 2 outside 1..1", m => m.RenameSetting("a", "b", 2)),
        ["RowSchema"] = ("targets schema 2 outside 1..1", m => m.ForSchema(2).RenameType("Mugirl.A", typeof(Fakes.NewComponent), "x")),
        ["EnumWithoutSite"] = ("has member renames but no declared site", m => m.RenameEnumMember("Mugirl.Mode", "Old", "New")),
        ["EnumChain"] = ("targets a legacy member", m => { m.DeclareEnumSite(OwnerSelector.Class("Mugirl.A"), "mode", "Mugirl.Mode"); m.RenameEnumMember("Mugirl.Mode", "A", "B"); m.RenameEnumMember("Mugirl.Mode", "B", "C"); }),
        ["LabelChain"] = ("targets a legacy label of the same owner", m => { m.RenameLabel(OwnerSelector.Class("Mugirl.A"), "a", "b", "x"); m.RenameLabel(OwnerSelector.Class("Mugirl.A"), "b", "c", "y"); }),
        ["LabelSwap"] = ("targets a legacy label of the same owner", m => { m.RenameLabel(OwnerSelector.Class("Mugirl.A"), "a", "b", "x"); m.RenameLabel(OwnerSelector.Class("Mugirl.A"), "b", "a", "y"); }),
        ["DroppedLordJobReset"] = ("is a dropped type; its lords are always removed", m => { m.DropType("Mugirl.LordJob_X", TypeDropPolicy.NullOut, "x"); m.ResetLordGraph("Mugirl.LordJob_X"); }),
        ["ImpliedDigit"] = ("'ThingDef/Techprint_Mugirl_Old' -> 'Techprint_Mugirl_New2' ends in a digit", m => m.RenameDef("ResearchProjectDef", "Mugirl_Old", "Mugirl_New2", "x")),
        ["ImpliedCollision"] = ("follows both", m => { m.RenameDef("ThingDef", "Mugirl_Floor", "Mugirl_FloorA", "x"); m.RenameDef("TerrainDef", "Mugirl_Floor", "Mugirl_FloorB", "y"); }),
        ["DefContextRootDef"] = ("must name a DefDatabase root short name, not 'Def'", m => m.DeclareValueContext("C", OwnerSelector.Any, "x", ValueContextKind.DefReference, "Def")),
        ["BadElementName"] = ("'not valid' is not a valid XML element name.", m => m.RenameLabel(OwnerSelector.Class("Mugirl.A"), "a", "not valid", "r")),
        ["GlobalLabel"] = ("labels are never renamed by name alone", m => m.RenameLabel(OwnerSelector.Any, "a", "b", "r")),
        ["GlobalEnumSite"] = ("Enum site 'mode' needs a concrete owner selector", m => m.DeclareEnumSite(OwnerSelector.Any, "mode", "Mugirl.Mode")),
    };

    public static TheoryData<string> CaseNames() => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void InvalidRegistrationsAreRejected(string caseName)
    {
        (string problem, Action<UpgradeModuleBuilder> register) = Cases[caseName];
        UpgradeTableException exception = Assert.Throws<UpgradeTableException>(() => SaveXml.Tables(register));
        Assert.Contains(problem, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleNamesAndOrdersMustBeUnique()
    {
        var builder = new UpgradeTableBuilder();
        builder.Module("Alpha", 1);
        builder.Module("Alpha", 2);
        builder.Module("Beta", 2);
        builder.Module("Framework", 3);

        UpgradeTableException exception = Assert.Throws<UpgradeTableException>(() => builder.Build());
        Assert.Contains("Module 'Alpha' is registered more than once.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("share order 2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Invalid module name 'Framework'.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ModulesAreCompiledInExplicitOrder()
    {
        var first = new EmptyStep("Early.Step");
        var second = new EmptyStep("Late.Step");
        UpgradeTables tables = UpgradeTableBuilder.Build(
        [
            new UpgradeModuleRegistration("Late", 20, new Registration(m => m.Step(second))),
            new UpgradeModuleRegistration("Early", 10, new Registration(m => m.Step(first))),
        ]);

        Assert.Equal(new[] { ("Early", 10), ("Late", 20) }, tables.Modules);
        Assert.Same(first, tables.DomSteps[0]);
        Assert.Same(second, tables.DomSteps[1]);
    }

    [Fact]
    public void ProductionModulesBuild()
    {
        Assert.True(UpgradeTables.TryGetDefault(out UpgradeTables? tables, out Exception? error), error?.ToString());
        Assert.NotNull(tables);
    }

    private sealed class EmptyStep(string id) : DomUpgradeStep(id, "test")
    {
        internal override void Plan(UpgradeContext context, EditPlan plan)
        {
        }
    }

    private sealed class Registration(Action<UpgradeModuleBuilder> register) : ISaveUpgradeModule
    {
        public void Register(UpgradeModuleBuilder module) => register(module);
    }
}
