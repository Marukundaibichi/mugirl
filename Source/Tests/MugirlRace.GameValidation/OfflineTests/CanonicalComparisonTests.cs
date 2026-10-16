using System.Text.Json.Nodes;
using System.Reflection;
using System.Xml.Linq;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

public sealed class CanonicalComparisonTests
{
    private static SaveDiff Diff() => new(new SaveDiffScope(new HashSet<string> { "Mugirl_Item", "Mugirl" },
        new HashSet<string> { "Mugirl_Item", "Mugirl" }, (_, label) => label is "mugirlMountState" or "mugirlWheelSlotRecords", new HashSet<string>()));

    private static XDocument Component(string fields) => Saves.Game("<components><li Class=\"Mugirl.Corporate\">" + fields + "</li></components>");

    [Theory]
    [InlineData("315.0001", "315.5")]
    [InlineData("0.0000001", "0.0000002")]
    [InlineData(" null ", "null")]
    [InlineData("Thing_Mugirl1", "Thing_Mugirl2")]
    public void CanonicalNestedValuesCannotDisappearOrChange(string before, string after)
    {
        XDocument canonical = Component("<finance><loan>" + before + "</loan></finance>");
        Assert.NotEmpty(Diff().Compare(canonical, Component("<finance><loan>" + after + "</loan></finance>")));
        Assert.NotEmpty(Diff().Compare(canonical, Component("<finance/>")));
        Assert.NotEmpty(Diff().Compare(Component("<finance/>"), canonical));
    }

    [Theory]
    [InlineData("<li>Steel</li><li>Wood</li>", "<li>Wood</li><li>Steel</li>")]
    [InlineData("<li><def>A</def></li><li><def>B</def></li>", "<li><def>B</def></li><li><def>A</def></li>")]
    public void OrderedListsCannotBeReordered(string a, string b)
        => Assert.NotEmpty(Diff().Compare(Component("<slots>" + a + "</slots>"), Component("<slots>" + b + "</slots>")));

    [Fact]
    public void NestedThingMovingBetweenHoldersIsDetected()
    {
        const string thing = "<li><def>Mugirl_Item</def><id>Mugirl_Item1</id></li>";
        Assert.NotEmpty(Diff().Compare(Component("<vault>" + thing + "</vault><reserve/>"), Component("<vault/><reserve>" + thing + "</reserve>")));
    }

    [Fact]
    public void ModOwnedDeepPawnIncludesTextAndVanillaJobState()
    {
        XDocument before = Component("<riders><li Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><kindDef>MugirlColonist</kindDef>"
            + "<name><nick> null </nick></name><jobs><curJob><def>Wait</def><count>17</count></curJob></jobs></li></riders>");
        var after = new XDocument(before);
        after.Descendants("nick").Single().Value = "null";
        after.Descendants("count").Single().Value = "18";
        Assert.Equal(2, Diff().Compare(before, after).Count);
    }

    [Fact]
    public void NewCompWrapperIsComparedEvenWithoutLegacyLabels()
    {
        var catalog = new SaveOwnerCatalog(SaveNameMap.Empty, LegacyCorpus.Empty, [("ThingDef", "Mugirl", "Mugirl.NewComp")],
            new Dictionary<string, HashSet<string>> { ["Mugirl.NewComp"] = ["mugirlMountState"] });
        var diff = new SaveDiff(new SaveDiffScope(new HashSet<string> { "Mugirl" }, new HashSet<string> { "Mugirl" }, catalog.IsModCompLabel, new HashSet<string>()));
        XDocument a = Saves.Game("<things><thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><kindDef>MugirlColonist</kindDef><mugirlMountState><ticks>123</ticks></mugirlMountState></thing></things>");
        var b = new XDocument(a);
        b.Descendants("ticks").Single().Value = "124";
        Assert.Single(diff.Compare(a, b));
        b.Descendants("mugirlMountState").Single().Remove();
        Assert.Single(diff.Compare(a, b));
    }

