using System.Linq;
using System.Text.Json;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade;

/// <summary>升级清单 JSON（验证器读取的契约）的结构与内容。</summary>
public sealed class ManifestTests
{
    private static readonly string[] TopLevel =
    [
        "manifestVersion", "schema", "stampLabel", "stampType", "types", "defs", "labels", "enums", "enumSites", "valueContexts", "values", "defaults", "steps",
        "jobPolicies", "lordPolicies", "settings", "removableContainers", "parallelGroups", "legacyComps", "runtimeProtocols", "moogirlEra",
    ];

    [Fact]
    public void ProductionManifestHasTheContractShape()
    {
        using JsonDocument json = JsonDocument.Parse(SaveUpgradeManifest.ToJson());
        JsonElement root = json.RootElement;

        Assert.Equal(TopLevel, root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(2, root.GetProperty("manifestVersion").GetInt32());
        Assert.Equal(SaveSchema.Current, root.GetProperty("schema").GetInt32());
        Assert.Equal("mugirlSaveSchema", root.GetProperty("stampLabel").GetString());
        Assert.Equal("Mugirl.Core.GameComponent_Mugirl", root.GetProperty("stampType").GetString());
        Assert.Equal(typeof(Mugirl.Core.GameComponent_Mugirl).FullName, root.GetProperty("stampType").GetString());
        foreach (string array in TopLevel[4..15].Concat(TopLevel[16..19]))
        {
            Assert.Equal(JsonValueKind.Array, root.GetProperty(array).ValueKind);
        }

        JsonElement settings = root.GetProperty("settings");
        Assert.Equal(new[] { "fileClass", "labels", "schema" }, settings.EnumerateObject().Select(p => p.Name));
        Assert.Equal(SaveSchema.SettingsCurrent, settings.GetProperty("schema").GetInt32());
        Assert.Contains("MooGirl.", root.GetProperty("moogirlEra").GetProperty("typePrefixes").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("MooGirl*", root.GetProperty("moogirlEra").GetProperty("defPatterns").EnumerateArray().Select(e => e.GetString()));
        JsonElement protocol = Assert.Single(root.GetProperty("runtimeProtocols").EnumerateArray());
        Assert.Equal(LegacyThinkTreeKeys.StepId, protocol.GetProperty("id").GetString());
        Assert.Equal(UnresolvedThinkTreeKeys.MarkerLabel, protocol.GetProperty("markerLabel").GetString());
        Assert.True(protocol.GetProperty("optional").GetBoolean());
        Assert.Equal("ScribeGolden/ThinkTreeSources", protocol.GetProperty("goldenFixture").GetString());
    }

    [Fact]
    public void ManifestSerializesEveryRowKind()
    {
        var step = new NoOpStep("Test.Restructure", "label:Class(Mugirl.Network)/oldList", "type:Mugirl.OldThing");
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.RenameType("Mugirl.OldComponent", typeof(Fakes.NewComponent), "renamed");
            m.DropType("Mugirl.OldThing", TypeDropPolicy.RemoveElement, "retired");
            m.RenameDef("ThingDef", "Mugirl_OldGun", "Mugirl_Gun_Rifle", "renamed", legacyRoot: "LegacyThingDef");
            m.DropDef("RecipeDef", "Mugirl_OldRecipe", DefDropPolicy.RemoveBills, "retired");
            m.RenameLabel(OwnerSelector.ThingComp(typeof(Fakes.CompMilk)), "milk", "mugirlMilk", "prefix");
            m.ConsumeLabel(OwnerSelector.Class("Mugirl.Network"), "oldList", "Test.Restructure", "restructured");
            m.RenameEnumMember("Mugirl.Mode", "Old", "New");
            m.DeclareEnumSite(OwnerSelector.Class("Mugirl.Network"), "mode", "Mugirl.Mode");
            m.DeclareValueContext("Tags", OwnerSelector.Any, "questTags");
            m.MapValue("Tags", "OldTag", "NewTag");
            m.MaterializeDefault(OwnerSelector.Class("Mugirl.Network"), "limit", "3", LegacyEvidence.LegacyClass, "default changed");
            m.Step(step);
            m.EndInFlightJobs("Mugirl.JobDriver_Old");
            m.ClaimJobDriver("Mugirl.JobDriver_Claimed", "Test.Restructure");
            m.ResetLordGraph("Mugirl.LordJob_Old");
            m.ClaimLordJob("Mugirl.LordJob_Claimed", "Test.Restructure");
            m.RenameSettingsClass("Mugirl.MugirlSettings", "Mugirl.Core.MugirlSettings", 1);
            m.RenameSetting("enableStructuralCrashEvent", "enableOpeningStoryline", 1);
            m.DropSetting("obsolete", 1);
            m.DeclareRemovableContainer("network/records", "independent records");
            m.DeclareParallelGroup(OwnerSelector.Class("Mugirl.Network"), "keys", "values");
            m.DeclareLegacyComps("ThingDef", "Mugirl_OldGun", "Mugirl.OldComp");
        });

        using JsonDocument json = JsonDocument.Parse(SaveUpgradeManifest.ToJson(tables));
        JsonElement root = json.RootElement;

        JsonElement[] types = [.. root.GetProperty("types").EnumerateArray()];
        Assert.Equal(Fakes.Name<Fakes.NewComponent>(), types[0].GetProperty("new").GetString());
        Assert.Equal("rename", types[0].GetProperty("disposition").GetString());
        Assert.Equal(JsonValueKind.Null, types[0].GetProperty("policy").ValueKind);
        Assert.Equal("drop", types[1].GetProperty("disposition").GetString());
        Assert.Equal("removeElement", types[1].GetProperty("policy").GetString());
        Assert.Equal(SaveXml.ModuleName, types[1].GetProperty("module").GetString());

        // 隐含 Def（Make_、Corpse_ 等）不单独列出。
        JsonElement[] defs = [.. root.GetProperty("defs").EnumerateArray()];
        Assert.Equal(2, defs.Length);
        Assert.Equal("ThingDef", defs[0].GetProperty("root").GetString());
        Assert.Equal("LegacyThingDef", defs[0].GetProperty("legacyRoot").GetString());
        Assert.Equal("removeBills", defs[1].GetProperty("policy").GetString());

        JsonElement[] labels = [.. root.GetProperty("labels").EnumerateArray()];
        Assert.Equal("ThingComp(" + Fakes.Name<Fakes.CompMilk>() + ")", labels[0].GetProperty("owner").GetString());
        Assert.Equal("consumed", labels[1].GetProperty("disposition").GetString());
        Assert.Equal("Test.Restructure", labels[1].GetProperty("step").GetString());

        Assert.Equal("Mugirl.Mode", root.GetProperty("enums")[0].GetProperty("enum").GetString());
        Assert.Equal("Tags", root.GetProperty("values")[0].GetProperty("context").GetString());
        JsonElement context = root.GetProperty("valueContexts").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Tags");
        Assert.Equal("Any", context.GetProperty("owner").GetString());
        Assert.Equal("questTags", context.GetProperty("label").GetString());
        Assert.Equal("text", context.GetProperty("kind").GetString());
        Assert.Equal("items", context.GetProperty("part").GetString());
        Assert.Equal(JsonValueKind.Null, context.GetProperty("defRoot").ValueKind);
        Assert.Equal(SaveXml.ModuleName, context.GetProperty("module").GetString());
        JsonElement enumSite = root.GetProperty("enumSites")[0];
        Assert.Equal("Mugirl.Mode", enumSite.GetProperty("enum").GetString());
        Assert.Equal("Class(Mugirl.Network)", enumSite.GetProperty("owner").GetString());
        Assert.Equal("mode", enumSite.GetProperty("label").GetString());
        Assert.False(enumSite.GetProperty("flags").GetBoolean());
        Assert.Equal("3", root.GetProperty("defaults")[0].GetProperty("oldDefault").GetString());
        Assert.Equal("legacyClass", root.GetProperty("defaults")[0].GetProperty("evidence").GetString());

        JsonElement manifestStep = root.GetProperty("steps")[0];
        Assert.Equal("dom", manifestStep.GetProperty("phase").GetString());
        Assert.Equal(new[] { "label:Class(Mugirl.Network)/oldList", "type:Mugirl.OldThing" }, manifestStep.GetProperty("consumes").EnumerateArray().Select(e => e.GetString()));

        Assert.Equal(new[] { "endInFlight", "claimed:Test.Restructure" }, root.GetProperty("jobPolicies").EnumerateArray().Select(e => e.GetProperty("policy").GetString()));
        Assert.Equal(new[] { "resetGraph", "claimed:Test.Restructure" }, root.GetProperty("lordPolicies").EnumerateArray().Select(e => e.GetProperty("policy").GetString()));

        JsonElement settings = root.GetProperty("settings");
        Assert.Equal("Mugirl.Core.MugirlSettings", settings.GetProperty("fileClass").GetProperty("new").GetString());
        Assert.Equal(2, settings.GetProperty("labels").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("labels")[1].GetProperty("new").ValueKind);
        Assert.Equal(1, settings.GetProperty("schema").GetInt32());

        Assert.Contains(root.GetProperty("removableContainers").EnumerateArray(), c => c.GetProperty("path").GetString() == "network/records");
        Assert.Equal(new[] { "keys", "values" }, root.GetProperty("parallelGroups")[0].GetProperty("labels").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("Mugirl_OldGun", root.GetProperty("legacyComps")[0].GetProperty("def").GetString());
        foreach (string array in new[] { "types", "defs", "labels", "enums", "values", "defaults", "steps", "jobPolicies", "lordPolicies", "legacyComps" })
        {
            Assert.All(root.GetProperty(array).EnumerateArray(), row => Assert.Equal(1, row.GetProperty("schema").GetInt32()));
        }
    }

    [Fact]
    public void ManifestPreservesDistinctValueContextsAndSchemaVersions()
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.DeclareValueContext("First", OwnerSelector.Class("Mugirl.First"), "state");
            m.DeclareValueContext("Second", OwnerSelector.Class("Mugirl.Second"), "state", part: ValuePart.Keys);
            m.DeclareValueContext("DefKeys", OwnerSelector.Class("Mugirl.Second"), "defs", ValueContextKind.DefReference, "ThingDef", ValuePart.Keys);
            m.MapValue("First", "Old", "FirstNew");
            m.ForSchema(2).MapValue("Second", "Old", "SecondNew");
        }, currentSchema: 2);

        using JsonDocument json = JsonDocument.Parse(SaveUpgradeManifest.ToJson(tables));
        JsonElement[] contexts = [.. json.RootElement.GetProperty("valueContexts").EnumerateArray().Where(c => c.GetProperty("module").GetString() == SaveXml.ModuleName)];
        Assert.Equal(new[] { "Class(Mugirl.First)", "Class(Mugirl.Second)", "Class(Mugirl.Second)" }, contexts.Select(c => c.GetProperty("owner").GetString()));
        Assert.Equal("defReference", contexts[2].GetProperty("kind").GetString());
        Assert.Equal("ThingDef", contexts[2].GetProperty("defRoot").GetString());
        Assert.Equal("keys", contexts[1].GetProperty("part").GetString());
        Assert.Equal(new[] { 1, 2 }, json.RootElement.GetProperty("values").EnumerateArray().Select(row => row.GetProperty("schema").GetInt32()));
    }

    private sealed class NoOpStep(string id, params string[] consumes) : DomUpgradeStep(id, "test", consumes)
    {
        internal override void Plan(UpgradeContext context, EditPlan plan)
        {
        }
    }
}
