using System.Xml.Linq;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

/// <summary>升级清单（K1）的名字空间：Def 按（根类型, 旧名）、标签按 owner、隐含 Def、ThingID、旧名单与翻译。</summary>
public sealed class SaveNameMapTests
{
    [Fact]
    public void ResidualDefTokensUseTheirActualDatabaseRootEvenWithoutGlobalDefInventory()
    {
        SaveNameMap map = new K1Builder().Def("ThingCategoryDef", "Mugirl_Slave", "Mugirl_Restraints")
            .Def("ThingDef", "OldThing", "NewThing").Def("HediffDef", "Gone", null).Build();
        XElement pawnKind = XElement.Parse("<kindDef>Mugirl_Slave</kindDef>");
        Assert.Equal("PawnKindDef", SaveDefRoots.RootOf(pawnKind));
        Assert.False(map.IsLegacyDef(SaveDefRoots.RootOf(pawnKind)!, pawnKind.Value));
        Assert.True(map.IsLegacyDef("ThingCategoryDef", "Mugirl_Slave"));
        Assert.False(map.IsLegacyDef("ThingDef", "Mugirl_Slave"));
        Assert.True(map.IsLegacyDef("ThingDef", "OldThing"));
        Assert.False(map.IsLegacyDef("PawnKindDef", "OldThing"));
        Assert.True(map.IsLegacyDef("HediffDef", "Gone"));
        Assert.False(map.IsLegacyDef("ThingDef", "Gone"));
        Assert.False(map.IsLegacyDef("ThingCategoryDef", "Mugirl_Restraints"));
    }

    [Fact]
    public void DefsAreKeyedByDatabaseRoot()
    {
        // 真实的冲突（spec/final/R/milk.json）：同一个旧名在两种根类型中改成不同的新名。
        SaveNameMap map = new K1Builder()
            .Def("HediffDef", "Consumed_MugirlMilk", "Mugirl_MilkVigor")
            .Def("ThoughtDef", "Consumed_MugirlMilk", "Mugirl_ConsumedMilk")
            .Build();

        Assert.Equal("Mugirl_MilkVigor", map.Def("HediffDef", "Consumed_MugirlMilk"));
        Assert.Equal("Mugirl_ConsumedMilk", map.Def("ThoughtDef", "Consumed_MugirlMilk"));
        Assert.Equal("Consumed_MugirlMilk", map.Def("ThingDef", "Consumed_MugirlMilk"));
        Assert.Equal("Consumed_MugirlMilk", map.DefAnyRoot("Consumed_MugirlMilk", out bool ambiguous));
        Assert.True(ambiguous);
    }

    [Fact]
    public void ApplyTranslatesDefReferencesByTheirPosition()
    {
        SaveNameMap map = new K1Builder()
            .Def("HediffDef", "Consumed_MugirlMilk", "Mugirl_MilkVigor")
            .Def("ThoughtDef", "Consumed_MugirlMilk", "Mugirl_ConsumedMilk")
            .Def("ThingCategoryDef", "Mugirl_Slave", "Mugirl_SlaveCategory")
            .Build();
        XDocument save = Saves.Game(
            "<things><thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><kindDef>Mugirl_Slave</kindDef>"
            + "<healthTracker><hediffSet><hediffs><li><def>Consumed_MugirlMilk</def></li></hediffs></hediffSet></healthTracker>"
            + "<needs><mood><thoughts><memories><li><def>Consumed_MugirlMilk</def></li></memories></thoughts></mood></needs></thing></things>");

        SaveApplyReport report = map.Apply(save, Saves.Catalog(map));

        Assert.Equal("Mugirl_Slave", Saves.Element(save, "kindDef").Value);
        Assert.Equal("Mugirl_MilkVigor", save.Descendants("hediffs").Single().Element("li")!.Element("def")!.Value);
        Assert.Equal("Mugirl_ConsumedMilk", save.Descendants("memories").Single().Element("li")!.Element("def")!.Value);
        Assert.Equal(2, report.Defs);
    }

