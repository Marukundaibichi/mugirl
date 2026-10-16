using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using HarmonyLib;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

[Collection("A5 real Scribe")]
public sealed class DuplicateForbiddableTests
{
    private const string Name = DuplicateForbiddableEvidence.Definition;
    private const string Id = Name + "1";
    private static XDocument Save(string fields, string name = Name) => Saves.Game("<maps><li><uniqueID>0</uniqueID><things><thing Class=\"Apparel\"><def>" + name + "</def><id>" + Id + "</id>" + fields + "</thing></things></li></maps>");
    private static SaveDiff Diff(bool known = true) => new(new SaveDiffScope(new HashSet<string> { Name, "Other" }, new HashSet<string> { Name, "Other" }, (_, _) => false,
        new HashSet<string>(), VerifiedDuplicateForbiddableThingIds: known ? new HashSet<string> { Id } : null));

    [Fact]
    public void ExactObservedDuplicateRetainsTheRawAddedDifference()
    {
        SaveDiff diff = Diff();
        var changes = diff.Compare(Save("<forbidden>True</forbidden>"), Save("<forbidden>True</forbidden><forbidden>True</forbidden>"));
        Assert.Single(changes); Assert.Equal("added", changes[0].Kind);
        Assert.NotNull(diff.ExplainForbiddableNormalization(changes[0]));
        SaveDiff unknown = Diff(false);
        Assert.Null(unknown.ExplainForbiddableNormalization(Assert.Single(unknown.Compare(Save("<forbidden>True</forbidden>"), Save("<forbidden>True</forbidden><forbidden>True</forbidden>")))));
    }

    [Theory]
    [InlineData("<forbidden>False</forbidden>", "<forbidden>True</forbidden><forbidden>True</forbidden>")]
    [InlineData("<forbidden>True</forbidden>", "<forbidden>True</forbidden><forbidden>False</forbidden>")]
    [InlineData("<forbidden>True</forbidden>", "<forbidden>True</forbidden><forbidden>True</forbidden><forbidden>True</forbidden>")]
    [InlineData("<forbidden>True</forbidden>", "<forbidden>True</forbidden><forbidden IsNull=\"True\" />")]
    [InlineData("<forbidden>True</forbidden>", "<forbidden>True</forbidden><forbidden><![CDATA[True]]></forbidden>")]
    [InlineData("<quality>True</quality>", "<quality>True</quality><quality>True</quality>")]
    [InlineData("", "<forbidden>True</forbidden><forbidden>True</forbidden>")]
    public void ChangedPrimaryDifferentDuplicateWrongCountOrOtherFieldIsNeverExplained(string before, string after)
    {
        SaveDiff diff = Diff();
        var changes = diff.Compare(Save(before), Save(after));
        Assert.NotEmpty(changes);
        Assert.All(changes, change => Assert.Null(diff.ExplainForbiddableNormalization(change)));
    }

    [Fact]
    public void AnotherDefOrNestedPlayerFieldCannotReuseTheThingEvidence()
    {
        foreach (var pair in new[] {
            (Save("<forbidden>True</forbidden>", "Other"), Save("<forbidden>True</forbidden><forbidden>True</forbidden>", "Other")),
            (Save("<note><forbidden>True</forbidden></note>"), Save("<note><forbidden>True</forbidden><forbidden>True</forbidden></note>")) })
        {
            SaveDiff diff = Diff();
            Assert.All(diff.Compare(pair.Item1, pair.Item2), change => Assert.Null(diff.ExplainForbiddableNormalization(change)));
        }
    }

    [Fact]
    public void AMapShapedSnapshotIsNotTheActualGameMapList()
    {
        XDocument a = Save("<forbidden>True</forbidden>");
        XDocument b = Save("<forbidden>True</forbidden><forbidden>True</forbidden>");
        foreach (XDocument doc in new[] { a, b })
        {
            XElement maps = SaveXml.Game(doc)!.Element("maps")!; maps.Remove();
            SaveXml.Game(doc)!.Add(new XElement("snapshot", maps));
        }
        SaveDiff diff = Diff();
        Assert.Null(diff.ExplainForbiddableNormalization(Assert.Single(diff.Compare(a, b))));
    }

