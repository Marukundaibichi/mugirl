using System.Globalization;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using HarmonyLib;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

[Collection("A5 real Scribe")]
public sealed class CorporatePriceObservationTests : IDisposable
{
    private const string Network = "Mugirl.Features.Corporate.CorporateNetwork";
    private const string Offer = "Mugirl.Features.Corporate.CorporatePersonOffer";
    private const string Completion = "Mugirl.SaveUpgrade.CorporateOfferPriceCompletion";
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private readonly Assembly production = Assembly.Load("MugirlRace");
    private readonly Harmony harmony = new(ValidationContext.OwnHarmonyId);
    private readonly ScribeLoader previousLoader = Scribe.loader;
    private readonly LoadSaveMode previousMode = Scribe.mode;
    private readonly StatDef previousMarket = StatDefOf.MarketValue;
    private readonly Game? previousGame = Current.Game;
    private readonly object? previousConfig;
    private readonly FieldInfo configField;
    private readonly ObservationMainDocumentFixture mainDocument = new();
    private static readonly List<string> engineErrors = [];

    public CorporatePriceObservationTests()
    {
        configField = production.GetType("Mugirl.Features.Corporate.CorporateDefOf", true)!.GetField("Mugirl_CorporatePeopleConfig", Members)!;
        previousConfig = configField.GetValue(null);
        engineErrors.Clear();
        harmony.Patch(typeof(Log).GetMethod(nameof(Log.Error), [typeof(string)])!, prefix: new HarmonyMethod(typeof(CorporatePriceObservationTests), nameof(CaptureError)));
    }
    public void Dispose()
    {
        CorporatePriceObservation.Stop();
        harmony.UnpatchAll(harmony.Id);
        Scribe.loader = previousLoader; Scribe.mode = previousMode;
        StatDefOf.MarketValue = previousMarket; Current.Game = previousGame;
        configField.SetValue(null, previousConfig);
        mainDocument.Dispose();
    }

    [Theory]
    [InlineData(675f, 1.5f, 1013)]
    [InlineData(1615f, 1.5f, 2423)]
    [InlineData(2585f, 1.5f, 3878)]
    [InlineData(4035f, 1.5f, 6053)]
    [InlineData(10f, -1f, 1)]
    [InlineData(float.NaN, 1.5f, 1)]
    [InlineData(float.PositiveInfinity, 1.5f, 1)]
    [InlineData(100000000f, 1f, 100000000)]
    public void IndependentArithmeticUsesFloatProductThenDecimalCeiling(float market, float multiplier, int expected)
        => Assert.Equal(expected, CorporatePriceObservation.Calculate(BitConverter.SingleToInt32Bits(market), BitConverter.SingleToInt32Bits(multiplier)));

    [Fact]
    public void EmptyCurrentSaveHasNoPricingEventsAndDoesNotCallMarketValue()
    {
        CorporatePriceObservation.Start(production, harmony, Document("<savegame><game><components/></game></savegame>"));
        JsonObject report = JsonNode.Parse(CorporatePriceObservation.SnapshotJson())!.AsObject();
        Assert.Empty(report["events"]!.AsArray()); Assert.Empty(report["errors"]!.AsArray());
    }

    [Fact]
    public void OrdinaryPricingOutsideTheBoundContinuationKeepsItsOriginalResult()
    {
        CorporatePriceObservation.Start(production, harmony, Document("<savegame><game><components/></game></savegame>"));
        object network = RuntimeHelpers.GetUninitializedObject(production.GetType(Network, true)!);
        Assert.Equal(0, production.GetType(Network, true)!.GetMethod("PeopleBasePrice")!.Invoke(network, [null]));
        JsonObject report = JsonNode.Parse(CorporatePriceObservation.SnapshotJson())!.AsObject();
        Assert.Empty(report["events"]!.AsArray()); Assert.Empty(report["errors"]!.AsArray());
    }

    [Fact]
    public void AnExtraPatchOnAnObserverMethodIsAlsoRejected()
    {
        CorporatePriceObservation.Start(production, harmony, Document("<savegame><game><components/></game></savegame>"));
        MethodInfo callback = typeof(CorporatePriceObservation).GetMethod("AfterMarket", Members)!;
        harmony.Patch(callback, postfix: new HarmonyMethod(typeof(CorporatePriceObservationTests), nameof(NoOp)));
        Assert.NotEmpty(JsonNode.Parse(CorporatePriceObservation.SnapshotJson())!["errors"]!.AsArray());
    }

