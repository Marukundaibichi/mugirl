using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using HarmonyLib;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

[Collection("A5 real Scribe")]
public sealed class VanillaObservationTests : IDisposable
{
    private readonly ScribeLoader loader = Scribe.loader;
    private readonly LoadSaveMode mode = Scribe.mode;
    private readonly Harmony harmony = new(ValidationContext.OwnHarmonyId);
    private readonly ObservationMainDocumentFixture mainDocument = new();

    public VanillaObservationTests()
    {
        typeof(VanillaReadObservation).GetMethod("Start", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [harmony, typeof(global::Mugirl.Features.Milk.CompMilkProduction).Assembly, null]);
    }

    public void Dispose()
    {
        VanillaReadObservation.Stop();
        harmony.UnpatchAll(ValidationContext.OwnHarmonyId);
        Scribe.loader = loader;
        Scribe.mode = mode;
        mainDocument.Dispose();
    }

    [Theory]
    [InlineData("")]
    [InlineData("<currentPsyfocus>-0.0001</currentPsyfocus>")]
    [InlineData("<currentPsyfocus>-1,000</currentPsyfocus>")]
    public void ActualOriginalPsyfocusInitializationIsBoundToSourceAndG9Save(string field)
    {
        (XmlDocument source, Pawn pawn) = Source("psychicEntropy", field);
        var tracker = new Pawn_PsychicEntropyTracker(pawn);
        Read(source, tracker, "psychicEntropy");
        Rand.PushState(7561);
        try
        {
            Scribe.mode = LoadSaveMode.PostLoadInit;
            tracker.ExposeData();
        }
        finally { Rand.PopState(); }
        XElement saved = Write(tracker);
        string evidence = VanillaReadObservation.SnapshotJson();
        JsonObject trace = JsonNode.Parse(evidence)!.AsObject();
        Assert.Empty(trace["errors"]!.AsArray());
        JsonNode entry = Assert.Single(trace["events"]!.AsArray())!;
        Assert.Equal(saved.Element("currentPsyfocus")!.Value, (string?)entry["newValue"]);
        XDocument expected = XDocument.Parse(source.OuterXml);
        Assert.Equal(1, VanillaReadEvidence.Apply(expected, evidence));
        Assert.Equal(saved.Element("currentPsyfocus")!.Value, expected.Descendants("currentPsyfocus").Single().Value);
        var corrupted = JsonNode.Parse(evidence)!.AsObject();
        int bits = corrupted["events"]![0]!["newFloatBits"]!.GetValue<int>();
        corrupted["events"]![0]!["newValue"] = BitConverter.Int32BitsToSingle(bits + 1).ToString("G9", CultureInfo.InvariantCulture);
        Assert.Throws<InvalidOperationException>(() => VanillaReadEvidence.Apply(XDocument.Parse(source.OuterXml), corrupted.ToJsonString()));
    }

    [Theory]
    [InlineData("<currentPsyfocus IsNull=\"True\" />")]
    [InlineData("<currentPsyfocus>0</currentPsyfocus>")]
    [InlineData("<currentPsyfocus>-0</currentPsyfocus>")]
    [InlineData("<currentPsyfocus>NaN</currentPsyfocus>")]
    [InlineData("<currentPsyfocus>0.570886374</currentPsyfocus>")]
    public void NonNegativeOrNullSourceDoesNotAuthorizeInitialization(string field)
    {
        (XmlDocument source, Pawn pawn) = Source("psychicEntropy", field);
        var tracker = new Pawn_PsychicEntropyTracker(pawn);
        Read(source, tracker, "psychicEntropy");
        Scribe.mode = LoadSaveMode.PostLoadInit;
        tracker.ExposeData();
        _ = Write(tracker);
        JsonObject trace = JsonNode.Parse(VanillaReadObservation.SnapshotJson())!.AsObject();
        Assert.Empty(trace["errors"]!.AsArray());
        Assert.Empty(trace["events"]!.AsArray());
    }

