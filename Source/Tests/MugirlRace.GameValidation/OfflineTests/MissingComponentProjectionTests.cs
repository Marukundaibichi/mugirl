using System.Reflection;
using System.IO;
using System.Xml.Linq;
using HarmonyLib;
using Mugirl.Features.Corporate;
using Mugirl.Features.Incidents;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

[Collection("A5 real Scribe")]
public sealed class MissingComponentProjectionTests
{
    private const string Story = "Mugirl.Features.Incidents.GameComponent_Storyline";
    private static readonly Assembly Production = typeof(CorporateNetwork).Assembly;

    [Fact]
    public void ProvenMissingComponentsHaveExactFieldsAndFollowTheActualCachedOrder()
    {
        using var cache = new Cache();
        XDocument original = Original(), expected = Canonical();
        string originalText = original.ToString(SaveOptions.DisableFormatting);
        MissingComponentProjection proof = MissingComponentProjection.Capture(Production);
        proof.Apply(expected, original);
        Assert.Equal(4, proof.Actions.Count);
        Assert.Equal(new[] { Story, MissingComponentProjection.Network, MissingComponentProjection.Introduction, MissingComponentProjection.Hub },
            expected.Root!.Element("game")!.Element("components")!.Elements().Select(e => SaveXml.ClassOf(e)));
        XElement network = Component(expected, MissingComponentProjection.Network);
        Assert.Equal(new[] { "vault", "history", "trade", "loan", "people", "missionBoard", "services" }, network.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("null", network.Element("loan")!.Element("creditor")!.Value);
        Assert.Empty(network.Element("services")!.Element("creditedPawnIds")!.Nodes());
        Assert.Null(network.Descendants("supportCallsUsed").SingleOrDefault());
        XElement contact = Component(expected, MissingComponentProjection.Introduction).Element("contact")!;
        Assert.Equal(new[] { "attached", "retrofitted", "courierPawn", "representativePawn", "visitMap", "quest" }, contact.Elements().Select(e => e.Name.LocalName));
        Assert.Equal(new[] { "True", "True", "null", "null", "null", "null" }, contact.Elements().Select(e => e.Value));
        XElement migration = Component(expected, MissingComponentProjection.Migration);
        Assert.True(XNode.DeepEquals(XElement.Parse("<migration><members/><playerKillIds/></migration>"), migration.Element("migration")));
        Assert.Equal("True", migration.Element("initialization")!.Attribute("IsNull")!.Value);
        XElement punisher = Component(expected, MissingComponentProjection.Punisher);
        Assert.Equal(new[] { "activePunishers", "initialization", "heldLoot", "lootBursts" }, punisher.Elements().Select(e => e.Name.LocalName));
        Assert.Empty(punisher.Element("heldLoot")!.Element("innerList")!.Nodes());
        Assert.Equal(originalText, original.ToString(SaveOptions.DisableFormatting));
        string once = expected.ToString(SaveOptions.DisableFormatting);
        MissingComponentProjection second = MissingComponentProjection.Capture(Production);
        second.Apply(expected, original);
        Assert.Empty(second.Actions);
        Assert.Equal(once, expected.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void CapturedEnumerationDeterminesPlacementInsteadOfAlphabeticOrAfterOrder()
    {
        using var cache = new Cache(reverse: true);
        XDocument expected = Canonical();
        MissingComponentProjection.Capture(Production).Apply(expected, Original());
        Assert.Equal(new[] { Story, MissingComponentProjection.Hub, MissingComponentProjection.Introduction, MissingComponentProjection.Network },
            expected.Root!.Element("game")!.Element("components")!.Elements().Select(e => SaveXml.ClassOf(e)));
        Assert.Equal(new[] { MissingComponentProjection.Punisher, MissingComponentProjection.Migration },
            expected.Descendants("maps").Single().Element("li")!.Element("components")!.Elements().Select(e => SaveXml.ClassOf(e)));
    }

    [Fact]
    public void ExistingHubAndAssetsKeepTheirOrderIdentityAndFullValues()
    {
        using var cache = new Cache();
        XDocument original = Original(), expected = Canonical();
        XElement sourceComponents = original.Root!.Element("game")!.Element("components")!;
        sourceComponents.AddFirst(new XElement("li", new XAttribute("Class", MissingComponentProjection.Hub), new XElement("mugirlSaveSchema", "0")));
        XElement expectedComponents = expected.Root!.Element("game")!.Element("components")!;
        Component(expected, MissingComponentProjection.Hub).Remove();
        expectedComponents.AddFirst(new XElement("li", new XAttribute("Class", MissingComponentProjection.Hub), new XElement("mugirlSaveSchema", "1")));
        sourceComponents.Add(XElement.Parse("<li Class='Mugirl.CorporateNetwork'><corporateVault><innerList><li><id>123</id></li></innerList></corporateVault></li>"));
        var network = XElement.Parse("<li Class='" + MissingComponentProjection.Network + "'><vault><innerList><li><id>123</id><label>  player text  </label></li></innerList></vault></li>");
        expectedComponents.Add(network);
        string value = network.ToString(SaveOptions.DisableFormatting);
        var proof = MissingComponentProjection.Capture(Production); proof.Apply(expected, original);
        Assert.Equal(3, proof.Actions.Count);
        Assert.Equal(MissingComponentProjection.Hub, SaveXml.ClassOf(expected.Root!.Element("game")!.Element("components")!.Elements().First()));
        Assert.Equal(value, Component(expected, MissingComponentProjection.Network).ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void ExistingDerivedComponentSatisfiesTheBaseFillCheckAndKeepsItsAssets()
    {
        using var cache = new Cache();
        cache.Game.Add(typeof(DerivedNetwork));
        XDocument original = Original(), expected = Canonical();
        var derived = XElement.Parse("<li Class='" + typeof(DerivedNetwork).FullName + "'><vault><innerList><li><id>42</id></li></innerList></vault></li>");
        original.Root!.Element("game")!.Element("components")!.Add(new XElement(derived));
        Component(expected, MissingComponentProjection.Hub).AddBeforeSelf(new XElement(derived));
        var proof = MissingComponentProjection.Capture(Production); proof.Apply(expected, original);
        Assert.Equal(3, proof.Actions.Count);
        Assert.DoesNotContain(expected.Descendants("li"), e => SaveXml.ClassOf(e) == MissingComponentProjection.Network);
        Assert.True(XNode.DeepEquals(derived, Component(expected, typeof(DerivedNetwork).FullName!)));
    }

    [Theory]
    [InlineData("ended")]
    [InlineData("pawn")]
    [InlineData("opening")]
    [InlineData("triggered")]
    [InlineData("unknown-part")]
    [InlineData("duplicate-part")]
    [InlineData("duplicate-field")]
    [InlineData("unknown-class")]
    [InlineData("source-assets")]
    [InlineData("source-short-assets")]
    [InlineData("source-short-map-assets")]
    [InlineData("source-ambiguous-short")]
    [InlineData("unknown-map")]
    [InlineData("nested-owner")]
    public void UnprovenSourcesFailAtomicallyWithoutReplacingAnyData(string condition)
    {
        using var cache = new Cache();
        XDocument original = Original(), expected = Canonical();
        XElement game = original.Root!.Element("game")!, quest = game.Element("questManager")!.Element("quests")!.Element("li")!;
        switch (condition)
        {
            case "ended": quest.Add(new XElement("ended", "True")); break;
            case "pawn": quest.Descendants("courier").Single().Value = "Thing_Mugirl8"; break;
            case "opening": game.Descendants("openingCrashStarted").Single().Value = "False"; break;
            case "triggered": game.Element("components")!.Element("li")!.Add(new XElement("courierRaidTriggered", "True")); break;
            case "unknown-part": quest.Element("parts")!.AddFirst(new XElement("li", new XAttribute("Class", "Other.DerivedSpawnCourier"))); break;
            case "duplicate-part": quest.Element("parts")!.Add(new XElement(quest.Element("parts")!.Element("li")!)); break;
            case "duplicate-field": quest.Add(new XElement("acceptanceTick", "1")); break;
            case "unknown-class": expected.Root!.Element("game")!.Element("components")!.AddFirst(new XElement("li", new XAttribute("Class", "Other.Network"))); break;
            case "source-assets": game.Element("components")!.Add(XElement.Parse("<li Class='Mugirl.CorporateNetwork'><corporateVault><innerList><li><id>1</id></li></innerList></corporateVault></li>")); break;
            case "source-short-assets": game.Element("components")!.Add(XElement.Parse("<li Class='CorporateNetwork'><corporateVault><innerList><li><id>1</id></li></innerList></corporateVault></li>")); break;
            case "source-short-map-assets": game.Element("maps")!.Element("li")!.Element("components")!.Add(XElement.Parse("<li Class='MapComponent_Punisher'><heldLoot><innerList><li><id>1</id></li></innerList></heldLoot></li>")); break;
            case "source-ambiguous-short":
                cache.Game.Add(typeof(AlternativeTypes.CorporateNetwork));
                game.Element("components")!.Add(XElement.Parse("<li Class='CorporateNetwork'><vault><innerList/></vault></li>")); break;
            case "unknown-map": expected.Descendants("uniqueID").Single().Value = "9"; break;
            case "nested-owner":
                XElement source = game.Element("components")!; source.Remove(); game.Add(new XElement("nested", source)); break;
        }
        string before = expected.ToString(SaveOptions.DisableFormatting);
        var proof = MissingComponentProjection.Capture(Production);
        Assert.Throws<InvalidOperationException>(() => proof.Apply(expected, original));
        Assert.Empty(proof.Actions);
        Assert.Equal(before, expected.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void MissingOrChangedRealCacheCannotBeReplacedByAnInventedEnumeration()
    {
        using var cache = new Cache();
        var proof = MissingComponentProjection.Capture(Production);
        cache.Game.Reverse();
        Assert.Throws<InvalidOperationException>(() => proof.Apply(Canonical(), Original()));
        cache.Values.Remove(typeof(MapComponent));
        Assert.Throws<InvalidOperationException>(() => MissingComponentProjection.Capture(Production));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtraConstructorOrReaderPatchInvalidatesTheProofEvenAfterEarlierSuccess(bool sourceStoryConstructor)
    {
        using var cache = new Cache();
        var proof = MissingComponentProjection.Capture(Production);
        proof.Apply(Canonical(), Original());
        var harmony = new Harmony("A5.MissingComponent.ConstructorPatch");
        ConstructorInfo constructor = (sourceStoryConstructor ? typeof(GameComponent_Storyline) : typeof(CorporateNetwork)).GetConstructor([typeof(Game)])!;
        harmony.Patch(constructor, prefix: new HarmonyMethod(typeof(MissingComponentProjectionTests), nameof(Noop)));
        try { Assert.Throws<InvalidOperationException>(() => proof.Apply(Canonical(), Original())); }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    [Fact]
    public void ProjectedFieldsMatchRealFreshConstructorsAndTheActualSavingWriter()
    {
        using var cache = new Cache();
        XDocument expected = Canonical();
        MissingComponentProjection.Capture(Production).Apply(expected, Original());
        FieldInfo overlay = typeof(CorporateNetwork).GetField("defaultedLoanFaction", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previous = overlay.GetValue(null);
        try
        {
            IExposable[] fresh = [new CorporateNetwork(null!), new CorporateIntroduction(null!), new MapComponent_Migration(null!), new MapComponent_Punisher(null!)];
            foreach (IExposable component in fresh)
            {
                XElement projected = new(Component(expected, component.GetType().FullName!));
                projected.Attribute("Class")!.Remove();
                // 独立fresh writer没有调用LoadedGame；两个True另由已证源前提及真实PreRef B/B2校准。
                if (component is CorporateIntroduction)
                {
                    projected.Element("contact")!.Element("attached")!.Remove();
                    projected.Element("contact")!.Element("retrofitted")!.Remove();
                }
                Assert.True(XNode.DeepEquals(projected, SaveFresh(component)), component.GetType().FullName);
            }
        }
        finally { overlay.SetValue(null, previous); }
    }

    [Fact]
    public void ProjectionDoesNotConsumeGameRandomState()
    {
        using var cache = new Cache();
        int expected;
        using (Rand.Block(782)) { expected = Rand.Int; }
        using (Rand.Block(782))
        {
            MissingComponentProjection.Capture(Production).Apply(Canonical(), Original());
            Assert.Equal(expected, Rand.Int);
        }
    }

    [Fact]
    public void ExtraQuestReaderPatchCannotMasqueradeAsOngoingNullCourierHistory()
    {
        using var cache = new Cache();
        var proof = MissingComponentProjection.Capture(Production);
        var harmony = new Harmony("A5.MissingComponent.QuestReaderPatch");
        harmony.Patch(typeof(QuestPart_Delay).GetMethod("ExposeData")!, prefix: new HarmonyMethod(typeof(MissingComponentProjectionTests), nameof(Noop)));
        try { Assert.Throws<InvalidOperationException>(() => proof.Apply(Canonical(), Original())); }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("other")]
    [InlineData("different-instance")]
    [InlineData("database")]
    public void ChangedOrUnboundDefOfCannotAuthorizeTheTextualQuestRoot(string mutation)
    {
        using var cache = new Cache();
        var proof = MissingComponentProjection.Capture(Production);
        switch (mutation)
        {
            case "null": cache.CourierField.SetValue(null, null); break;
            case "other": cache.CourierField.SetValue(null, new QuestScriptDef { defName = "OtherRoot" }); break;
            case "different-instance": cache.CourierField.SetValue(null, new QuestScriptDef { defName = "Mugirl_CourierContact" }); break;
            case "database": DefDatabase<QuestScriptDef>.Clear(); break;
        }
        XDocument expected = Canonical(); string before = expected.ToString(SaveOptions.DisableFormatting);
        Assert.Throws<InvalidOperationException>(() => proof.Apply(expected, Original()));
        Assert.Equal(before, expected.ToString(SaveOptions.DisableFormatting));
        Assert.Throws<InvalidOperationException>(() => MissingComponentProjection.Capture(Production));
    }

    [Fact]
    public void QualityObserverInstalledBeforeCaptureIsProvenByItsExactCurrentChain()
    {
        using var cache = new Cache();
        using var quality = new QualityHooks();
        var proof = MissingComponentProjection.Capture(Production);
        XDocument expected = Canonical(); proof.Apply(expected, Original());
        Assert.Equal(4, proof.Actions.Count);
        Assert.Equal(3, proof.Readers.OfType<System.Text.Json.Nodes.JsonObject>().Count(r => (string?)r["proof"] is { } text
            && text.Contains("exact active quality observer registration", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("same-owner")]
    [InlineData("callback")]
    [InlineData("duplicate")]
    [InlineData("priority")]
    [InlineData("before")]
    [InlineData("proof-spoof")]
    [InlineData("inner-prefix")]
    [InlineData("inner-postfix")]
    [InlineData("inner-finalizer")]
    public void LaterChangesToTheObservedConverterOrItsProofStillFailAtomically(string change)
    {
        using var cache = new Cache();
        using var quality = new QualityHooks();
        var proof = MissingComponentProjection.Capture(Production);
        var extra = new Harmony(change is "same-owner" or "duplicate" or "priority" or "before"
            ? ValidationContext.OwnHarmonyId : "A5.MissingComponent.QualityForeign");
        MethodInfo converter = (MethodInfo)ReviewedEngine.ResolveMethod(0x060034A1)!;
        MethodInfo callback = typeof(UnconsumedQualityObservation).GetMethod("BeforeConverter", BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo noop = typeof(MissingComponentProjectionTests).GetMethod(nameof(Noop), BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            if (change.StartsWith("inner-", StringComparison.Ordinal))
            {
                var patch = new HarmonyMethod(noop) { innerMethod = new InnerMethod((MethodInfo)ReviewedEngine.ResolveMethod(0x060034B1)!, []) };
                PatchProcessor processor = extra.CreateProcessor(converter);
                if (change == "inner-prefix") { processor.AddInnerPrefix(patch); }
                else if (change == "inner-postfix") { processor.AddInnerPostfix(patch); }
                else { processor.AddInnerFinalizer(patch); }
                processor.Patch();
                Patches actual = Harmony.GetPatchInfo(converter)!;
                Assert.Equal(1, actual.InnerPrefixes.Count + actual.InnerPostfixes.Count + actual.InnerFinalizers.Count);
            }
            else if (change == "proof-spoof")
            {
                extra.Patch(typeof(UnconsumedQualityObservation).GetMethod("ProveRegisteredHook", BindingFlags.NonPublic | BindingFlags.Static)!,
                    prefix: new HarmonyMethod(typeof(MissingComponentProjectionTests), nameof(ForgeQualityProof)));
            }
            else
            {
                MethodInfo target = change == "callback" ? callback : converter;
                MethodInfo hook = change is "duplicate" or "priority" or "before" ? callback : noop;
                var patch = new HarmonyMethod(hook) { priority = change == "priority" ? Priority.High : Priority.First };
                if (change == "before") { patch.before = ["Other.Control"]; }
                extra.Patch(target, prefix: patch);
                if (change == "duplicate") { Assert.Equal(2, Harmony.GetPatchInfo(converter)!.Prefixes.Count(p => p.PatchMethod == callback)); }
            }
            XDocument expected = Canonical(); string before = expected.ToString(SaveOptions.DisableFormatting);
            Assert.Throws<InvalidOperationException>(() => proof.Apply(expected, Original()));
            Assert.Equal(before, expected.ToString(SaveOptions.DisableFormatting));
        }
        finally { extra.UnpatchAll(extra.Id); }
    }

    private static bool ForgeQualityProof(ref string? __result) { __result = "forged proof"; return false; }

    private sealed class QualityHooks : IDisposable
    {
        private readonly Harmony harmony = new(ValidationContext.OwnHarmonyId);
        internal QualityHooks() => UnconsumedQualityObservation.Start(Production, harmony,
            Saves.Game("<maps><li><things><thing Class='Apparel'><def>Mugirl_AutoloadingSystem</def><id>Mugirl_AutoloadingSystem1</id><quality>Normal</quality></thing></things></li></maps>"));
        public void Dispose() { UnconsumedQualityObservation.Stop(); harmony.UnpatchAll(harmony.Id); }
    }

    private static XElement SaveFresh(IExposable component)
    {
        ScribeLoader loader = Scribe.loader;
        ScribeSaver saver = Scribe.saver;
        LoadSaveMode mode = Scribe.mode;
        string path = Path.Combine(Path.GetTempPath(), "mugirl-missing-component-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            Scribe.saver = new ScribeSaver(); Scribe.mode = LoadSaveMode.Inactive;
            Scribe.saver.InitSaving(path, "li"); component.ExposeData(); Scribe.saver.FinalizeSaving();
            return XElement.Load(path);
        }
        finally
        {
            if (Scribe.mode == LoadSaveMode.Saving) { Scribe.saver.ForceStop(); }
            Scribe.loader = loader; Scribe.saver = saver; Scribe.mode = mode;
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    private static void Noop() { }
    private static class AlternativeTypes
    {
        internal sealed class CorporateNetwork : GameComponent { }
    }
    private sealed class DerivedNetwork(Game game) : CorporateNetwork(game) { }
    private static XElement Component(XDocument document, string name) => document.Descendants("li").Single(e => SaveXml.ClassOf(e) == name);
    private static XDocument Original() => Saves.Game("""
        <components><li Class="Mugirl.MugirlStoryState"><openingCrashStarted>True</openingCrashStarted></li></components>
        <questManager><quests><li><root>Mugirl_CourierRaid</root><acceptanceTick>1</acceptanceTick><parts>
        <li Class="Mugirl.QuestPart_SpawnCourier"><courier>null</courier><map>Map_0</map></li>
        </parts></li></quests></questManager><maps><li><uniqueID>0</uniqueID><components/></li></maps>
        """);
    private static XDocument Canonical() => Saves.Game("""
        <components><li Class="Mugirl.Features.Incidents.GameComponent_Storyline"><opening><started>True</started></opening><courier/></li>
        <li Class="Mugirl.Core.GameComponent_Mugirl"><mugirlSaveSchema>1</mugirlSaveSchema></li></components>
        <questManager><quests><li><root>Mugirl_CourierContact</root><acceptanceTick>1</acceptanceTick><parts>
        <li Class="Mugirl.Features.Incidents.QuestPart_SpawnCourier"><courier>null</courier><map>Map_0</map></li>
        </parts></li></quests></questManager><maps><li><uniqueID>0</uniqueID><components/></li></maps>
        """);

    private sealed class Cache : IDisposable
    {
        internal Dictionary<Type, List<Type>> Values { get; }
        internal List<Type> Game => Values[typeof(GameComponent)];
        internal FieldInfo CourierField { get; }
        private readonly List<Type>? previousGame;
        private readonly List<Type>? previousMap;
        private readonly object? previousCourier;
        private readonly QuestScriptDef[] previousQuests;
        internal Cache(bool reverse = false)
        {
            Values = (Dictionary<Type, List<Type>>)typeof(GenTypes).GetField("cachedSubclassesNonAbstract", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Values.TryGetValue(typeof(GameComponent), out previousGame);
            Values.TryGetValue(typeof(MapComponent), out previousMap);
            Values[typeof(GameComponent)] = [typeof(GameComponent_Storyline), typeof(CorporateNetwork), typeof(CorporateIntroduction), Production.GetType(MissingComponentProjection.Hub)!];
            Values[typeof(MapComponent)] = [typeof(MapComponent_Migration), typeof(MapComponent_Punisher)];
            if (reverse) { Values[typeof(GameComponent)].Reverse(); Values[typeof(MapComponent)].Reverse(); }
            CourierField = Production.GetType("Mugirl.Features.Incidents.IncidentsDefOf")!.GetField("Mugirl_CourierContact", BindingFlags.Public | BindingFlags.Static)!;
            previousCourier = CourierField.GetValue(null);
            previousQuests = [.. DefDatabase<QuestScriptDef>.AllDefsListForReading];
            DefDatabase<QuestScriptDef>.Clear();
            var courier = new QuestScriptDef { defName = "Mugirl_CourierContact" };
            DefDatabase<QuestScriptDef>.Add(courier); CourierField.SetValue(null, courier);
        }
        public void Dispose()
        {
            if (previousGame is null) { Values.Remove(typeof(GameComponent)); } else { Values[typeof(GameComponent)] = previousGame; }
            if (previousMap is null) { Values.Remove(typeof(MapComponent)); } else { Values[typeof(MapComponent)] = previousMap; }
            CourierField.SetValue(null, previousCourier);
            DefDatabase<QuestScriptDef>.Clear();
            foreach (QuestScriptDef definition in previousQuests) { DefDatabase<QuestScriptDef>.Add(definition); }
        }
    }
}