    [Fact]
    public void LegacyOnlyDefsExcludeNamesAnyRootStillUses()
    {
        SaveNameMap map = new K1Builder()
            .Def("ThingCategoryDef", "Mugirl_Slave", "Mugirl_SlaveCategory")
            .Def("ThingDef", "Mugirl_OldShackles", "Mugirl_Shackles")
            .Build();

        HashSet<string> legacyOnly = map.LegacyOnlyDefs(new HashSet<string>(StringComparer.Ordinal) { "Mugirl_Slave", "Mugirl_Shackles" });

        Assert.DoesNotContain("Mugirl_Slave", legacyOnly);
        Assert.Contains("Mugirl_OldShackles", legacyOnly);
    }

    [Fact]
    public void ImpliedDefsFollowTheirSource()
    {
        SaveNameMap map = new K1Builder()
            .Def("ThingDef", "Mugirl_MechanoidBulldog", null)
            .Def("ThingDef", "Mugirl_OldBench", "Mugirl_Bench")
            .Build();

        Assert.True(map.IsDroppedDef("ThingDef", "Corpse_Mugirl_MechanoidBulldog"));
        Assert.True(map.IsDroppedDef("RecipeDef", "Make_Mugirl_MechanoidBulldog"));
        Assert.Equal("Blueprint_Mugirl_Bench", map.Def("ThingDef", "Blueprint_Mugirl_OldBench"));
        Assert.Equal("Make_Mugirl_BenchBulk", map.Def("RecipeDef", "Make_Mugirl_OldBenchBulk"));
    }

    [Fact]
    public void ThingIdsUseTheLongestDefNameThatEndsInDigits()
    {
        SaveNameMap map = new K1Builder().Def("ThingDef", "Mugirl_Gun2", "Mugirl_Rifle").Def("ThingDef", "Mugirl_Gun", "Mugirl_Pistol").Build();

        Assert.Equal("Thing_Mugirl_Rifle345", map.TranslateId("Thing_Mugirl_Gun2345"));
        Assert.Equal("Mugirl_Rifle7", map.TranslateId("Mugirl_Gun27"));
        Assert.Equal("Human12", map.TranslateId("Human12"));
    }

    [Fact]
    public void LabelFateIsDecidedByTheOwnerSelector()
    {
        SaveNameMap map = new K1Builder()
            .Type("Mugirl.CompMooMilkable", "Mugirl.Features.Milk.CompMilkProduction")
            .Label("ThingComp(Mugirl.Features.Milk.CompMilkProduction)", "milkFullness", "mugirlMilkFullness")
            .Label("Class(Mugirl.SlaveApparel)", "color", null, "drop")
            .Build();
        LegacyCorpus corpus = Saves.Corpus([("ThingDef", "Mugirl", "Mugirl.CompMooMilkable")], [("Mugirl.CompMooMilkable", "milkFullness")]);
        SaveOwnerCatalog catalog = Saves.Catalog(map, corpus);
        XDocument save = Saves.Game(
            "<things><thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><milkFullness>0.5</milkFullness></thing>"
            + "<thing Class=\"Pawn\"><def>Human</def><id>Human2</id><milkFullness>0.7</milkFullness></thing>"
            + "<thing Class=\"Apparel\"><def>Hat</def><id>Hat3</id><color>RGBA(1, 1, 1, 1)</color></thing>"
            + "<thing Class=\"Mugirl.SlaveApparel\"><def>Mugirl_Collar</def><id>Mugirl_Collar4</id><color>RGBA(1, 1, 1, 1)</color></thing></things>");
        XElement[] things = [.. save.Descendants("thing")];

        Assert.Equal(SaveLabelFateKind.Renamed, map.LabelFate(things[0], "milkFullness", catalog).Kind);
        Assert.Equal(SaveLabelFateKind.Unchanged, map.LabelFate(things[1], "milkFullness", catalog).Kind);
        Assert.Equal(SaveLabelFateKind.Unchanged, map.LabelFate(things[2], "color", catalog).Kind);
        Assert.Equal(SaveLabelFateKind.Dropped, map.LabelFate(things[3], "color", catalog).Kind);

        SaveApplyReport report = map.Apply(save, catalog);
        Assert.Equal(1, report.LabelsRenamed);
        Assert.Single(report.LabelsRemoved);
        Assert.NotNull(things[0].Element("mugirlMilkFullness"));
        Assert.NotNull(things[1].Element("milkFullness"));
        Assert.NotNull(things[2].Element("color"));
        Assert.Null(things[3].Element("color"));
    }