    [Fact]
    public void OneUlpMutationAfterInitializationFailsObservation()
    {
        (XmlDocument source, Pawn pawn) = Source("psychicEntropy", string.Empty);
        var tracker = new Pawn_PsychicEntropyTracker(pawn);
        Read(source, tracker, "psychicEntropy");
        Scribe.mode = LoadSaveMode.PostLoadInit;
        tracker.ExposeData();
        FieldInfo value = typeof(Pawn_PsychicEntropyTracker).GetField("currentPsyfocus", BindingFlags.NonPublic | BindingFlags.Instance)!;
        int bits = BitConverter.SingleToInt32Bits((float)value.GetValue(tracker)!);
        value.SetValue(tracker, BitConverter.Int32BitsToSingle(bits + 1));
        _ = Write(tracker);
        string evidence = VanillaReadObservation.SnapshotJson();
        Assert.NotEmpty(JsonNode.Parse(evidence)!["errors"]!.AsArray());
        Assert.Throws<InvalidOperationException>(() => VanillaReadEvidence.Apply(XDocument.Parse(source.OuterXml), evidence));
    }

    [Fact]
    public void ObservationConsumesNoAdditionalRandomNumbers()
    {
        typeof(VanillaReadObservation).GetField("active", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);
        (float value, float next) baseline = Run();
        typeof(VanillaReadObservation).GetMethod("Start", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [harmony, typeof(global::Mugirl.Features.Milk.CompMilkProduction).Assembly, null]);
        (float value, float next) observed = Run();
        Assert.Equal(BitConverter.SingleToInt32Bits(baseline.value), BitConverter.SingleToInt32Bits(observed.value));
        Assert.Equal(BitConverter.SingleToInt32Bits(baseline.next), BitConverter.SingleToInt32Bits(observed.next));
        Assert.Empty(JsonNode.Parse(VanillaReadObservation.SnapshotJson())!["errors"]!.AsArray());

        static (float, float) Run()
        {
            (XmlDocument source, Pawn pawn) = Source("psychicEntropy", string.Empty);
            var tracker = new Pawn_PsychicEntropyTracker(pawn);
            Rand.PushState(121397);
            try
            {
                Read(source, tracker, "psychicEntropy");
                Scribe.mode = LoadSaveMode.PostLoadInit;
                tracker.ExposeData();
                _ = Write(tracker);
                return (tracker.CurrentPsyfocus, Rand.Value);
            }
            finally { Rand.PopState(); }
        }
    }

    [Fact]
    public void ActualApparelReferenceResolutionExplainsOnlyItsNullList()
    {
        (XmlDocument source, Pawn pawn) = Source("apparel", "<wornApparel><innerList /></wornApparel><lockedApparel IsNull=\"True\" />");
        var tracker = new Pawn_ApparelTracker(pawn);
        Read(source, tracker, "apparel");
        Scribe.mode = LoadSaveMode.ResolvingCrossRefs;
        tracker.ExposeData();
        _ = Write(tracker);
        string evidence = VanillaReadObservation.SnapshotJson();
        JsonObject trace = JsonNode.Parse(evidence)!.AsObject();
        Assert.True(trace["errors"]!.AsArray().Count == 0, evidence);
        Assert.Single(trace["events"]!.AsArray());
        XDocument expected = XDocument.Parse(source.OuterXml);
        Assert.Equal(1, VanillaReadEvidence.Apply(expected, evidence));
        Assert.False(expected.Descendants("lockedApparel").Single().HasAttributes);
        source.SelectSingleNode("//lockedApparel")!.InnerText = "player text";
        Assert.Throws<InvalidOperationException>(() => VanillaReadEvidence.Apply(XDocument.Parse(source.OuterXml), evidence));
    }