    [Theory]
    [InlineData("inner-prefix", false)]
    [InlineData("inner-postfix", false)]
    [InlineData("inner-finalizer", false)]
    [InlineData("inner-prefix", true)]
    [InlineData("inner-postfix", true)]
    [InlineData("inner-finalizer", true)]
    public void NoOpInnerHooksAreRejectedAtTargetsAndObserverMethods(string role, bool observer)
    {
        CorporatePriceObservation.Start(production, harmony, Document("<savegame><game><components/></game></savegame>"));
        MethodInfo target = observer ? typeof(CorporatePriceObservation).GetMethod("AfterMarket", Members)!
            : production.GetType(Network, true)!.GetMethod("PeopleBasePrice")!;
        var foreign = new Harmony("CorporatePrice.Inner.NegativeControl");
        try
        {
            PatchProcessor processor = foreign.CreateProcessor(target);
            MethodInfo inner = observer ? typeof(BitConverter).GetMethod(nameof(BitConverter.SingleToInt32Bits), [typeof(float)])!
                : typeof(Math).GetMethod(nameof(Math.Max), [typeof(int), typeof(int)])!;
            var noop = new HarmonyMethod(typeof(CorporatePriceObservationTests), nameof(NoOp)) { innerMethod = new InnerMethod(inner, []) };
            if (role == "inner-prefix") { processor.AddInnerPrefix(noop); }
            else if (role == "inner-postfix") { processor.AddInnerPostfix(noop); }
            else { processor.AddInnerFinalizer(noop); }
            processor.Patch();
            Patches patches = Harmony.GetPatchInfo(target)!;
            Assert.Equal(1, role == "inner-prefix" ? patches.InnerPrefixes.Count : role == "inner-postfix" ? patches.InnerPostfixes.Count : patches.InnerFinalizers.Count);
            string json = CorporatePriceObservation.SnapshotJson();
            Assert.NotEmpty(JsonNode.Parse(json)!["errors"]!.AsArray());
            Assert.Throws<InvalidOperationException>(() => CorporatePriceObservation.Apply(XDocument.Parse("<savegame><game/></savegame>"), json));
            if (!observer) { Assert.Contains(JsonNode.Parse(json)!["readers"]!.AsArray(), item => (string?)item?["role"] == role); }
        }
        finally { foreign.UnpatchAll(foreign.Id); }
    }

    [Fact]
    public void OrdinaryUnboundPricingOnAnotherThreadIsIgnored()
    {
        CorporatePriceObservation.Start(production, harmony, Document("<savegame><game><components/></game></savegame>"));
        object network = RuntimeHelpers.GetUninitializedObject(production.GetType(Network, true)!);
        object? result = null;
        OnWorker(() => result = production.GetType(Network, true)!.GetMethod("PeopleBasePrice")!.Invoke(network, [null]));
        Assert.Equal(0, result);
        JsonObject report = JsonNode.Parse(CorporatePriceObservation.SnapshotJson())!.AsObject();
        Assert.Empty(report["errors"]!.AsArray());
        Assert.Empty(report["events"]!.AsArray());
    }

    [Fact]
    public void ForeignPricePatchCannotAuthorizeAProjectionEvenWhenItDoesNothing()
    {
        MethodInfo price = production.GetType(Network, true)!.GetMethod("PeopleBasePrice")!;
        var foreign = new Harmony("CorporatePrice.Foreign.NegativeControl");
        try
        {
            foreign.Patch(price, postfix: new HarmonyMethod(typeof(CorporatePriceObservationTests), nameof(NoOp)));
            CorporatePriceObservation.Start(production, harmony, Document("<savegame><game><components/></game></savegame>"));
            string report = CorporatePriceObservation.SnapshotJson();
            Assert.NotEmpty(JsonNode.Parse(report)!["errors"]!.AsArray());
            Assert.Throws<InvalidOperationException>(() => CorporatePriceObservation.Apply(XDocument.Parse("<savegame><game/></savegame>"), report));
        }
        finally { foreign.UnpatchAll(foreign.Id); }
    }
    private static void NoOp() { }
    private static bool CaptureError(string text) { engineErrors.Add(text); return false; }

