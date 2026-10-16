using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using HarmonyLib;
using Mugirl.Features.Incidents;
using Mugirl.SaveUpgrade;
using Verse;
using Verse.AI;

namespace Mugirl.Tests.SaveUpgrade;

[Collection("Reference aliases")]
public sealed class ThinkTreeForeignSourceTests
{
    [Fact]
    public void PreservingUnknownKeysRequiresTheReviewedJobWriter()
    {
        Assert.True(ReviewedSaveEngine.CanPreserveUnresolvedThinkKeys(new Guid("8e962ecc-7ac7-42d2-8f3b-1b16cd24d2cd")));
        Assert.False(ReviewedSaveEngine.CanPreserveUnresolvedThinkKeys(new Guid("425a5939-54d7-432a-8f44-e3076b25fce9")));
        Assert.False(ReviewedSaveEngine.CanPreserveUnresolvedThinkKeys(Guid.Empty));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownForeignSourceDoesNotInvalidateAProvenMugirlSource(bool reverse)
    {
        using var scope = new Scope();
        var jobs = scope.Read(scope.ForeignXml() + scope.OwnXml(), reverse: reverse);
        Assert.False(scope.Session!.Report.Failed, scope.Session.Report.Error);
        Assert.Null(jobs.Foreign!.jobGiver);
        Assert.Same(scope.OwnNode, jobs.Own!.jobGiver);
        XmlDocument saved = scope.Write(jobs.Foreign, jobs.Own);
        Assert.Equal(Scope.Unknown.ToString(), saved.SelectSingleNode("//foreign/lastJobGiverKey")!.InnerText);
        Assert.Equal(scope.OwnNode.UniqueSaveKey.ToString(), saved.SelectSingleNode("//owned/lastJobGiverKey")!.InnerText);
        Assert.Equal("1234", saved.SelectSingleNode("//foreign/startTick")!.InnerText);
        Assert.Equal("321", saved.SelectSingleNode("//foreign/expiryInterval")!.InnerText);
        Assert.Single(saved.SelectNodes("//foreign/lastJobGiverKey")!.Cast<XmlNode>());
    }

    [Fact]
    public void CurrentSchemaReloadAndRepeatedSavesKeepTheOriginalUnknownKey()
    {
        using var scope = new Scope();
        Job first = scope.Read(scope.ForeignXml()).Foreign!;
        string once = SaveXml.Game(scope.Write(first));
        Assert.Equal(once, SaveXml.Game(scope.Write(first)));
        Job second = scope.Read(once, stamp: SaveSchema.Current).Foreign!;
        Assert.False(scope.Session!.Report.Failed);
        Assert.Null(second.jobGiver);
        Assert.Equal(once, SaveXml.Game(scope.Write(second)));
    }

    [Fact]
    public void CloningPreservesTheSourceAndPoolReuseCannotInheritIt()
    {
        using var scope = new Scope();
        Job original = scope.Read(scope.ForeignXml()).Foreign!;
        Job clone = original.Clone();
        Assert.Equal(Scope.Unknown.ToString(), scope.Write(clone).SelectSingleNode("//lastJobGiverKey")!.InnerText);
        original.Clear();
        // 故意复用相同身份与 Def，证明隔离依赖真实 Clear 钩子，而非碰巧不同的 loadID。
        original.loadID = 1;
        original.def = scope.ForeignJob;
        original.jobGiverThinkTree = scope.ForeignTree;
        Assert.Null(scope.Write(original).SelectSingleNode("//lastJobGiverKey"));
        Assert.Equal(Scope.Unknown.ToString(), scope.Write(clone).SelectSingleNode("//lastJobGiverKey")!.InnerText);
    }

    [Fact]
    public void ResolvedOrChangedJobsCannotResurrectAnOldUnknownSource()
    {
        using var scope = new Scope();
        Job job = scope.Read(scope.ForeignXml()).Foreign!;
        job.jobGiver = scope.ForeignTree.thinkRoot;
        Assert.Equal(job.jobGiver.UniqueSaveKey.ToString(), scope.Write(job).SelectSingleNode("//lastJobGiverKey")!.InnerText);
        job.jobGiver = null;
        Assert.Null(scope.Write(job).SelectSingleNode("//lastJobGiverKey"));
        job = scope.Read(scope.ForeignXml()).Foreign!;
        job.loadID++;
        Assert.Null(scope.Write(job).SelectSingleNode("//lastJobGiverKey"));
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("job")]
    [InlineData("provenance")]
    [InlineData("current-key")]
    [InlineData("wrong-tree")]
    public void UnknownOwnedUnprovenOrCollidingSourcesStillFail(string scenario)
    {
        using var scope = new Scope();
        int key = Scope.Unknown;
        if (scenario == "tree") { scope.ForeignTree.modContentPack = scope.OwnTree.modContentPack; }
        if (scenario == "job") { scope.ForeignJob.modContentPack = scope.OwnTree.modContentPack; }
        if (scenario == "provenance") { scope.ForeignTree.modContentPack = null; }
        if (scenario == "wrong-tree") { key = scope.OwnKey; }
        if (scenario == "current-key")
        {
            typeof(ThinkNode).GetField("uniqueSaveKeyInt", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(scope.ForeignTree.thinkRoot, key);
        }
        scope.Read(scope.ForeignXml(key));
        Assert.True(scope.Session!.Report.Failed);
        Assert.Equal("Framework.ThinkTreeKeys", scope.Session.Report.FailedStep);
    }

    [Theory]
    [InlineData("side")]
    [InlineData("no-mod")]
    [InlineData("newer")]
    public void UnrelatedDocumentsAndUnsupportedSchemasKeepNativeBehavior(string scenario)
    {
        using var scope = new Scope();
        Job job = scope.Read(scope.ForeignXml(), stamp: scenario == "newer" ? SaveSchema.Current + 1 : null,
            mainDocument: scenario != "side", hasMugirl: scenario != "no-mod").Foreign!;
        Assert.Null(scope.Write(job).SelectSingleNode("//lastJobGiverKey"));
    }

    private sealed class Scope : IDisposable
    {
        internal const int Unknown = -1941758528;
        private readonly Harmony harmony = new("Mugirl.Tests.ForeignThinkKeys." + Guid.NewGuid().ToString("N"));
        private readonly ScribeErrorCapture errors = new();
        private readonly string directory = Path.Combine(Path.GetTempPath(), "mugirl-foreign-think-" + Guid.NewGuid().ToString("N"));
        private readonly ThinkTreeDef[] previousTrees = [.. DefDatabase<ThinkTreeDef>.AllDefsListForReading];
        private readonly JobDef[] previousJobs = [.. DefDatabase<JobDef>.AllDefsListForReading];
        private readonly FieldInfo active = typeof(SaveUpgradeRuntime).GetField("activeSession", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? previousSession;
        internal ThinkTreeDef OwnTree { get; }
        internal ThinkTreeDef ForeignTree { get; }
        internal ThinkNode OwnNode { get; } = new JobGiver_RegisterPunisher();
        internal JobDef ForeignJob { get; }
        internal int OwnKey { get; }
        internal SaveUpgradeSession? Session { get; private set; }

        internal Scope()
        {
            previousSession = active.GetValue(null);
            Directory.CreateDirectory(directory);
            Type[] hooks = [typeof(Harmony_ThinkTreeKeyAssigner_Reset), typeof(Harmony_ThinkTreeKeyAssigner_AssignKeys),
                typeof(Harmony_ThinkTreeKeyAssigner_AssignSingleKey), typeof(Harmony_ThinkTreeDef_ResolveReferences),
                typeof(Harmony_ThinkNode_DeepCopy_LegacyLayout), typeof(Harmony_Job_ExposeData_ThinkTreeIdentity),
                typeof(Harmony_ThinkTreeDef_TryGetThinkNodeWithSaveKey), typeof(Harmony_Job_Clear_ThinkTreeIdentity),
                typeof(Harmony_Job_Clone_ThinkTreeIdentity)];
            foreach (Type hook in hooks) { harmony.CreateClassProcessor(hook).Patch(); }
            ThinkTreeKeyAssigner.Reset();
            LegacyThinkTreeKeys.Reset();
            ModContentPack ours = Pack(typeof(LegacyThinkTreeKeys).Assembly), foreign = Pack(typeof(Job).Assembly);
            OwnTree = new ThinkTreeDef { defName = "Mugirl_TestSource", modContentPack = ours,
                thinkRoot = new ThinkNode_Priority { subNodes = [OwnNode] } };
            ForeignTree = new ThinkTreeDef { defName = "ForeignSource", modContentPack = foreign, thinkRoot = new ThinkNode_Priority() };
            OwnTree.ResolveReferences();
            ForeignTree.ResolveReferences();
            OwnKey = ((LegacyThinkTreeLedger)typeof(LegacyThinkTreeKeys).GetField("Ledger", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null)!).KeyFor(OwnNode);
            Assert.NotEqual(Unknown, OwnKey);
            Assert.NotEqual(Unknown, ForeignTree.thinkRoot.UniqueSaveKey);
            ForeignJob = new JobDef { defName = "ForeignJob", modContentPack = foreign, driverClass = typeof(JobDriver) };
            DefDatabase<ThinkTreeDef>.Add(OwnTree);
            DefDatabase<ThinkTreeDef>.Add(ForeignTree);
            DefDatabase<JobDef>.Add(ForeignJob);
        }

        internal string ForeignXml(int key = Unknown) => "<foreign><def>ForeignJob</def><loadID>1</loadID><startTick>1234</startTick><expiryInterval>321</expiryInterval>"
            + "<jobGiverThinkTree>ForeignSource</jobGiverThinkTree><lastJobGiverKey>" + key + "</lastJobGiverKey></foreign>";

        internal string OwnXml() => "<owned><def>ForeignJob</def><loadID>2</loadID><jobGiverThinkTree>Mugirl_TestSource</jobGiverThinkTree>"
            + "<lastJobGiverKey>" + OwnKey + "</lastJobGiverKey></owned>";

        internal (Job? Foreign, Job? Own) Read(string body, int? stamp = null, bool reverse = false, bool mainDocument = true, bool hasMugirl = true)
        {
            string input = Path.Combine(directory, "input.xml");
            SaveXml.Save(body, stamp, mugirl: hasMugirl).Save(input);
            Scribe.loader.InitLoading(input);
            XmlDocument document = Scribe.loader.curXmlParent.OwnerDocument!;
            Session = SaveUpgradeSession.Start(951, mainDocument ? document : SaveXml.Save(""), input);
            Session.RunDom(SaveXml.Empty(), new TestCatalog());
            active.SetValue(null, Session);
            Job? foreign = null, own = null;
            Scribe.EnterNode("game");
            if (reverse) { Scribe_Deep.Look(ref own, "owned"); }
            Scribe_Deep.Look(ref foreign, "foreign");
            if (!reverse) { Scribe_Deep.Look(ref own, "owned"); }
            Scribe.ExitNode();
            Scribe.loader.curXmlParent = null;
            Scribe.loader.curParent = null;
            Scribe.loader.curPathRelToParent = null;
            Scribe.mode = LoadSaveMode.Inactive;
            Scribe.loader.crossRefs.ResolveAllCrossReferences();
            Scribe.loader.initer.DoAllPostLoadInits();
            Scribe.ForceStop();
            Assert.Empty(errors.Messages);
            Session.MarkPhase(UpgradePhase.A);
            Session.MarkPhase(UpgradePhase.B);
            Session.Complete();
            return (foreign, own);
        }

        internal XmlDocument Write(Job foreign, Job? own = null)
        {
            string output = Path.Combine(directory, "output.xml");
            Scribe.saver.InitSaving(output, "savegame");
            Scribe.EnterNode("game");
            Scribe_Deep.Look(ref foreign, "foreign");
            if (own is not null) { Scribe_Deep.Look(ref own, "owned"); }
            Scribe.ExitNode();
            Scribe.saver.FinalizeSaving();
            Assert.Empty(errors.Messages);
            var document = new XmlDocument { XmlResolver = null };
            document.Load(output);
            return document;
        }

        private static ModContentPack Pack(Assembly assembly)
        {
            var pack = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
            pack.assemblies = new ModAssemblyHandler(pack);
            pack.assemblies.loadedAssemblies.Add(assembly);
            return pack;
        }

        public void Dispose()
        {
            Scribe.ForceStop();
            active.SetValue(null, previousSession);
            harmony.UnpatchAll(harmony.Id);
            ThinkTreeKeyAssigner.Reset();
            LegacyThinkTreeKeys.Reset();
            DefDatabase<ThinkTreeDef>.Clear();
            foreach (ThinkTreeDef tree in previousTrees) { DefDatabase<ThinkTreeDef>.Add(tree); }
            DefDatabase<JobDef>.Clear();
            foreach (JobDef job in previousJobs) { DefDatabase<JobDef>.Add(job); }
            foreach (string file in Directory.GetFiles(directory)) { File.Delete(file); }
            Directory.Delete(directory);
            errors.Dispose();
        }
    }
}
