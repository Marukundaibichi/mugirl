using System.Xml.Linq;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

/// <summary>存档比较的范围（本 Mod 拥有的节点）、误差、读档重置，以及用升级清单解释差异。</summary>
public sealed class SaveDiffTests
{
    private static readonly HashSet<string> ModDefs = new(StringComparer.Ordinal) { "Mugirl", "Mugirl_Collar", "Mugirl_Shocked", "Job_Nuzzle", "Mugirl_Old", "Corpse_Mugirl_Old" };
    private static readonly HashSet<string> ModThingDefs = new(StringComparer.Ordinal) { "Mugirl", "Mugirl_Collar", "Mugirl_Old", "Corpse_Mugirl_Old" };

    private static SaveDiff Diff(params string[] thinkKeys)
        => new(new SaveDiffScope(ModDefs, ModThingDefs, (def, label) => def == "Mugirl" && label.StartsWith("milk", StringComparison.Ordinal),
            new HashSet<string>(thinkKeys, StringComparer.Ordinal)));

    private static string Pawn(string tracker, string milk, string extra = "")
        => $"<things><thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><kindDef>Mugirl_Colony</kindDef>{tracker}<milkFullness>{milk}</milkFullness>{extra}</thing></things>";

    [Fact]
    public void ModPawnsCompareIdentityAndCompLabelsButNotVanillaTrackers()
    {
        XDocument a = Saves.Game(Pawn("<AlienRaces_AlienComp><colorChannels><li>RGBA(0.824, 0.733, 0.412, 1.000)</li></colorChannels></AlienRaces_AlienComp><skills><x>1</x></skills>", "0.5"));
        XDocument b = Saves.Game(Pawn("<AlienRaces_AlienComp><colorChannels><li>RGBA(0.9, 0.9, 0.9, 1.000)</li></colorChannels></AlienRaces_AlienComp><skills><x>2</x></skills>", "0.25"));
        SaveDiff diff = Diff();

        List<SaveDifference> differences = diff.Compare(a, b);

        SaveDifference only = Assert.Single(differences);
        Assert.Equal("thing:Mugirl1/milkFullness", only.Item);
        Assert.Equal("changed", only.Kind);
        Assert.Equal(1, diff.ItemCounts["mod-pawn"]);
        Assert.Equal(1, diff.ItemCounts["comp-label"]);
    }

    [Fact]
    public void NestedModContentInsideAModPawnIsStillCompared()
    {
        string hediffA = "<healthTracker><hediffSet><hediffs><li><def>Mugirl_Shocked</def><severity>0.5</severity></li></hediffs></hediffSet></healthTracker>";
        string hediffB = "<healthTracker><hediffSet><hediffs><li><def>Mugirl_Shocked</def><severity>0.7</severity></li></hediffs></hediffSet></healthTracker>";

        List<SaveDifference> differences = Diff().Compare(Saves.Game(Pawn(hediffA, "0.5")), Saves.Game(Pawn(hediffB, "0.5")));

        Assert.Equal("def:Mugirl1/hediffs/li[Mugirl_Shocked]", Assert.Single(differences).Item);
    }

    [Fact]
    public void AllUnscopedValuesRequireExactEquality()
    {
        Assert.False(SaveXml.SameValue("RGBA(0.824, 0.733, 0.412, 1.000)", "RGBA(0.824, 0.732, 0.412, 1.000)"));
        Assert.False(SaveXml.SameValue("0.5000001", "0.5"));
        Assert.False(SaveXml.SameValue("120003", "120001"));
        Assert.False(SaveXml.SameValue("(10, 0, 20)", "(11, 0, 20)"));
        Assert.False(SaveXml.SameValue("0.5", "0.6"));
    }