    [Fact]
    public void ActualGuardedProductionPostfixCoexistsWithSourceBoundObservation()
    {
        Type type = typeof(Mugirl.Features.Restraints.Harmony_Pawn_ApparelTracker_ExposeData);
        MethodInfo target = typeof(Pawn_ApparelTracker).GetMethod(nameof(Pawn_ApparelTracker.ExposeData))!;
        var production = new Harmony("MugirlMod.Mod");
        production.CreateClassProcessor(type).Patch();
        try
        {
            typeof(VanillaReadObservation).GetMethod("Start", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [harmony, typeof(global::Mugirl.Features.Milk.CompMilkProduction).Assembly, null]);
            (XmlDocument source, Pawn pawn) = Source("apparel", "<wornApparel><innerList /></wornApparel><lockedApparel IsNull=\"True\" />");
            var tracker = new Pawn_ApparelTracker(pawn);
            pawn.apparel = tracker;
            Read(source, tracker, "apparel");
            Scribe.mode = LoadSaveMode.ResolvingCrossRefs;
            tracker.ExposeData();
            Scribe.mode = LoadSaveMode.PostLoadInit;
            tracker.ExposeData();
            _ = Write(tracker);
            string evidence = VanillaReadObservation.SnapshotJson();
            JsonObject trace = JsonNode.Parse(evidence)!.AsObject();
            Assert.True(trace["errors"]!.AsArray().Count == 0, evidence);
            Assert.Single(trace["events"]!.AsArray());
            JsonNode[] records = [.. trace["patches"]!.AsArray().OfType<JsonNode>().Where(p => (string?)p["owner"] == "MugirlMod.Mod")];
            Assert.Equal(2, records.Length);
            Assert.All(records, p => Assert.Contains("PostLoadInit-only", (string?)p["proof"], StringComparison.Ordinal));
            Assert.Equal(1, VanillaReadEvidence.Apply(XDocument.Parse(source.OuterXml), evidence));
        }
        finally { production.Unpatch(target, type.GetMethod("Postfix")!); }
    }

    [Fact]
    public void UnknownPatchFailureIncludesItsActualOwnerMethodRoleAndPriority()
    {
        var unknown = new Harmony("A5.Unknown.Observer.NegativeControl");
        MethodInfo target = typeof(Pawn_ApparelTracker).GetMethod(nameof(Pawn_ApparelTracker.ExposeData))!;
        unknown.Patch(target, prefix: new HarmonyMethod(typeof(VanillaObservationTests), nameof(UnknownNoop)) { priority = Priority.High });
        try
        {
            string evidence = VanillaReadObservation.SnapshotJson();
            JsonObject trace = JsonNode.Parse(evidence)!.AsObject();
            Assert.NotEmpty(trace["errors"]!.AsArray());
            JsonNode patch = Assert.Single(trace["patches"]!.AsArray(), p => (string?)p?["owner"] == unknown.Id)!;
            Assert.Equal("prefix", (string?)patch["role"]);
            Assert.Equal(Priority.High, (int?)patch["priority"]);
            Assert.Contains(nameof(UnknownNoop), (string?)patch["method"], StringComparison.Ordinal);
            Assert.Null(patch["proof"]);
        }
        finally { unknown.UnpatchAll(unknown.Id); }
    }

    private static void UnknownNoop() { }

    [Fact]
    public void AnIdenticalSideDocumentDoesNotBindTheActualMainTracker()
    {
        (XmlDocument source, Pawn pawn) = Source("psychicEntropy", "<currentPsyfocus>-1</currentPsyfocus>");
        var otherMain = new XmlDocument(); otherMain.LoadXml(source.OuterXml);
        ObservationMainDocumentFixture.Bind(otherMain);
        var tracker = new Pawn_PsychicEntropyTracker(pawn);
        Scribe.loader = new ScribeLoader { curXmlParent = source.SelectSingleNode("//psychicEntropy"), curParent = tracker };
        Scribe.mode = LoadSaveMode.LoadingVars;
        tracker.ExposeData();
        Assert.NotEmpty(JsonNode.Parse(VanillaReadObservation.SnapshotJson())!["errors"]!.AsArray());
    }