    [Theory]
    [InlineData("wrong-price")]
    [InlineData("wrong-pawn")]
    [InlineData("wrong-owner")]
    [InlineData("wrong-continuation")]
    [InlineData("duplicate-event")]
    [InlineData("one-ulp-input")]
    [InlineData("error")]
    public void ReplayRejectsUnprovenInputsBeforeChangingAnyField(string fault)
    {
        XDocument expected = XDocument.Parse(Canonical());
        JsonObject evidence = Evidence(expected);
        JsonObject entry = evidence["events"]![0]!.AsObject();
        if (fault == "wrong-price") { expected.Descendants("price").Single().Value = "1351"; }
        if (fault == "wrong-pawn") { entry["pawnId"] = "Thing_Mugirl999"; }
        if (fault == "wrong-owner") { expected.Descendants("li").First().SetAttributeValue("Class", "ThirdParty.UserNotes"); }
        if (fault == "wrong-continuation") { expected.Descendants("continuation").Single().SetAttributeValue("Class", "ThirdParty.Other"); }
        if (fault == "duplicate-event") { evidence["events"]!.AsArray().Add(entry.DeepClone()); }
        if (fault == "one-ulp-input") { entry["marketBits"] = BitConverter.SingleToInt32Bits(676f); }
        if (fault == "error") { evidence["errors"]!.AsArray().Add("failed reader"); }
        string before = expected.ToString(SaveOptions.DisableFormatting);
        Assert.Throws<InvalidOperationException>(() => CorporatePriceObservation.Apply(expected, evidence.ToJsonString()));
        Assert.Equal(before, expected.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void ReplayChangesOnlyPriceAndItsExactContinuation()
    {
        XDocument expected = XDocument.Parse(Canonical());
        JsonObject evidence = Evidence(expected);
        expected.Descendants("offers").Single().Elements("li").Single().Add(new XElement("playerText", "1350"));
        Assert.Equal(1, CorporatePriceObservation.Apply(expected, evidence.ToJsonString()));
        Assert.Equal("1013", expected.Descendants("price").Single().Value);
        Assert.Equal("1350", expected.Descendants("playerText").Single().Value);
        Assert.Equal("True", expected.Descendants("continuation").Single().Attribute("IsNull")!.Value);
        Assert.Equal("Thing_Mugirl10884", expected.Descendants("pawn").Single().Value);
        Assert.Throws<InvalidOperationException>(() => CorporatePriceObservation.Apply(expected, evidence.ToJsonString()));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("nested-unrelated")]
    [InlineData("factor-changed-by-input")]
    [InlineData("read-worker")]
    [InlineData("bound-cross-thread")]
    [InlineData("bound-market-cross-thread")]
    [InlineData("source-price")]
    [InlineData("source-pawn")]
    [InlineData("already-versioned")]
    [InlineData("duplicate-source")]
    [InlineData("missing-original-pawn")]
    [InlineData("same-id-other-pawn")]
    [InlineData("no-loaded-scope")]
    [InlineData("throw-market")]
    [InlineData("late-extra-hook")]
    [InlineData("duplicate-observer-hook")]
    [InlineData("side-document")]
    [InlineData("unlisted-network")]
    [InlineData("duplicate-network")]
    [InlineData("wrong-input-path")]
    public void RealCompletionAndScribeRequireExactInputsAndRestoreCallScopes(string fault)
    {
        XmlDocument original = Document(Original());
        if (fault == "source-price") { original.SelectSingleNode("//price")!.InnerText = "1351"; }
        if (fault == "source-pawn") { original.SelectSingleNode("//pawn")!.InnerText = "Thing_Mugirl999"; }
        if (fault == "already-versioned") { XmlElement version = original.CreateElement("priceVersion"); version.InnerText = "1"; original.SelectSingleNode("//corporatePeopleOffers/li")!.AppendChild(version); }
        if (fault == "duplicate-source") { XmlNode first = original.SelectSingleNode("//corporatePeopleOffers/li")!; first.ParentNode!.AppendChild(first.CloneNode(true)); }
        if (fault == "missing-original-pawn") { XmlNode missingPawn = original.SelectSingleNode("//corporateVault/innerList/li")!; missingPawn.ParentNode!.RemoveChild(missingPawn); }
        CorporatePriceObservation.Start(production, harmony, original, "pricing-main.rws");
        object network = RuntimeHelpers.GetUninitializedObject(production.GetType(Network, true)!);
        object offer = Activator.CreateInstance(production.GetType(Offer, true)!)!;
        object completion = Activator.CreateInstance(production.GetType(Completion, true)!)!;
        object config = Activator.CreateInstance(configField.FieldType)!;
        config.GetType().GetField("basePriceMultiplier")!.SetValue(config, 1.5f);
        configField.SetValue(null, config);
        Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        Current.Game.tickManager = (TickManager)RuntimeHelpers.GetUninitializedObject(typeof(TickManager));
        Current.Game.components = fault == "unlisted-network" ? [] : [(GameComponent)network];
        if (fault == "duplicate-network") { Current.Game.components.Add((GameComponent)network); }
        InputStatWorker.Reads = 0;
        InputStatWorker.OnRead = null;
        StatDefOf.MarketValue = new StatDef { defName = "ObservedMarket", workerClass = typeof(InputStatWorker), defaultBaseValue = 675f, minValue = 0f, maxValue = 100000000f };
        var race = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        race.defName = "Mugirl"; race.category = ThingCategory.Pawn;
        var kind = (PawnKindDef)RuntimeHelpers.GetUninitializedObject(typeof(PawnKindDef)); kind.defName = "Mugirl_Slave";
        var pawn = new Pawn { def = race, kindDef = kind, thingIDNumber = 10884 };
        var vault = new ThingOwner<Thing>((IThingHolder)network);
        Set(network, "vault", vault); vault.InnerListForReading.Add(pawn); pawn.holdingOwner = vault;
        Set(offer, "id", 17); Set(offer, "price", 1350); Set(offer, "pawn", pawn); Set(offer, "continuation", completion);
        XmlDocument canonical = Document(Canonical(includePawn: true));
        ObservationMainDocumentFixture.Bind(fault == "side-document" ? Document(canonical.OuterXml) : canonical,
            fault == "wrong-input-path" ? "pricing-side.rws" : "pricing-main.rws");
        XmlElement ownerNode = (XmlElement)canonical.SelectSingleNode("/savegame/game/components/li")!;
        XmlElement offerNode = (XmlElement)ownerNode.SelectSingleNode("people/offers/li")!;
        void ReadAndComplete()
        {
            // Bind callbacks to their actual source shapes; the isolated harness owns Scribe state. Valuation/completion below run real patched methods.
            ReadCallback(network, ownerNode, LoadSaveMode.LoadingVars);
            using var types = new TypeCacheScope(production.GetType(Completion, true)!);
            Scribe.loader = new ScribeLoader { curXmlParent = offerNode, curParent = (IExposable)offer };
            Scribe.mode = LoadSaveMode.LoadingVars;
            ((IExposable)offer).ExposeData();
            ((LoadedObjectDirectory)typeof(CrossRefHandler).GetField("loadedObjectDirectory", Members)!.GetValue(Scribe.loader.crossRefs)!).RegisterLoaded(pawn);
            Scribe.loader.curXmlParent = null;
            Scribe.mode = LoadSaveMode.ResolvingCrossRefs;
            ((IExposable)offer).ExposeData();
            if (fault == "same-id-other-pawn") { Set(offer, "pawn", new Pawn { def = race, kindDef = kind, thingIDNumber = 10884, holdingOwner = vault }); }
            if (fault == "nested-unrelated")
            {
                object unrelated = Activator.CreateInstance(production.GetType(Offer, true)!)!;
                InputStatWorker.OnRead = () => production.GetType(Offer, true)!.GetMethod("Complete", Members)!.Invoke(unrelated, [network]);
            }
            if (fault == "throw-market") { InputStatWorker.OnRead = () => throw new InvalidOperationException("valuation negative control"); }
            if (fault == "factor-changed-by-input") { InputStatWorker.OnRead = () => config.GetType().GetField("basePriceMultiplier")!.SetValue(config, 2f); }
            if (fault is "bound-cross-thread" or "bound-market-cross-thread")
            {
                InputStatWorker.OnRead = () =>
                {
                    InputStatWorker.OnRead = null;
                    OnWorker(() =>
                    {
                        if (fault == "bound-cross-thread") { production.GetType(Network, true)!.GetMethod("PeopleBasePrice")!.Invoke(network, [pawn]); }
                        else { Assert.Equal(675f, pawn.MarketValue); }
                    });
                };
            }
            object?[] loadedArgs = [network, null];
            if (fault != "no-loaded-scope") { Invoke("BeforeLoaded", loadedArgs); }
            try
            {
                Scribe.mode = LoadSaveMode.Inactive;
                void Complete() => production.GetType(Offer, true)!.GetMethod("Complete", Members)!.Invoke(offer, [network]);
                if (fault == "throw-market") { Assert.Throws<TargetInvocationException>(Complete); }
                else { Complete(); }
            }
            finally { if (fault != "no-loaded-scope") { Invoke("LoadedFailed", [null, loadedArgs[1]]); } InputStatWorker.OnRead = null; }
            }
        if (fault == "read-worker")
        {
            OnWorker(ReadAndComplete);
            Assert.Equal(1, InputStatWorker.Reads);
            // LongEvent reads/prices on its worker, while the later save and Snapshot run on the main thread.
            // An ordinary getter outside the completed scope must not become a pricing event or thread error.
            Assert.Equal(675f, pawn.MarketValue);
        }
        else { ReadAndComplete(); }
        if (fault == "late-extra-hook") { harmony.Patch(production.GetType(Network, true)!.GetMethod("PeopleBasePrice")!, postfix: new HarmonyMethod(typeof(CorporatePriceObservationTests), nameof(NoOp))); }
        if (fault == "duplicate-observer-hook") { harmony.Patch(typeof(Thing).GetProperty(nameof(Thing.MarketValue))!.GetMethod!, postfix: new HarmonyMethod(typeof(CorporatePriceObservation).GetMethod("AfterMarket", Members)!) { priority = Priority.Last }); }
        if (fault != "throw-market")
        {
            Scribe.mode = LoadSaveMode.Inactive;
            string actualSave = Scribe.saver.DebugOutputFor((IExposable)offer);
            Assert.Contains(fault == "factor-changed-by-input" ? "<price>1350</price>" : "<price>1013</price>", actualSave, StringComparison.Ordinal);
        }
        string report = CorporatePriceObservation.SnapshotJson();
        if (fault is not ("none" or "nested-unrelated" or "factor-changed-by-input" or "read-worker"))
        {
            JsonArray errors = JsonNode.Parse(report)!["errors"]!.AsArray();
            Assert.NotEmpty(errors);
            if (fault == "bound-cross-thread")
            { Assert.Contains(errors, e => e!.GetValue<string>().Contains("bound pricing call crossed", StringComparison.Ordinal)); }
            if (fault == "bound-market-cross-thread")
            { Assert.Contains(errors, e => e!.GetValue<string>().Contains("bound MarketValue call crossed", StringComparison.Ordinal)); }
            Assert.DoesNotContain(errors, e => e!.GetValue<string>().Contains("scope did not unwind", StringComparison.Ordinal));
            Assert.Throws<InvalidOperationException>(() => CorporatePriceObservation.Apply(XDocument.Parse(Canonical()), report));
            return;
        }
        Assert.Empty(JsonNode.Parse(report)!["errors"]!.AsArray());
        JsonObject item = Assert.Single(JsonNode.Parse(report)!["events"]!.AsArray())!.AsObject();
        int expectedPrice = fault == "factor-changed-by-input" ? 1350 : 1013;
        Assert.Equal(expectedPrice, (int?)item["expectedPrice"]);
        Assert.Equal(BitConverter.SingleToInt32Bits(675f), (int?)item["marketBits"]);
        Assert.Equal(expectedPrice, offer.GetType().GetField("price")!.GetValue(offer));
        Assert.Null(offer.GetType().GetField("continuation", Members)!.GetValue(offer));
        Assert.Equal(fault == "read-worker" ? 2 : 1, InputStatWorker.Reads);
        Assert.True(engineErrors.Count == 0, string.Join("\n", engineErrors));
    }

    public sealed class InputStatWorker : StatWorker
    {
        internal static int Reads;
        internal static Action? OnRead;
        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true) { Reads++; OnRead?.Invoke(); return 675f; }
    }

    private sealed class TypeCacheScope : IDisposable
    {
        private readonly IDictionary cache = (IDictionary)typeof(GenTypes).GetField("typeCache", Members)!.GetValue(null)!;
        private readonly List<DictionaryEntry> saved = [];
        internal TypeCacheScope(Type type)
        {
            foreach (DictionaryEntry item in cache) { saved.Add(item); }
            Type key = cache.GetType().GetGenericArguments()[0];
            ConstructorInfo ctor = key.GetConstructors(Members).Single(c => c.GetParameters().Length == 2);
            foreach (string? ns in new string?[] { null, "Verse", "Mugirl.Features.Corporate" })
            { cache[ctor.Invoke([type.FullName!, ns])] = type; }
        }
        public void Dispose()
        {
            cache.Clear();
            foreach (DictionaryEntry item in saved) { cache.Add(item.Key, item.Value); }
        }
    }

    private static void ReadCallback(object instance, XmlElement node, LoadSaveMode mode)
    {
        Scribe.loader = new ScribeLoader { curXmlParent = node, curParent = (IExposable)instance };
        Scribe.mode = mode;
        object?[] args = [instance, null]; Invoke("BeforeExpose", args);
        Invoke("AfterExpose", [args[1], true]);
    }
    private static object? Invoke(string name, object?[] args)
        => typeof(CorporatePriceObservation).GetMethod(name, Members)!.Invoke(null, args);
    private static void Set(object instance, string field, object value) => instance.GetType().GetField(field, Members)!.SetValue(instance, value);
    private static void OnWorker(Action action)
    {
        Exception? failure = null;
        var worker = new System.Threading.Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        worker.Start(); worker.Join();
        if (failure is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw(); }
    }
    private static XmlDocument Document(string xml) { var document = new XmlDocument(); document.LoadXml(xml); return document; }
    private static string Original() => "<savegame><game><components><li Class=\"Mugirl.CorporateNetwork\"><corporateVault><innerList><li Class=\"Pawn\"><def>Mugirl</def><id>Mugirl10884</id><kindDef>Mugirl_Slave</kindDef></li></innerList></corporateVault><corporatePeopleOffers><li><id>17</id><pawn>Thing_Mugirl10884</pawn><price>1350</price></li></corporatePeopleOffers></li></components></game></savegame>";
    private static string Canonical(bool includePawn = false) => "<savegame><game><components><li Class=\"" + Network + "\">"
        + (includePawn ? "<vault><innerList><li Class=\"Pawn\"><def>Mugirl</def><id>Mugirl10884</id><kindDef>Mugirl_Slave</kindDef></li></innerList></vault>" : "")
        + "<people><offers><li><id>17</id><pawn>Thing_Mugirl10884</pawn><price>1350</price><continuation Class=\"" + Completion + "\"/></li></offers></people></li></components></game></savegame>";
    private static JsonObject Evidence(XDocument source)
    {
        XElement offer = source.Descendants("offers").Single().Element("li")!;
        var before = new JsonObject();
        foreach (string field in new[] { "id", "pawn", "price", "paid", "paidAmount", "delivered", "buyAsColonist", "transferApplied", "continuation" })
        { before[field] = offer.Element(field)?.ToString(SaveOptions.DisableFormatting); }
        return new JsonObject { ["format"] = 1, ["gameMvid"] = ReviewedEngine.Mvid.ToString(), ["errors"] = new JsonArray(),
            ["events"] = new JsonArray(new JsonObject { ["kind"] = "CorporateOfferPriceCompletion", ["path"] = "/savegame[1]/game[1]/components[1]/li[1]/people[1]/offers[1]/li[1]",
                ["offerId"] = 17, ["pawnId"] = "Thing_Mugirl10884", ["before"] = before, ["marketBits"] = BitConverter.SingleToInt32Bits(675f),
                ["multiplierBits"] = BitConverter.SingleToInt32Bits(1.5f), ["expectedPrice"] = 1013, ["expectedReceipt"] = 0, ["completed"] = true, ["saved"] = true }) };
    }
}