    [Fact]
    public void AttributeChangesAreNotHidden()
        => Assert.NotEmpty(Diff().Compare(Component("<state ref=\"a\"/>"), Component("<state ref=\"b\"/>")));

    [Fact]
    public void ConsumedLabelRemainsEvidenceWithoutExecutingItsStep()
    {
        SaveNameMap map = new K1Builder().Label("Class(Mugirl.Corporate)", "loan", null, "consumed", "Corporate.Finance").Build();
        XDocument input = Component("<loan>315.0001</loan>");
        map.Apply(input, new SaveOwnerCatalog(map, LegacyCorpus.Empty, []));
        Assert.Equal("315.0001", input.Descendants("loan").Single().Value);
    }

    [Fact]
    public void PreciseReferenceTraceChangesOnlyTheReadLeaf()
    {
        XDocument document = Saves.Game("<ref>Thing_Old1</ref><text>Thing_Old1</text>");
        string trace = Trace(Event("Reference", "/savegame[1]/game[1]/ref[1]", "Thing_Old1", "Thing_New1"));
        Assert.Equal(1, SaveReadTrace.Apply(document, trace));
        Assert.Equal("Thing_Old1", document.Descendants("text").Single().Value);
        Assert.Equal("Thing_New1", document.Descendants("ref").Single().Value);
        Assert.Throws<InvalidOperationException>(() => SaveReadTrace.Apply(document, trace));
    }

    [Fact]
    public void IdenticalRepeatedActualReadIsIdempotentWithinOneTrace()
    {
        XDocument document = Saves.Game("<ref>old</ref>");
        JsonObject first = Event("Reference", "/savegame[1]/game[1]/ref[1]", "old", "new");
        Assert.Equal(1, SaveReadTrace.Apply(document, Trace(first, (JsonObject)first.DeepClone())));
    }

    [Theory]
    [InlineData("/savegame[1]/game[1]/absent[1]", "old", "Reference")]
    [InlineData("/savegame[1]/game[1]/ref[1]", "wrong", "Reference")]
    [InlineData("/savegame[1]/game[1]/ref[1]", "old", "Claimed")]
    public void InvalidReadEvidenceFails(string path, string oldValue, string kind)
        => Assert.Throws<InvalidOperationException>(() => SaveReadTrace.Apply(Saves.Game("<ref>old</ref>"), Trace(Event(kind, path, oldValue, "new"))));

    [Fact]
    public void DictionaryDeletionRequiresExactPairsAfterAliases()
    {
        XDocument document = Saves.Game("<dict><keys><li>old</li><li>keep</li></keys><values><li>3.1</li><li>4.2</li></values></dict>");
        var removal = new JsonObject { ["kind"] = "DictionaryPairs", ["path"] = "/savegame[1]/game[1]/dict[1]",
            ["indices"] = new JsonArray(0), ["oldKeys"] = new JsonArray("null"), ["oldValues"] = new JsonArray("3.1") };
        Assert.Throws<InvalidOperationException>(() => SaveReadTrace.Apply(new XDocument(document), Trace((JsonObject)removal.DeepClone())));
        Assert.Equal(3, SaveReadTrace.Apply(document, Trace(Event("ReferenceList", "/savegame[1]/game[1]/dict[1]/keys[1]/li[1]", "old", "null"), removal)));
        Assert.Equal("keep", document.Descendants("keys").Single().Value);
        Assert.Equal("4.2", document.Descendants("values").Single().Value);
    }

    [Fact]
    public void CurrentProductionCompSerializationRootsAreDiscoverable()
    {
        Type[] types = [typeof(Mugirl.Features.Milk.CompMilkProduction), typeof(Mugirl.Features.WeaponWheel.CompWeaponWheel), typeof(Mugirl.Features.Mounting.CompMount)];
        var roots = new CurrentScribeRoots(types[0].Assembly);
        roots.Discover(types);
        Assert.True(roots.Problems.Count == 0, string.Join("\n", roots.Problems));
        Assert.Contains("mugirlMilkFullness", roots.Labels[types[0].FullName!]);
        Assert.Contains("mugirlWheelSlotRecords", roots.Labels[types[1].FullName!]);
        Assert.Contains("mugirlMountSchedule", roots.Labels[types[2].FullName!]);
    }