    [Fact]
    public void StepConsumptionMakesALabelGone()
    {
        SaveNameMap map = new K1Builder()
            .Step("Milk.MoveTimers", "label:ThingComp(Mugirl.CompMilk)/milkFullness_lastResourceUpdateTick", "def:ThingDef/Mugirl_Old", "value:signals/OldSignal", "enum:Mugirl.Mode.Old")
            .ValueContext("signals", "Class(Mugirl.Signal)", "signal")
            .EnumSite("Mugirl.Mode", "Class(Mugirl.Signal)", "mode")
            .Build();
        LegacyCorpus corpus = Saves.Corpus([("ThingDef", "Mugirl", "Mugirl.CompMilk")], [("Mugirl.CompMilk", "milkFullness_lastResourceUpdateTick")]);
        XDocument save = Saves.Game("<things><thing><def>Mugirl</def><id>Mugirl1</id><milkFullness_lastResourceUpdateTick>5</milkFullness_lastResourceUpdateTick></thing></things>");

        SaveLabelFate fate = map.LabelFate(Saves.Element(save, "thing"), "milkFullness_lastResourceUpdateTick", Saves.Catalog(map, corpus));

        Assert.Equal(SaveLabelFateKind.Consumed, fate.Kind);
        Assert.Equal("Milk.MoveTimers", fate.Step);
        Assert.True(map.ConsumesDef("ThingDef", "Mugirl_Old"));
        XDocument values = Saves.Game("<components><li Class=\"Mugirl.Signal\"><signal>OldSignal</signal><mode>Old</mode><unrelated>OldSignal</unrelated></li></components>");
        Assert.True(map.ConsumesValue("OldSignal", Saves.Element(values, "signal"), Saves.Catalog(map)));
        Assert.True(map.ConsumesValue("Old", Saves.Element(values, "mode"), Saves.Catalog(map)));
        Assert.False(map.ConsumesValue("OldSignal", Saves.Element(values, "unrelated"), Saves.Catalog(map)));
    }

    [Fact]
    public void ConflictingRowsAreAmbiguous()
    {
        SaveNameMap map = new K1Builder()
            .Label("Class(Mugirl.A)", "ticks", "timerTicks")
            .Label("Any", "ticks", null, "drop")
            .Build();
        XDocument save = Saves.Game("<components><li Class=\"Mugirl.A\"><ticks>5</ticks></li></components>");

        Assert.Equal(SaveLabelFateKind.Ambiguous, map.LabelFate(save.Descendants("li").First(e => SaveXml.ClassOf(e) is not null), "ticks", Saves.Catalog(map)).Kind);
    }

    [Fact]
    public void JobPoliciesAcceptLegacyAndMappedNames()
    {
        SaveNameMap map = new K1Builder()
            .Type("Mugirl.JobDriver_Nuzzle", "Mugirl.Features.Social.JobDriver_MugirlNuzzle")
            .Type("Mugirl.JobDriver_FollowRoper", "Mugirl.Features.Roping.JobDriver_FollowRoper")
            .JobPolicy("Mugirl.JobDriver_Nuzzle", "endInFlight")
            .JobPolicy("Mugirl.JobDriver_FollowRoper", "claimed:Roping.Followers")
            .LordPolicy("Mugirl.LordJob_CorporateSupport", "resetGraph")
            .Build();

        Assert.True(map.EndsJob("Mugirl.Features.Social.JobDriver_MugirlNuzzle", schemaZero: true));
        Assert.False(map.EndsJob("Mugirl.Features.Roping.JobDriver_FollowRoper", schemaZero: true));
        Assert.False(map.EndsJob("Mugirl.Features.Social.JobDriver_MugirlNuzzle", schemaZero: false));
        Assert.False(map.EndsJob("RimWorld.JobDriver_Wait", schemaZero: true));
        Assert.Equal("resetGraph", map.LordPolicy("Mugirl.LordJob_CorporateSupport"));
    }