    [Theory]
    [InlineData("prefix")]
    [InlineData("postfix")]
    [InlineData("finalizer")]
    public void InnerTrackerHooksAreVisibleAndRejected(string role)
    {
        MethodInfo target = typeof(Pawn_PsychicEntropyTracker).GetMethod(nameof(Pawn_PsychicEntropyTracker.ExposeData))!;
        MethodInfo inner = typeof(Pawn_PsychicEntropyTracker).GetMethod(nameof(Pawn_PsychicEntropyTracker.SetInitialPsyfocusLevel))!;
        var other = new Harmony("A5.Vanilla.Inner.Negative");
        try
        {
            var hook = new HarmonyMethod(typeof(VanillaObservationTests), nameof(UnknownNoop)) { innerMethod = new InnerMethod(inner, []) };
            PatchProcessor processor = other.CreateProcessor(target);
            if (role == "prefix") { processor.AddInnerPrefix(hook); } else if (role == "postfix") { processor.AddInnerPostfix(hook); } else { processor.AddInnerFinalizer(hook); }
            processor.Patch();
            JsonObject trace = JsonNode.Parse(VanillaReadObservation.SnapshotJson())!.AsObject();
            Assert.NotEmpty(trace["errors"]!.AsArray());
            Assert.Contains(trace["patches"]!.AsArray(), p => (string?)p?["owner"] == other.Id && (string?)p?["role"] == "inner" + char.ToUpperInvariant(role[0]) + role[1..]);
        }
        finally { other.UnpatchAll(other.Id); }
    }

    [Fact]
    public void UnmatchedOwnerAndDuplicateSourceFailClosed()
    {
        (XmlDocument source, Pawn pawn) = Source("psychicEntropy", "<currentPsyfocus>-1</currentPsyfocus><currentPsyfocus>-2</currentPsyfocus>");
        var tracker = new Pawn_PsychicEntropyTracker(pawn);
        Read(source, tracker, "psychicEntropy", new Pawn_PsychicEntropyTracker(pawn));
        string evidence = VanillaReadObservation.SnapshotJson();
        Assert.NotEmpty(JsonNode.Parse(evidence)!["errors"]!.AsArray());
    }

    private static (XmlDocument, Pawn) Source(string tracker, string fields)
    {
        var source = new XmlDocument { XmlResolver = null, PreserveWhitespace = true };
        source.LoadXml("<savegame><game><things><thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><kindDef>MugirlColonist</kindDef><"
            + tracker + ">" + fields + "</" + tracker + "></thing></things></game></savegame>");
        return (source, new Pawn { def = new ThingDef { defName = "Mugirl" }, kindDef = new PawnKindDef { defName = "MugirlColonist" }, thingIDNumber = 1 });
    }

    private static void Read(XmlDocument source, IExposable value, string label, IExposable? wrongOwner = null)
    {
        ObservationMainDocumentFixture.Bind(source);
        Scribe.loader = new ScribeLoader { curXmlParent = source.SelectSingleNode("//" + label), curParent = wrongOwner ?? value };
        Scribe.mode = LoadSaveMode.LoadingVars;
        value.ExposeData();
    }

    private static XElement Write(IExposable value)
    {
        string path = Path.Combine(Path.GetTempPath(), "mugirl-a5-observe-" + Guid.NewGuid().ToString("N") + ".xml");
        ScribeSaver saver = Scribe.saver;
        LoadSaveMode previous = Scribe.mode;
        try
        {
            Scribe.saver = new ScribeSaver();
            Scribe.mode = LoadSaveMode.Inactive;
            Scribe.saver.InitSaving(path, "snapshot");
            value.ExposeData();
            Scribe.saver.FinalizeSaving();
            return XElement.Load(path, LoadOptions.PreserveWhitespace);
        }
        finally
        {
            if (Scribe.mode == LoadSaveMode.Saving) { Scribe.saver.ForceStop(); }
            Scribe.saver = saver;
            Scribe.mode = previous;
            if (File.Exists(path)) { File.Delete(path); }
        }
    }
}