    [Fact]
    public void ALogKeyCannotAuthorizeDeletingAnyMatchingJobField()
    {
        string jobA = "<jobs><curJob><def>Job_Nuzzle</def><lastJobGiverKey>12345</lastJobGiverKey></curJob></jobs>";
        string jobB = "<jobs><curJob><def>Job_Nuzzle</def></curJob></jobs>";
        SaveDiff diff = Diff("12345");

        List<SaveDifference> differences = diff.Compare(Saves.Game(Pawn(jobA, "0.5")), Saves.Game(Pawn(jobB, "0.5")));

        Assert.Single(differences);
        Assert.Empty(diff.LoadResets);
        Assert.NotEmpty(Diff().Compare(Saves.Game(Pawn(jobA, "0.5")), Saves.Game(Pawn(jobB, "0.5"))));
    }

    [Fact]
    public void NameListsPreservePositionAndReportRemovedTail()
    {
        string listA = "<filter><allowedDefs><li>Steel</li><li>Mugirl_Old</li><li>Wood</li></allowedDefs></filter>";
        string listB = "<filter><allowedDefs><li>Steel</li><li>Wood</li></allowedDefs></filter>";
        XDocument a = Saves.Game($"<things><thing><def>Mugirl_Collar</def><id>Mugirl_Collar2</id>{listA}</thing></things>");
        XDocument b = Saves.Game($"<things><thing><def>Mugirl_Collar</def><id>Mugirl_Collar2</id>{listB}</thing></things>");

        List<SaveDifference> differences = Diff().Compare(a, b);
        Assert.Equal(2, differences.Count);
        Assert.Contains(differences, d => d.Kind == "changed" && d.Path.EndsWith("li#2", StringComparison.Ordinal));
        Assert.Contains(differences, d => d.Kind == "missing" && d.Path.EndsWith("li#3", StringComparison.Ordinal));
    }

    [Fact]
    public void DroppedThingsAndTheirImpliedDefsAreExplained()
    {
        SaveNameMap map = new K1Builder().Def("ThingDef", "Mugirl_Old", null).Build();
        XDocument a = Saves.Game("<things><thing><def>Mugirl_Old</def><id>Mugirl_Old3</id></thing><thing Class=\"Corpse\"><def>Corpse_Mugirl_Old</def><id>Corpse_Mugirl_Old4</id></thing></things>");
        XDocument b = Saves.Game("<things/>");
        SaveOwnerCatalog catalog = Saves.Catalog(map);
        map.Apply(a, catalog);
        List<SaveDifference> differences = Diff().Compare(a, b);
        var explainer = new SaveDiffExplainer(map, catalog, schemaZero: true, _ => false);

        Assert.Equal(2, differences.Count);
        Assert.All(differences, d => Assert.NotNull(explainer.Explain(d, differences)));
    }

    [Fact]
    public void InFlightModJobsEndedByPolicyAreExplained()
    {
        SaveNameMap map = new K1Builder().Type("Mugirl.JobDriver_Nuzzle", "Mugirl.Features.Social.JobDriver_MugirlNuzzle")
            .JobPolicy("Mugirl.JobDriver_Nuzzle", "endInFlight").Build();
        string jobs = "<jobs><curJob><def>Job_Nuzzle</def><count>1</count></curJob><curDriver Class=\"Mugirl.JobDriver_Nuzzle\"><curToilIndex>2</curToilIndex></curDriver></jobs>";
        XDocument a = Saves.Game(Pawn(jobs, "0.5"));
        XDocument b = Saves.Game(Pawn("<jobs/>", "0.5"));
        SaveOwnerCatalog catalog = Saves.Catalog(map);
        map.Apply(a, catalog);
        List<SaveDifference> differences = Diff().Compare(a, b);

        Assert.Equal(2, differences.Count);
        var legacy = new SaveDiffExplainer(map, catalog, schemaZero: true, _ => false);
        Assert.All(differences, d => Assert.Contains("job policy", legacy.Explain(d, differences), StringComparison.Ordinal));
        var current = new SaveDiffExplainer(map, catalog, schemaZero: false, _ => false);
        Assert.All(differences, d => Assert.Null(current.Explain(d, differences)));
    }