    [Fact]
    public void ActualTwoNativeComponentsReadTheFirstLeafAndWriteTwoTrueValues()
    {
        ScribeLoader previousLoader = Scribe.loader; ScribeSaver previousSaver = Scribe.saver; LoadSaveMode previousMode = Scribe.mode;
        ThingDef[] definitions = [.. DefDatabase<ThingDef>.AllDefsListForReading];
        string path = Path.Combine(Path.GetTempPath(), "mugirl-forbiddable-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            var xml = new XmlDocument(); xml.LoadXml("<thing><forbidden>True</forbidden></thing>");
            var apparel = new Apparel { def = new ThingDef { defName = Name, thingClass = typeof(Apparel), comps = [new CompProperties_Forbiddable(), new CompProperties { compClass = typeof(CompColorable) }, new CompProperties { compClass = typeof(CompQuality) }, new CompProperties_Styleable(), new CompProperties_Forbiddable(), new CompProperties { compClass = typeof(CompColorable) }] }, thingIDNumber = 1 };
            apparel.InitializeComps();
            DefDatabase<ThingDef>.Clear(); DefDatabase<ThingDef>.Add(apparel.def);
            Scribe.loader = new ScribeLoader { curXmlParent = xml.DocumentElement, curParent = apparel }; Scribe.mode = LoadSaveMode.LoadingVars;
            foreach (ThingComp comp in apparel.AllComps) { comp.PostExposeData(); }
            Assert.True(DuplicateForbiddableEvidence.TryRoster(apparel, out string[] roster)); Assert.Equal(6, roster.Length);
            DuplicateForbiddableEvidence.Begin();
            Assert.Contains(Id, DuplicateForbiddableEvidence.Capture([apparel], out _));
            Scribe.mode = LoadSaveMode.Inactive; Scribe.saver = new ScribeSaver(); Scribe.saver.InitSaving(path, "thing");
            foreach (ThingComp comp in apparel.AllComps) { comp.PostExposeData(); }
            Scribe.saver.FinalizeSaving();
            Assert.Equal(new[] { "True", "True" }, XElement.Load(path).Elements("forbidden").Select(e => e.Value));
            typeof(CompForbiddable).GetField("forbiddenInt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(apparel.AllComps[4], false);
            Assert.False(DuplicateForbiddableEvidence.TryRoster(apparel, out _));
            typeof(CompForbiddable).GetField("forbiddenInt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(apparel.AllComps[4], true);
            CompProperties originalProps = apparel.def.comps[0];
            apparel.def.comps[0] = new CompProperties_Forbiddable();
            Assert.False(DuplicateForbiddableEvidence.TryRoster(apparel, out _));
            apparel.def.comps[0] = originalProps;
            apparel.AllComps.Add(new UnknownComp());
            Assert.False(DuplicateForbiddableEvidence.TryRoster(apparel, out _));
            apparel.AllComps.RemoveAt(apparel.AllComps.Count - 1);
            DefDatabase<ThingDef>.Clear(); DefDatabase<ThingDef>.Add(new ThingDef { defName = Name });
            Assert.Empty(DuplicateForbiddableEvidence.Capture([apparel], out _));
        }
        finally
        {
            if (Scribe.mode == LoadSaveMode.Saving) { Scribe.saver.ForceStop(); }
            Scribe.loader = previousLoader; Scribe.saver = previousSaver; Scribe.mode = previousMode;
            DefDatabase<ThingDef>.Clear(); foreach (ThingDef def in definitions) { DefDatabase<ThingDef>.Add(def); }
            if (File.Exists(path)) { File.Delete(path); }
        }
    }

    [Fact]
    public void AReaderPatchInvalidatesTheProofEvenWhenItDoesNotChangeTheValue()
    {
        var harmony = new Harmony("A5.Forbidden.Reader.Negative");
        harmony.Patch(typeof(CompForbiddable).GetMethod(nameof(CompForbiddable.PostExposeData))!, prefix: new HarmonyMethod(typeof(DuplicateForbiddableTests), nameof(Noop)));
        try { Assert.Throws<InvalidOperationException>(DuplicateForbiddableEvidence.Begin); }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    private static void Noop() { }
    private sealed class UnknownComp : ThingComp;

    [Theory]
    [InlineData("apparel")]
    [InlineData("writer")]
    [InlineData("current")]
    [InlineData("maps")]
    [InlineData("thing")]
    [InlineData("converter")]
    public void OuterReaderFinalWriterAndActualMapSourcesCannotBePatched(string targetName)
    {
        MethodInfo target = targetName switch
        {
            "apparel" => typeof(Apparel).GetMethod(nameof(Apparel.ExposeData))!,
            "writer" => typeof(ScribeSaver).GetMethod(nameof(ScribeSaver.WriteElement))!,
            "current" => typeof(Current).GetProperty(nameof(Current.Game))!.GetMethod!,
            "maps" => typeof(Game).GetProperty(nameof(Game.Maps))!.GetMethod!,
            "thing" => typeof(Thing).GetMethod(nameof(Thing.ExposeData))!,
            _ => (MethodInfo)ReviewedEngine.ResolveMethod(0x060034A1)!,
        };
        var other = new Harmony("A5.Forbidden.OuterChain.Negative");
        other.Patch(target, prefix: new HarmonyMethod(typeof(DuplicateForbiddableTests), nameof(Noop)));
        try { Assert.Throws<InvalidOperationException>(DuplicateForbiddableEvidence.Begin); }
        finally { other.UnpatchAll(other.Id); }
    }

    [Theory]
    [InlineData("prefix")]
    [InlineData("postfix")]
    [InlineData("finalizer")]
    public void InnerBoolReadHooksAreAlsoRejected(string role)
    {
        MethodInfo target = typeof(CompForbiddable).GetMethod(nameof(CompForbiddable.PostExposeData))!;
        MethodInfo inner = new CurrentScribeRoots(typeof(DuplicateForbiddableTests).Assembly).DirectCalls(target).Single(m => m.DeclaringType == typeof(Scribe_Values));
        var other = new Harmony("A5.Forbidden.Inner.Negative");
        try
        {
            var hook = new HarmonyMethod(typeof(DuplicateForbiddableTests), nameof(Noop)) { innerMethod = new InnerMethod(inner, []) };
            PatchProcessor processor = other.CreateProcessor(target);
            if (role == "prefix") { processor.AddInnerPrefix(hook); } else if (role == "postfix") { processor.AddInnerPostfix(hook); } else { processor.AddInnerFinalizer(hook); }
            processor.Patch(); Assert.Throws<InvalidOperationException>(DuplicateForbiddableEvidence.Begin);
        }
        finally { other.UnpatchAll(other.Id); }
    }
}