    [Fact]
    public void ActualProductionDomPreviewKeepsConsumedMoneyAsCanonicalEvidence()
    {
        XDocument source = XDocument.Parse("""
            <savegame><meta><gameVersion>1.6.4871 rev591</gameVersion><modIds><li>har.mugirlrace</li></modIds></meta>
            <game><components><li Class="Mugirl.CorporateNetwork"><corporateLoanPrincipal>315.0001</corporateLoanPrincipal>
            <corporateLoanInterest>0.0002</corporateLoanInterest><corporateRecords><li><detail> null </detail></li></corporateRecords>
            </li></components></game></savegame>
            """, LoadOptions.PreserveWhitespace);
        string original = source.ToString(SaveOptions.DisableFormatting);
        CanonicalSavePreview preview = CanonicalSavePreview.Create(typeof(Mugirl.Features.Milk.CompMilkProduction).Assembly, source);
        Assert.Equal(original, source.ToString(SaveOptions.DisableFormatting));
        XElement money = Assert.Single(preview.Document.Descendants(), e => !e.HasElements && e.Value == "315.0001");
        Assert.True(money.Ancestors().Count() > 4);
        Assert.Contains(preview.Document.Descendants(), e => !e.HasElements && e.Value == " null ");
        var bad = new XDocument(preview.Document);
        bad.Descendants().Single(e => !e.HasElements && e.Value == "315.0001").Value = "315.5";
        Assert.NotEmpty(Diff().Compare(preview.Document, bad));
    }

