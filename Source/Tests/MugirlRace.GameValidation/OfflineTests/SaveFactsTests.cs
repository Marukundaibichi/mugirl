using System.Xml.Linq;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

/// <summary>事实的读取（束缚服装的默认值、空集合）与按升级清单翻译。</summary>
public sealed class SaveFactsTests
{
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Restraints = new(StringComparer.Ordinal)
    {
        ["Mugirl_Collar"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["isLocked"] = "True", ["lockCount"] = "2", ["timer"] = "0" },
    };

    private static SaveFactQueries Queries() => SaveFactQueries.Legacy(["Mugirl", "Mugirl_Collar", "Mugirl_Shocked"], ["Mugirl", "Mugirl_Collar"], Restraints);

    [Fact]
    public void RestraintsUseTheBuildDefaultsForUnwrittenLabels()
    {
        XDocument save = Saves.Game("<things>"
            + "<thing Class=\"Mugirl.AdvancedSlaveApparel\"><def>Mugirl_Collar</def><id>Mugirl_Collar1</id></thing>"
            + "<thing Class=\"Mugirl.AdvancedSlaveApparel\"><def>Mugirl_Collar</def><id>Mugirl_Collar2</id><isLocked>False</isLocked><lockCount>0</lockCount></thing>"
            + "<thing Class=\"Mugirl.AdvancedSlaveApparel\"><def>Mugirl_Collar</def><id>Mugirl_Collar3</id><lockCount>3</lockCount><ticks>1234</ticks></thing>"
            + "</things>");

        SortedDictionary<string, string> facts = SaveFacts.Read(save, Queries()).Values;

        Assert.Equal("True", facts["restraint/Mugirl_Collar1/isLocked"]);
        Assert.Equal("2", facts["restraint/Mugirl_Collar1/lockCount"]);
        Assert.Equal("False", facts["restraint/Mugirl_Collar2/isLocked"]);
        Assert.Equal("1234", facts["restraint/Mugirl_Collar3/timer"]);
        Assert.Equal("3", facts["restraints/count"]);
        Assert.Equal("2", facts["restraints/locked"]);
        Assert.Equal("1", facts["restraints/unlocked"]);
        Assert.Equal("2", facts["restraints/customLockCount"]);
        Assert.Equal("1", facts["restraints/runningTimers"]);
    }

    [Fact]
    public void EmptyCollectionSummaryDoesNotProveMissingFieldEquivalence()
    {
        XDocument empty = Saves.Game("<components><li Class=\"Mugirl.Story\"><flags /><count>1</count></li></components>");
        XDocument missing = Saves.Game("<components><li Class=\"Mugirl.Story\"><count>1</count></li></components>");

        Assert.Equal(SaveFacts.Read(empty, Queries()).Values, SaveFacts.Read(missing, Queries()).Values);
        var diff = new SaveDiff(new SaveDiffScope(new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), (_, _) => false,
            new HashSet<string>(StringComparer.Ordinal)));
        Assert.Equal("missing", Assert.Single(diff.Compare(empty, missing)).Kind);
    }

    [Fact]
    public void TranslationFollowsRootsOwnersAndDrops()
    {
        SaveNameMap map = new K1Builder()
            .Type("Mugirl.Story", "Mugirl.Features.Incidents.GameComponent_Story")
            .Type("Mugirl.HediffComp_Old", "Mugirl.Features.Restraints.HediffComp_Shock")
            .Label("GameComponent(Mugirl.Features.Incidents.GameComponent_Story)", "crashStarted", "openingStarted")
            .Label("GameComponent(Mugirl.Features.Incidents.GameComponent_Story)", "obsolete", null, "drop")
            .Label("HediffComp(Mugirl.Features.Restraints.HediffComp_Shock)", "ticksLeft", "remainingTicks")
            .Def("ThingDef", "Mugirl_Collar", "Mugirl_ShockCollar")
            .Def("HediffDef", "Mugirl_Shocked", "Mugirl_Shock")
            .Def("ThingDef", "Mugirl_Gone", null)
            .Build();
        LegacyCorpus corpus = Saves.Corpus([("HediffDef", "Mugirl_Shocked", "Mugirl.HediffComp_Old")], []);
        var legacy = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["component/Mugirl.Story/crashStarted"] = "True",
            ["component/Mugirl.Story/obsolete"] = "5",
            ["pawn/Mugirl1/hediff/Mugirl_Shocked"] = "1",
            ["pawn/Mugirl1/hediff/Mugirl_Shocked/@class"] = "HediffWithComps",
            ["pawn/Mugirl1/hediff/Mugirl_Shocked/ticksLeft"] = "300",
            ["restraint/Mugirl_Collar7/def"] = "Mugirl_Collar",
            ["thing/Mugirl_Collar"] = "2",
            ["thing/Mugirl_Gone"] = "1",
        };

        SortedDictionary<string, string> translated = SaveFacts.Translate(legacy, map, Saves.Catalog(map, corpus), out List<string> dropped);

        Assert.Equal("True", translated["component/Mugirl.Features.Incidents.GameComponent_Story/openingStarted"]);
        Assert.Equal("300", translated["pawn/Mugirl1/hediff/Mugirl_Shock/remainingTicks"]);
        Assert.Equal("Mugirl_ShockCollar", translated["restraint/Mugirl_ShockCollar7/def"]);
        Assert.Equal("2", translated["thing/Mugirl_ShockCollar"]);
        Assert.Equal(new[] { "component/Mugirl.Story/obsolete", "thing/Mugirl_Gone" }, dropped.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void QueriesFollowRenamedThingLabels()
    {
        SaveNameMap map = new K1Builder()
            .Label("ThingComp(Mugirl.Features.Milk.CompMilkProduction)", "milkFullness", "mugirlMilkFullness")
            .Label("Class(Mugirl.Features.Restraints.Restraint)", "lockCount", "locksLeft")
            .Def("ThingDef", "Mugirl_Collar", "Mugirl_ShockCollar")
            .Build();

        SaveFactQueries translated = Queries().Translate(map);

        Assert.Equal("mugirlMilkFullness", translated.MilkFullnessLabel);
        Assert.Equal("locksLeft", translated.RestraintLabels[SaveFactQueries.LockCountKey]);
        Assert.True(translated.RestraintDefaults.ContainsKey("Mugirl_ShockCollar"));
        Assert.Contains("Mugirl_ShockCollar", translated.ModThingDefs);
    }
}