    [Fact]
    public void OldSchemaAndPreserveNeverImplyEndingAnUnlistedJob()
    {
        SaveNameMap map = new K1Builder().JobPolicy("Mugirl.JobDriver_Preserved", "preserve")
            .LordPolicy("Mugirl.LordJob_Preserved", "preserve").Build();
        Assert.False(map.EndsJob("Mugirl.JobDriver_Preserved", schemaZero: true));
        Assert.False(map.EndsJob("Mugirl.JobDriver_Unlisted", schemaZero: true));
        Assert.Equal("preserve", map.LordPolicy("Mugirl.LordJob_Preserved"));
    }

    [Fact]
    public void ValueChangesRequireTheirDeclaredContext()
    {
        SaveNameMap map = new K1Builder()
            .Enum("Mugirl.OrderState", "Waiting", "Pending")
            .Value("Corporate.RecordKeys", "Mugirl.CorporateIntro.Title", "Corporate.Intro")
            .ValueContext("Corporate.RecordKeys", "Class(Mugirl.Record)", "title")
            .EnumSite("Mugirl.OrderState", "Class(Mugirl.Record)", "state")
            .Def("JobDef", "Job_Nuzzle", "Mugirl_Nuzzle")
            .Build();

        XDocument save = Saves.Game("<components><li Class=\"Mugirl.Record\"><state>Waiting</state><title>Mugirl.CorporateIntro.Title</title><other>Waiting</other></li></components>");
        SaveOwnerCatalog catalog = Saves.Catalog(map);
        Assert.True(map.ExplainsValueChange("Waiting", "Pending", Saves.Element(save, "state"), catalog));
        Assert.True(map.ExplainsValueChange("Mugirl.CorporateIntro.Title", "Corporate.Intro", Saves.Element(save, "title"), catalog));
        Assert.False(map.ExplainsValueChange("Waiting", "Pending", Saves.Element(save, "other"), catalog));
        Assert.False(map.ExplainsValueChange("Waiting", "Done", Saves.Element(save, "state"), catalog));
        Assert.Contains("Waiting", map.LegacyOnlyValues());
    }

    [Fact]
    public void SameTextInDifferentContextsAndDictionarySidesDoesNotCollide()
    {
        SaveNameMap map = new K1Builder()
            .ValueContext("keys", "Class(Mugirl.Record)", "data", "keys")
            .ValueContext("values", "Class(Mugirl.Record)", "data", "values")
            .Value("keys", "Old", "NewKey").Value("values", "Old", "NewValue")
            .Build();
        XDocument save = Saves.Game("<components><li Class=\"Mugirl.Record\"><data><keys><li>Old</li></keys><values><li>Old</li></values></data><other>Old</other></li><li Class=\"Other.Record\"><data><keys><li>Old</li></keys></data></li></components>");
        SaveOwnerCatalog catalog = Saves.Catalog(map);
        XElement key = save.Descendants("keys").First().Element("li")!;
        XElement value = save.Descendants("values").Single().Element("li")!;
        Assert.True(map.IsLegacyValue(key, catalog));
        Assert.False(map.ExplainsValueChange("Old", "NewValue", key, catalog));
        Assert.False(map.ExplainsValueChange("Old", "NewKey", value, catalog));
        map.Apply(save, catalog);
        Assert.Equal("NewKey", key.Value);
        Assert.Equal("NewValue", value.Value);
        Assert.Equal("Old", Saves.Element(save, "other").Value);
        Assert.Equal("Old", save.Descendants("keys").Last().Element("li")!.Value);
    }

    [Fact]
    public void EnumNamesAndFlagsAreScopedToTheirSites()
    {
        SaveNameMap map = new K1Builder().Enum("Mugirl.A", "Old", "NewA").Enum("Mugirl.B", "Old", "NewB")
            .EnumSite("Mugirl.A", "Class(Mugirl.Record)", "a", flags: true)
            .EnumSite("Mugirl.B", "Class(Mugirl.Record)", "b").Build();
        XDocument save = Saves.Game("<components><li Class=\"Mugirl.Record\"><a>Old, Other</a><b>Old</b><c>Old</c></li></components>");
        map.Apply(save, Saves.Catalog(map));
        Assert.Equal("NewA, Other", Saves.Element(save, "a").Value);
        Assert.Equal("NewB", Saves.Element(save, "b").Value);
        Assert.Equal("Old", Saves.Element(save, "c").Value);
    }
}