    [Fact]
    public void LordGraphResetDeclarationCannotExplainAnArbitraryReplacement()
    {
        SaveNameMap map = new K1Builder().LordPolicy("Mugirl.LordJob_Support", "resetGraph").Build();
        static string Lord(string toil, string job) =>
            $"<lords><li><lordJob Class=\"Mugirl.LordJob_Support\"><fallback>{job}</fallback></lordJob><lordToilData><li Class=\"Mugirl.LordToilData_X\"><t>{toil}</t></li></lordToilData></li></lords>";
        XDocument a = Saves.Game(Lord("1", "(1, 0, 1)"));
        XDocument b = Saves.Game(Lord("9", "(2, 0, 2)"));
        SaveOwnerCatalog catalog = Saves.Catalog(map);
        List<SaveDifference> differences = Diff().Compare(a, b);
        var explainer = new SaveDiffExplainer(map, catalog, schemaZero: true, _ => false);

        Assert.Equal(2, differences.Count);
        Assert.Null(explainer.Explain(differences.Single(d => d.Path.Contains("lordToilData", StringComparison.Ordinal)), differences));
        Assert.Null(explainer.Explain(differences.Single(d => d.Path.Contains("lordJob", StringComparison.Ordinal)), differences));
    }

    [Fact]
    public void PreservedLordGraphAndJobStateCannotBeLost()
    {
        SaveNameMap map = new K1Builder().JobPolicy("Mugirl.JobDriver_Nuzzle", "preserve")
            .LordPolicy("Mugirl.LordJob_Support", "preserve").Build();
        string jobs = "<jobs><curJob><def>Job_Nuzzle</def><count>1</count></curJob><curDriver Class=\"Mugirl.JobDriver_Nuzzle\"><curToilIndex>2</curToilIndex></curDriver></jobs>";
        string lord = "<lords><li><loadID>12</loadID><lordJob Class=\"Mugirl.LordJob_Support\"/><curLordToilIdx>3</curLordToilIdx><triggerData><li Class=\"Verse.AI.Group.TriggerData\"><ticks>87</ticks></li></triggerData></li></lords>";
        XDocument before = Saves.Game(Pawn(jobs, "0.5") + lord);
        XDocument after = Saves.Game(Pawn("<jobs/>", "0.5") + lord.Replace("<curLordToilIdx>3</curLordToilIdx>", string.Empty, StringComparison.Ordinal)
            .Replace("<ticks>87</ticks>", "<ticks>0</ticks>", StringComparison.Ordinal));
        List<SaveDifference> differences = Diff().Compare(before, after);
        var explainer = new SaveDiffExplainer(map, Saves.Catalog(map), schemaZero: true, _ => false);
        Assert.Equal(4, differences.Count);
        Assert.All(differences, d => Assert.Null(explainer.Explain(d, differences)));
    }

    [Fact]
    public void DefaultMaterializationDoesNotExplainLossOfAnExplicitValue()
    {
        SaveNameMap map = new K1Builder()
            .Default("Class(Mugirl.CorporateNetwork)", "corporateSupportCallsUsed", "-1")
            .Enum("Mugirl.OrderState", "Waiting", "Pending")
            .EnumSite("Mugirl.OrderState", "Class(Mugirl.CorporateNetwork)", "state")
            .Build();
        XDocument a = Saves.Game("<components><li Class=\"Mugirl.CorporateNetwork\"><corporateSupportCallsUsed>0</corporateSupportCallsUsed><state>Waiting</state></li></components>");
        XDocument b = Saves.Game("<components><li Class=\"Mugirl.CorporateNetwork\"><state>Pending</state></li></components>");
        SaveOwnerCatalog catalog = Saves.Catalog(map);
        List<SaveDifference> differences = Diff().Compare(a, b);
        var explainer = new SaveDiffExplainer(map, catalog, schemaZero: true, _ => false);

        Assert.Equal(2, differences.Count);
        Assert.Null(explainer.Explain(differences.Single(d => d.Kind == "missing"), differences));
        Assert.NotNull(explainer.Explain(differences.Single(d => d.Kind == "changed"), differences));
    }