    [Fact]
    public void AllLoadableCurrentThingCompRootsResolveWithoutGuessing()
    {
        Assembly assembly = typeof(Mugirl.Features.Milk.CompMilkProduction).Assembly;
        IEnumerable<Type> types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.OfType<Type>();
        }
        var roots = new CurrentScribeRoots(assembly);
        roots.Discover(types.Where(t => !t.IsAbstract && typeof(ThingComp).IsAssignableFrom(t)));
        Assert.True(roots.Labels.Count >= 10);
        Assert.True(roots.Problems.Count == 0, string.Join("\n", roots.Problems));
    }

    [Fact]
    public void ThinkKeyDefaultOmissionOnlyAppliesToTheTracedJobLeaf()
    {
        XDocument input = Saves.Game("<jobs><curJob><lastJobGiverKey>123</lastJobGiverKey></curJob></jobs><lastJobGiverKey>123</lastJobGiverKey>");
        Assert.Equal(1, SaveReadTrace.Apply(input, Trace(Event("ThinkKey", "/savegame[1]/game[1]/jobs[1]/curJob[1]/lastJobGiverKey[1]", "123", "-1"))));
        Assert.Null(input.Descendants("curJob").Single().Element("lastJobGiverKey"));
        Assert.Equal("123", Assert.Single(input.Descendants("lastJobGiverKey")).Value);
    }

    [Fact]
    public void ClaimedLordAndConsumedValuesCannotExplainArbitraryChanges()
    {
        SaveNameMap map = new K1Builder().LordPolicy("Mugirl.LordJob", "claimed:Corporate.Graph")
            .Value("Money", "315.0001", null).ValueContext("Money", "Class(Mugirl.LordJob)", "loan", "scalar").Build();
        var explainer = new SaveDiffExplainer(map, new SaveOwnerCatalog(map, LegacyCorpus.Empty, []), true, _ => false);
        XDocument a = Saves.Game("<lords><li><loadID>1</loadID><lordJob Class=\"Mugirl.LordJob\"><loan>315.0001</loan></lordJob><ticksInToil>17</ticksInToil></li></lords>");
        var b = new XDocument(a);
        b.Descendants("loan").Single().Value = "315.5";
        b.Descendants("ticksInToil").Single().Value = "0";
        List<SaveDifference> differences = Diff().Compare(a, b);
        Assert.Equal(2, differences.Count);
        Assert.All(differences, d => Assert.Null(explainer.Explain(d, differences)));
    }

    [Fact]
    public void CurrentFactQueriesReadNestedStateAndDoNotCountEmptySlotRecordsAsWeapons()
    {
        XDocument document = Saves.Game("""
            <things><thing Class="Pawn"><def>Mugirl</def><id>Mugirl1</id><kindDef>MugirlColonist</kindDef><mugirlMilkFullness>0.1234567</mugirlMilkFullness>
            <mugirlWheelSlotRecords><li><weapon>null</weapon><unlocked>True</unlocked></li><li><weapon>Thing_Steel1</weapon></li></mugirlWheelSlotRecords></thing></things>
            <components><li Class="Mugirl.CorporateNetwork"><finance><loan>315.0001</loan></finance><trade><orders><li><text> null </text></li></orders></trade></li></components>
            """);
        SaveFacts facts = SaveFacts.Read(document, SaveFactQueries.Current(["Mugirl"], ["Mugirl"]));
        Assert.Equal("0.1234567", facts.Values["pawn/Mugirl1/milk/fullness"]);
        Assert.Equal("1", facts.Values["pawn/Mugirl1/wheel/slots"]);
        Assert.Equal("1", facts.Values["pawn/Mugirl1/wheel/unlocked"]);
        Assert.Equal("315.0001", facts.Values["component/Mugirl.CorporateNetwork/finance/loan"]);
        Assert.Equal(" null ", facts.Values["component/Mugirl.CorporateNetwork/trade/orders/li/text"]);
    }

    [Fact]
    public void DictionaryDiagnosticsAggregateOnlyTheExactRuntimeStep()
    {
        static JsonObject Step(string id, long actions) => new() { ["id"] = id, ["phase"] = "dom", ["actions"] = actions };
        var runtime = new JsonObject { ["steps"] = new JsonArray(Step("Core.Renames", 3), Step(SaveActionParity.DictionaryStep, 2), Step(SaveActionParity.DictionaryStep, 2)) };
        var offline = new JsonObject { ["steps"] = new JsonArray(Step("Core.Renames", 3)) };
        Assert.Empty(SaveActionParity.Compare(runtime, offline));
        string trace = Trace(new JsonObject { ["kind"] = "DictionaryPairs", ["indices"] = new JsonArray(0, 2),
            ["oldKeys"] = new JsonArray("null", "null"), ["oldValues"] = new JsonArray("2", "4") });
        SaveActionParity.VerifyDictionaryActions(runtime, trace);
        Assert.Throws<InvalidOperationException>(() => SaveActionParity.VerifyDictionaryActions(runtime, Trace()));
        ((JsonArray)runtime["steps"]!).Add(Step("Unexpected.Dom", 2));
        Assert.Single(SaveActionParity.Compare(runtime, offline));
    }

    [Fact]
    public void ATextThatLooksLikeALoadIdCannotBeTranslatedOrExplainAChange()
    {
        SaveNameMap map = new K1Builder().Def("ThingDef", "Mugirl_Old", "Mugirl_New").Build();
        XDocument input = Component("<playerText>Thing_Mugirl_Old123</playerText>");
        var catalog = new SaveOwnerCatalog(map, LegacyCorpus.Empty, []);
        map.Apply(input, catalog);
        XElement text = input.Descendants("playerText").Single();
        Assert.Equal("Thing_Mugirl_Old123", text.Value);
        Assert.False(map.ExplainsValueChange(text.Value, "Thing_Mugirl_New123", text, catalog));
    }

    private static JsonObject Event(string kind, string path, string from, string to)
        => new() { ["kind"] = kind, ["path"] = path, ["oldValue"] = from, ["newValue"] = to };

    private static string Trace(params JsonObject[] events)
        => new JsonObject { ["format"] = 1, ["events"] = new JsonArray([.. events.Cast<JsonNode>()]), ["errors"] = new JsonArray() }.ToJsonString();
}
