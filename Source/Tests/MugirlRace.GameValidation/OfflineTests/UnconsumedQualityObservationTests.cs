using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using HarmonyLib;
using RimWorld.Planet;
using Waylith.UnityCompat;
using Mugirl.Features.Milk;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

[Collection("A5 real Scribe")]
public sealed class UnconsumedQualityObservationTests : IDisposable
{
    private static int unreachableCalls;
    private readonly ScribeLoader loader = Scribe.loader;
    private readonly ScribeSaver saver = Scribe.saver;
    private readonly LoadSaveMode mode = Scribe.mode;
    private readonly Game previousGame = Current.Game;
    private readonly ObservationMainDocumentFixture mainDocument = new();
    private readonly UnityRuntime? previousUnityRuntime = UnityRuntime.Current;
    private static readonly FieldInfo UnityCurrent = typeof(UnityRuntime).GetField("<Current>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly ThingDef[] definitions = [.. DefDatabase<ThingDef>.AllDefsListForReading];
    private readonly Harmony harmony = new(ValidationContext.OwnHarmonyId);
    private readonly FieldInfo modChanged = typeof(ScribeMetaHeaderUtility).GetField("modListChanged", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
    private readonly FieldInfo loadedBuild = typeof(ScribeMetaHeaderUtility).GetField("loadedGameVersionBuild", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
    private readonly object? beforeChanged;
    private readonly object? beforeBuild;
    private readonly string beforeVersion = ScribeMetaHeaderUtility.loadedGameVersion;
    private readonly int beforeMajor = ScribeMetaHeaderUtility.loadedGameVersionMajor;
    private readonly int beforeMinor = ScribeMetaHeaderUtility.loadedGameVersionMinor;

    public UnconsumedQualityObservationTests()
    {
        _ = new ThingDef(); // 先按正常离线fallback初始化BaseContent，随后只替换App.IsEditor的环境状态。
        beforeChanged = modChanged.GetValue(null); beforeBuild = loadedBuild.GetValue(null);
        modChanged.SetValue(null, false); loadedBuild.SetValue(null, VersionControl.CurrentBuild);
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        var world = (World)RuntimeHelpers.GetUninitializedObject(typeof(World));
        world.factionManager = new FactionManager();
        typeof(Game).GetField("worldInt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(game, world);
        typeof(Game).GetField("maps", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(game, new List<Map>());
        Current.Game = game;
        // 离线宿主没有UnityRuntime时，真实Application.isEditor按契约返回true；只配置App状态，不启动任何窗口或引擎循环。
        var runtime = (UnityRuntime)RuntimeHelpers.GetUninitializedObject(typeof(UnityRuntime));
        typeof(UnityRuntime).GetField("<App>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtime, new UnityAppState { IsEditor = false });
        UnityCurrent.SetValue(null, runtime);
    }

    public void Dispose()
    {
        UnconsumedQualityObservation.Stop(); harmony.UnpatchAll(harmony.Id);
        Scribe.loader = loader; Scribe.saver = saver; Scribe.mode = mode;
        Current.Game = previousGame;
        UnityCurrent.SetValue(null, previousUnityRuntime);
        mainDocument.Dispose();
        modChanged.SetValue(null, beforeChanged); loadedBuild.SetValue(null, beforeBuild);
        AccessTools.Field(typeof(ScribeMetaHeaderUtility), "loadedGameVersion").SetValue(null, beforeVersion);
        AccessTools.Field(typeof(ScribeMetaHeaderUtility), "loadedGameVersionMajor").SetValue(null, beforeMajor);
        AccessTools.Field(typeof(ScribeMetaHeaderUtility), "loadedGameVersionMinor").SetValue(null, beforeMinor);
        DefDatabase<ThingDef>.Clear(); foreach (ThingDef definition in definitions) { DefDatabase<ThingDef>.Add(definition); }
    }

    [Fact]
    public void ActualOriginalReaderAndConverterGateProveTheSingleUnusedLeaf()
    {
        (XDocument source, Apparel apparel) = Setup();
        XElement saved = Roundtrip(source, apparel);
        Assert.Null(saved.Element("quality"));
        string json = UnconsumedQualityObservation.SnapshotJson();
        Assert.True(JsonNode.Parse(json)!["errors"]!.AsArray().Count == 0, json);
        Assert.Equal(1, UnconsumedQualityObservation.Apply(source, json));
        Assert.Empty(source.Descendants("quality"));
    }

    [Fact]
    public void ActualAutoloadingApparelHasOnlyTheReviewedExtraComp()
    {
        (XDocument source, Apparel apparel) = Setup(autoloading: true);
        XElement saved = Roundtrip(source, apparel);
        Assert.Null(saved.Element("quality"));
        string json = UnconsumedQualityObservation.SnapshotJson();
        Assert.True(JsonNode.Parse(json)!["errors"]!.AsArray().Count == 0, json);
        Assert.Equal(1, UnconsumedQualityObservation.Apply(source, json));
    }

    [Fact]
    public void OnlyActiveExactRegisteredCallbacksReceiveAReadOnlyProof()
    {
        _ = Setup();
        DuplicateForbiddableEvidence.Begin(); // 同时启用的精确只读观察器可共存，未知同owner补丁仍必须拒绝。
        MethodInfo converter = (MethodInfo)ReviewedEngine.ResolveMethod(0x060034A1)!;
        Patches patches = Harmony.GetPatchInfo(converter)!;
        Assert.NotNull(UnconsumedQualityObservation.ProveRegisteredHook(converter, Assert.Single(patches.Prefixes), "prefix"));
        Assert.NotNull(UnconsumedQualityObservation.ProveRegisteredHook(converter, Assert.Single(patches.Postfixes), "postfix"));
        Assert.NotNull(UnconsumedQualityObservation.ProveRegisteredHook(converter, Assert.Single(patches.Finalizers), "finalizer"));
        Assert.Null(UnconsumedQualityObservation.ProveRegisteredHook(converter, Assert.Single(patches.Postfixes), "prefix"));
        harmony.Patch(converter, prefix: new HarmonyMethod(typeof(UnconsumedQualityObservationTests), nameof(Noop)));
        Patch unknown = Harmony.GetPatchInfo(converter)!.Prefixes.Single(p => p.PatchMethod.DeclaringType == typeof(UnconsumedQualityObservationTests));
        Assert.Null(UnconsumedQualityObservation.ProveRegisteredHook(converter, unknown, "prefix"));
    }

    [Theory]
    [InlineData("prefix")]
    [InlineData("postfix")]
    [InlineData("finalizer")]
    public void AnInnerHookOnASourceIdentityHelperRevokesTheObserverProof(string role)
    {
        _ = Setup();
        MethodInfo converter = (MethodInfo)ReviewedEngine.ResolveMethod(0x060034A1)!;
        Patch callback = Assert.Single(Harmony.GetPatchInfo(converter)!.Postfixes);
        Assert.NotNull(UnconsumedQualityObservation.ProveRegisteredHook(converter, callback, "postfix"));
        MethodInfo target = typeof(MainSaveDocument).GetMethod(nameof(MainSaveDocument.Matches))!;
        MethodInfo inner = new CurrentScribeRoots(typeof(CompMilkProduction).Assembly).DirectCalls(target).First(m => m.Name == nameof(Path.GetFullPath));
        var other = new Harmony("A5.Quality.IdentityHelper.Inner");
        try
        {
            var hook = new HarmonyMethod(typeof(UnconsumedQualityObservationTests), nameof(Noop)) { innerMethod = new InnerMethod(inner, []) };
            PatchProcessor processor = other.CreateProcessor(target);
            if (role == "prefix") { processor.AddInnerPrefix(hook); } else if (role == "postfix") { processor.AddInnerPostfix(hook); } else { processor.AddInnerFinalizer(hook); }
            processor.Patch();
            Assert.Null(UnconsumedQualityObservation.ProveRegisteredHook(converter, callback, "postfix"));
        }
        finally { other.UnpatchAll(other.Id); }
    }

    [Theory]
    [InlineData("CanonicalTypeName")]
    [InlineData("ResolveMethod")]
    [InlineData("get_Mvid")]
    public void PatchingAnEngineIdentityHelperRevokesTheObserverProof(string name)
    {
        _ = Setup();
        MethodInfo converter = (MethodInfo)ReviewedEngine.ResolveMethod(0x060034A1);
        Patch callback = Assert.Single(Harmony.GetPatchInfo(converter)!.Postfixes);
        Assert.NotNull(UnconsumedQualityObservation.ProveRegisteredHook(converter, callback, "postfix"));
        var other = new Harmony("A5.Quality.EngineHelper");
        try
        {
            other.Patch(typeof(ReviewedEngine).GetMethod(name)!, prefix: new HarmonyMethod(typeof(UnconsumedQualityObservationTests), nameof(Noop)));
            Assert.Null(UnconsumedQualityObservation.ProveRegisteredHook(converter, callback, "postfix"));
        }
        finally { other.UnpatchAll(other.Id); }
    }

    [Theory]
    [InlineData("Good")]
    [InlineData(" Normal ")]
    public void OtherValuesAreNotInterpretedAsTheHistoricalUnusedNormal(string value)
    {
        (XDocument source, Apparel apparel) = Setup(value: value);
        _ = Roundtrip(source, apparel);
        string json = UnconsumedQualityObservation.SnapshotJson();
        Assert.Equal(0, UnconsumedQualityObservation.Apply(source, json));
        Assert.Equal(value, source.Descendants("quality").Single().Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void QualityOrUnknownAdditionalCompNeverAuthorizesFieldLoss(bool quality)
    {
        (XDocument source, Apparel apparel) = Setup(extra: quality ? typeof(CompQuality) : typeof(UnknownComp));
        _ = Roundtrip(source, apparel);
        string json = UnconsumedQualityObservation.SnapshotJson();
        Assert.NotEmpty(JsonNode.Parse(json)!["errors"]!.AsArray());
        Assert.Throws<InvalidOperationException>(() => UnconsumedQualityObservation.Apply(source, json));
        Assert.Single(source.Descendants("quality"));
    }

    [Fact]
    public void NativeConverterEnabledIsNotAssumedToIgnoreQuality()
    {
        (XDocument source, Apparel apparel) = Setup();
        modChanged.SetValue(null, true);
        _ = Roundtrip(source, apparel);
        string json = UnconsumedQualityObservation.SnapshotJson();
        Assert.NotEmpty(JsonNode.Parse(json)!["errors"]!.AsArray());
        Assert.Throws<InvalidOperationException>(() => UnconsumedQualityObservation.Apply(source, json));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("duplicate")]
    [InlineData("removed")]
    [InlineData("predicate-patch")]
    [InlineData("universal-patch")]
    [InlineData("unreachable-body-patch")]
    [InlineData("wrong-source")]
    [InlineData("wrong-build")]
    public void CrossBuildApparelRequiresTheExactSourceAndUnmodifiedNativeChain(string fault)
    {
        unreachableCalls = 0;
        (XDocument source, Apparel apparel) = Setup();
        XElement meta = source.Root!.Element("meta")!;
        meta.SetElementValue("gameVersion", fault == "wrong-source" ? "1.7.4978 rev1" : "1.7.4979 rev109");
        AccessTools.Field(typeof(ScribeMetaHeaderUtility), "loadedGameVersion").SetValue(null, "1.7.4979 rev109");
        AccessTools.Field(typeof(ScribeMetaHeaderUtility), "loadedGameVersionMajor").SetValue(null, 1);
        AccessTools.Field(typeof(ScribeMetaHeaderUtility), "loadedGameVersionMinor").SetValue(null, 7);
        loadedBuild.SetValue(null, fault == "wrong-build" ? 4978 : 4979);
        var chain = (List<BackCompatibilityConverter>)AccessTools.Field(typeof(BackCompatibility), "conversionChain").GetValue(null)!;
        BackCompatibilityConverter[] original = [.. chain];
        var other = new Harmony("A5.Quality.NativeConverter");
        try
        {
            if (fault == "duplicate") { chain.Add(chain[0]); }
            if (fault == "removed") { chain.RemoveAt(0); }
            if (fault is "predicate-patch" or "universal-patch")
            {
                MethodInfo method = fault == "predicate-patch"
                    ? AccessTools.Method(typeof(BackCompatibilityConverter_1_5), nameof(BackCompatibilityConverter.AppliesToVersion))
                    : AccessTools.Method(typeof(BackCompatibilityConverter_Universal), nameof(BackCompatibilityConverter.PostExposeData));
                other.Patch(method, prefix: new HarmonyMethod(typeof(UnconsumedQualityObservationTests), nameof(Noop)));
            }
            if (fault == "unreachable-body-patch")
            {
                other.Patch(AccessTools.Method(typeof(BackCompatibilityConverter_0_18), nameof(BackCompatibilityConverter.PostExposeData)),
                    prefix: new HarmonyMethod(typeof(UnconsumedQualityObservationTests), nameof(UnreachableConverter)));
            }
            XElement saved = Roundtrip(source, apparel);
            string json = UnconsumedQualityObservation.SnapshotJson();
            if (fault is "none" or "unreachable-body-patch")
            {
                Assert.Equal(0, unreachableCalls);
                Assert.Null(saved.Element("quality"));
                Assert.True(JsonNode.Parse(json)!["errors"]!.AsArray().Count == 0, json);
                Assert.False((bool)JsonNode.Parse(json)!["events"]![0]!["converterSkipped"]!);
                Assert.Equal(1, UnconsumedQualityObservation.Apply(new XDocument(source), json));
                XDocument wrong = new(source);
                wrong.Root!.Element("meta")!.SetElementValue("gameVersion", "1.7.4980 rev60");
                Assert.Throws<InvalidOperationException>(() => UnconsumedQualityObservation.Apply(wrong, json));
            }
            else
            {
                Assert.NotEmpty(JsonNode.Parse(json)!["errors"]!.AsArray());
                Assert.Throws<InvalidOperationException>(() => UnconsumedQualityObservation.Apply(source, json));
            }
        }
        finally { other.UnpatchAll(other.Id); chain.Clear(); chain.AddRange(original); }
    }

    [Fact]
    public void WrongPathOwnerValueAndRosterCannotReuseAnObservation()
    {
        (XDocument source, Apparel apparel) = Setup();
        _ = Roundtrip(source, apparel);
        string json = UnconsumedQualityObservation.SnapshotJson();
        foreach (string field in new[] { "path", "id", "def", "oldValue", "comps" })
        {
            JsonObject changed = JsonNode.Parse(json)!.AsObject();
            JsonNode row = changed["events"]![0]!;
            if (field == "comps") { row[field] = new JsonArray("RimWorld.CompQuality"); }
            else { row[field] = field == "path" ? "/savegame[1]/meta[1]" : "wrong"; }
            Assert.Throws<InvalidOperationException>(() => UnconsumedQualityObservation.Apply(new XDocument(source), changed.ToJsonString()));
        }
        XDocument nondefault = new(source);
        nondefault.Descendants("quality").Single().Value = "Excellent";
        Assert.Throws<InvalidOperationException>(() => UnconsumedQualityObservation.Apply(nondefault, json));
    }

    [Fact]
    public void MissingSaveCallbackOrMutatedDefinitionFailsEvidence()
    {
        (XDocument source, Apparel apparel) = Setup();
        Read(source, apparel);
        string json = UnconsumedQualityObservation.SnapshotJson();
        Assert.NotEmpty(JsonNode.Parse(json)!["errors"]!.AsArray());
        Assert.Throws<InvalidOperationException>(() => UnconsumedQualityObservation.Apply(source, json));
    }

    [Fact]
    public void AnUnknownReaderPatchIsReportedBeforeItCanAuthorizeAnything()
    {
        var other = new Harmony("A5.Quality.UnknownReader");
        other.Patch(typeof(Apparel).GetMethod(nameof(Apparel.ExposeData))!, prefix: new HarmonyMethod(typeof(UnconsumedQualityObservationTests), nameof(Noop)));
        try { Assert.Throws<InvalidOperationException>(() => Setup()); }
        finally { other.UnpatchAll(other.Id); }
    }

    [Fact]
    public void IdenticalSideDocumentCannotBecomeTheMainSource()
    {
        (XDocument source, Apparel apparel) = Setup();
        var main = new XmlDocument { XmlResolver = null }; main.LoadXml(source.ToString());
        _ = Roundtrip(source, apparel, main);
        string json = UnconsumedQualityObservation.SnapshotJson();
        Assert.NotEmpty(JsonNode.Parse(json)!["errors"]!.AsArray());
        Assert.Throws<InvalidOperationException>(() => UnconsumedQualityObservation.Apply(source, json));
    }

    [Fact]
    public void TheActualLoadingThreadMayDifferFromTheLaterSavingThread()
    {
        (XDocument source, Apparel apparel) = Setup();
        OnThread(() => Read(source, apparel));
        _ = Save(apparel);
        string json = UnconsumedQualityObservation.SnapshotJson();
        JsonNode evidence = JsonNode.Parse(json)!;
        Assert.True(evidence["errors"]!.AsArray().Count == 0, json);
        Assert.NotEqual((int?)evidence["loadThread"], (int?)evidence["saveThread"]);
        Assert.Equal(Environment.CurrentManagedThreadId, (int?)evidence["saveThread"]);
        Assert.Equal(1, UnconsumedQualityObservation.Apply(source, json));
    }

    [Fact]
    public void AnAlreadyBoundReadPhaseCannotMoveToAnotherThread()
    {
        (XDocument source, Apparel apparel) = Setup();
        OnThread(() => Read(source, apparel, phases: [LoadSaveMode.LoadingVars]));
        Scribe.mode = LoadSaveMode.ResolvingCrossRefs;
        apparel.ExposeData();
        string json = UnconsumedQualityObservation.SnapshotJson();
        Assert.Contains(JsonNode.Parse(json)!["errors"]!.AsArray(), error => error!.GetValue<string>().Contains("actual loading thread", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => UnconsumedQualityObservation.Apply(source, json));
    }

    [Fact]
    public void ARepeatedSavingPhaseOnAnotherThreadIsRejected()
    {
        (XDocument source, Apparel apparel) = Setup();
        Read(source, apparel);
        OnThread(() => Save(apparel));
        _ = Save(apparel);
        string json = UnconsumedQualityObservation.SnapshotJson();
        Assert.NotEmpty(JsonNode.Parse(json)!["errors"]!.AsArray());
        Assert.Throws<InvalidOperationException>(() => UnconsumedQualityObservation.Apply(source, json));
    }

    [Theory]
    [InlineData("initialize")]
    [InlineData("constructor")]
    [InlineData("getter")]
    public void InitializationAndGetterHooksCannotHideBehindAnUnchangedPostExposeData(string kind)
    {
        MethodBase method = kind switch
        {
            "initialize" => typeof(ThingComp).GetMethod(nameof(ThingComp.Initialize))!,
            "constructor" => typeof(CompForbiddable).GetConstructor(Type.EmptyTypes)!,
            _ => typeof(Thing).GetProperty(nameof(Thing.ThingID))!.GetMethod!,
        };
        var other = new Harmony("A5.Quality.Initialization.Negative");
        other.Patch(method, prefix: new HarmonyMethod(typeof(UnconsumedQualityObservationTests), nameof(Noop)));
        try { Assert.Throws<InvalidOperationException>(() => Setup()); }
        finally { other.UnpatchAll(other.Id); }
    }

    [Theory]
    [InlineData("prefix")]
    [InlineData("postfix")]
    [InlineData("finalizer")]
    public void InnerReaderHooksAreRejected(string role)
    {
        MethodInfo target = typeof(ThingWithComps).GetMethod(nameof(ThingWithComps.InitializeComps))!;
        MethodInfo inner = new CurrentScribeRoots(typeof(CompMilkProduction).Assembly).DirectCalls(target).First(m => m.DeclaringType == typeof(Activator));
        var other = new Harmony("A5.Quality.Inner.Negative");
        try
        {
            var hook = new HarmonyMethod(typeof(UnconsumedQualityObservationTests), nameof(Noop)) { innerMethod = new InnerMethod(inner, []) };
            PatchProcessor processor = other.CreateProcessor(target);
            if (role == "prefix") { processor.AddInnerPrefix(hook); } else if (role == "postfix") { processor.AddInnerPostfix(hook); } else { processor.AddInnerFinalizer(hook); }
            processor.Patch();
            Assert.Throws<InvalidOperationException>(() => Setup());
        }
        finally { other.UnpatchAll(other.Id); }
    }

    private (XDocument, Apparel) Setup(string value = "Normal", Type? extra = null, bool autoloading = false)
    {
        string name = autoloading ? "Mugirl_AutoloadingSystem" : "Mugirl_RA_MechanicBackpack";
        var definition = new ThingDef { defName = name, thingClass = typeof(Apparel), category = ThingCategory.Item,
            useHitPoints = false, apparel = new ApparelProperties(), comps = [new CompProperties_Forbiddable(), new CompProperties { compClass = typeof(CompColorable) }, new CompProperties_Styleable()] };
        if (autoloading) { definition.comps.Add(new CompProperties { compClass = typeof(Mugirl.Features.WeaponWheel.CompAutoloadingSystem) }); }
        if (extra is not null) { definition.comps.Add(new CompProperties { compClass = extra }); }
        DefDatabase<ThingDef>.Clear(); DefDatabase<ThingDef>.Add(definition);
        var apparel = new Apparel { def = definition, thingIDNumber = 1 };
        XDocument source = Saves.Game("<things><thing><def>" + name + "</def><id>" + name + "1</id><quality>" + value + "</quality></thing></things>");
        UnconsumedQualityObservation.Start(typeof(CompMilkProduction).Assembly, harmony, source);
        return (source, apparel);
    }

    private static void Read(XDocument source, Apparel apparel, XmlDocument? otherMain = null, LoadSaveMode[]? phases = null)
    {
        var xml = new XmlDocument { XmlResolver = null, PreserveWhitespace = true };
        xml.LoadXml(source.ToString(SaveOptions.DisableFormatting));
        ObservationMainDocumentFixture.Bind(otherMain ?? xml);
        Scribe.loader = new ScribeLoader { curXmlParent = xml.SelectSingleNode("//thing"), curParent = apparel };
        foreach (LoadSaveMode phase in phases ?? [LoadSaveMode.LoadingVars, LoadSaveMode.ResolvingCrossRefs, LoadSaveMode.PostLoadInit])
        { Scribe.mode = phase; apparel.ExposeData(); }
    }

    private static XElement Roundtrip(XDocument source, Apparel apparel, XmlDocument? otherMain = null)
    {
        Read(source, apparel, otherMain);
        return Save(apparel);
    }

    private static XElement Save(Apparel apparel)
    {
        string path = Path.Combine(Path.GetTempPath(), "mugirl-quality-proof-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            Scribe.saver = new ScribeSaver(); Scribe.mode = LoadSaveMode.Inactive;
            Scribe.saver.InitSaving(path, "snapshot"); apparel.ExposeData(); Scribe.saver.FinalizeSaving();
            return XElement.Load(path);
        }
        finally { if (Scribe.mode == LoadSaveMode.Saving) { Scribe.saver.ForceStop(); } if (File.Exists(path)) { File.Delete(path); } }
    }

    private static void OnThread(Action action)
    {
        Exception? failure = null;
        var worker = new System.Threading.Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        worker.Start(); worker.Join();
        if (failure is not null) { throw new InvalidOperationException("Natural worker-thread Scribe call failed.", failure); }
    }

    private static void Noop() { }
    private static void UnreachableConverter()
    {
        unreachableCalls++;
        throw new InvalidOperationException("An inapplicable converter must never run.");
    }
    public sealed class UnknownComp : ThingComp;
}