    [Fact]
    public void DroppedLordJobExplainsItsEntireRemovedLord()
    {
        SaveNameMap map = new K1Builder().Type("Mugirl.LordJob_Retired", null).Build();
        XDocument before = Saves.Game("<lords><li><loadID>12</loadID><lordJob Class=\"Mugirl.LordJob_Retired\"/><curLordToilIdx>3</curLordToilIdx></li></lords>");
        XDocument after = Saves.Game("<lords/>");
        List<SaveDifference> differences = Diff().Compare(before, after);
        Assert.Equal("lord:12", Assert.Single(differences).Item);
        Assert.NotNull(new SaveDiffExplainer(map, Saves.Catalog(map), schemaZero: true, _ => false).Explain(differences[0], differences));
    }

    [Fact]
    public void UnexplainedLossesStayUnexplained()
    {
        SaveNameMap map = new K1Builder().Build();
        XDocument a = Saves.Game("<things><thing><def>Mugirl_Collar</def><id>Mugirl_Collar2</id><tickCounter>120034</tickCounter></thing></things>");
        XDocument b = Saves.Game("<things><thing><def>Mugirl_Collar</def><id>Mugirl_Collar2</id></thing></things>");
        List<SaveDifference> differences = Diff().Compare(a, b);

        Assert.Null(new SaveDiffExplainer(map, Saves.Catalog(map), schemaZero: true, _ => false).Explain(Assert.Single(differences), differences));
    }

    [Fact]
    public void DuplicateColorableNormalizationRetainsRawDifferencesAndRequiresRuntimeEvidence()
    {
        const string color = "RGBA(0.500, 0.600, 0.700, 1.000)";
        static XDocument Save(string content) => Saves.Game("<things><thing><def>Mugirl_Collar</def><id>Mugirl_Collar2</id>" + content + "</thing></things>");
        string first = $"<color>{color}</color><colorActive>True</colorActive>";
        XDocument before = Save(first);
        XDocument after = Save(first + first);
        var diff = new SaveDiff(new SaveDiffScope(ModDefs, ModThingDefs, (_, _) => false, new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<string, int>(StringComparer.Ordinal) { ["Mugirl_Collar"] = 2 }));
        List<SaveDifference> raw = diff.Compare(before, after);
        Assert.Equal(2, raw.Count);
        Assert.All(raw, d => Assert.NotNull(diff.ExplainColorableNormalization(d)));
        SaveDiff unknown = Diff();
        Assert.All(unknown.Compare(before, after), d => Assert.Null(unknown.ExplainColorableNormalization(d)));
    }

    [Theory]
    [InlineData("<color>A</color><colorActive>True</colorActive>", "<color>B</color><colorActive>True</colorActive><color>B</color><colorActive>True</colorActive>")]
    [InlineData("<color>A</color><colorActive>True</colorActive>", "<color>A</color><colorActive>True</colorActive><color>B</color><colorActive>True</colorActive>")]
    [InlineData("<color>A</color><colorActive>True</colorActive>", "<color>A</color><colorActive>True</colorActive><color>A</color><colorActive>False</colorActive>")]
    [InlineData("<quality>1</quality>", "<quality>1</quality><quality>1</quality>")]
    public void ColorableNormalizationNeverHidesPrimaryChangesUnequalDuplicatesOrOtherFields(string from, string to)
    {
        static XDocument Save(string content) => Saves.Game("<things><thing><def>Mugirl_Collar</def><id>Mugirl_Collar2</id>" + content + "</thing></things>");
        var diff = new SaveDiff(new SaveDiffScope(ModDefs, ModThingDefs, (_, _) => false, new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<string, int>(StringComparer.Ordinal) { ["Mugirl_Collar"] = 2 }));
        List<SaveDifference> raw = diff.Compare(Save(from), Save(to));
        Assert.NotEmpty(raw);
        Assert.All(raw, d => Assert.Null(diff.ExplainColorableNormalization(d)));
    }
}
