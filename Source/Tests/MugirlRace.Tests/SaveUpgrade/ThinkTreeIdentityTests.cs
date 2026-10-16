using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml;
using HarmonyLib;
using Mugirl.Features.Incidents;
using Mugirl.SaveUpgrade;
using Verse;
using Verse.AI;

namespace Mugirl.Tests.SaveUpgrade;

[Collection("Reference aliases")]
public sealed class ThinkTreeIdentityTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(42, false)]
    [InlineData(-15527, true)]
    public void ShadowMatchesRealAssignerWithCollisionsAndRestoresRandomState(int seed, bool linkParents)
    {
        ThinkNode_Priority root = Tree(new JobGiver_RegisterPunisher());
        if (linkParents)
        {
            foreach (ThinkNode node in root.subNodes)
            {
                node.parent = root;
            }
        }

        var ledger = new LegacyThinkTreeLedger(n => n.GetType().Name);
        Rand.PushState(51684);
        int expected;
        try { expected = Rand.Int; }
        finally { Rand.PopState(); }
        Rand.PushState(51684);
        try
        {
            ledger.Capture(root.ThisAndChildrenRecursive, seed);
            Assert.Equal(expected, Rand.Int);
        }
        finally { Rand.PopState(); }

        ThinkTreeKeyAssigner.Reset();
        try
        {
            ThinkTreeKeyAssigner.AssignKeys(root, seed);
            Assert.All(root.ThisAndChildrenRecursive, n => Assert.Equal(n.UniqueSaveKey, ledger.KeyFor(n)));
            ThinkNode copy = root.DeepCopy();
            // DeepCopy 已按真实调用顺序分配；另一份纯 shadow 按相同的子先父顺序累计。
            CaptureCopies(copy, ledger);
            Assert.All(copy.ThisAndChildrenRecursive, n => Assert.Equal(n.UniqueSaveKey, ledger.KeyFor(n)));
        }
        finally { ThinkTreeKeyAssigner.Reset(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealJobReadRequiresAProvenOldKeyBeforeTheNextSaveWritesTheCanonicalKey(bool unknownKey)
    {
        using var errors = new ScribeErrorCapture();
        var harmony = new Harmony("Mugirl.Tests.ThinkTreeIdentities");
        Type[] hooks = [typeof(Harmony_ThinkTreeKeyAssigner_Reset), typeof(Harmony_ThinkTreeKeyAssigner_AssignKeys),
            typeof(Harmony_ThinkTreeKeyAssigner_AssignSingleKey), typeof(Harmony_ThinkTreeDef_ResolveReferences),
            typeof(Harmony_ThinkNode_DeepCopy_LegacyLayout),
            typeof(Harmony_Job_ExposeData_ThinkTreeIdentity), typeof(Harmony_ThinkTreeDef_TryGetThinkNodeWithSaveKey),
            typeof(Harmony_BackCompatibility_BackCompatibleDefName)];
        string directory = Path.Combine(Path.GetTempPath(), "mugirl-thinktree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        ThinkTreeDef[] originalDefs = [.. DefDatabase<ThinkTreeDef>.AllDefsListForReading];
        FieldInfo active = typeof(SaveUpgradeRuntime).GetField("activeSession", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previousSession = active.GetValue(null);
        try
        {
            foreach (Type hook in hooks) { harmony.CreateClassProcessor(hook).Patch(); }
            ThinkTreeKeyAssigner.Reset();
            var old = new ThinkTreeDef { defName = "MugirlPunisher", thinkRoot = Tree(new JobGiver_MugirlPunisher()) };
            old.ResolveReferences();
            int oldKey = old.thinkRoot.subNodes[0].UniqueSaveKey;
            ThinkTreeKeyAssigner.Reset();
            var current = new ThinkTreeDef { defName = "Mugirl_Punisher", thinkRoot = Tree(new JobGiver_RegisterPunisher()) };
            current.ResolveReferences();
            Assert.Null(LegacyThinkTreeKeys.Problem);
            int canonical = current.thinkRoot.subNodes[0].UniqueSaveKey;
            Assert.NotEqual(oldKey, canonical);
            if (unknownKey) { oldKey = canonical; }
            DefDatabase<ThinkTreeDef>.Add(current);
            string input = Path.Combine(directory, "input.xml");
            SaveXml.Save("<job><jobGiverThinkTree>MugirlPunisher</jobGiverThinkTree><lastJobGiverKey>" + oldKey + "</lastJobGiverKey></job>").Save(input);
            Scribe.loader.InitLoading(input);
            XmlDocument document = Scribe.loader.curXmlParent.OwnerDocument!;
            SaveUpgradeSession session = SaveUpgradeSession.Start(919, document, input);
            session.ReadTrace = new ValidationReadTrace();
            session.RunDom(SaveXml.Empty(), new TestCatalog());
            active.SetValue(null, session);
            Scribe.EnterNode("game");
            Job? job = null;
            Scribe_Deep.Look(ref job, "job");
            Scribe.ExitNode();
            Scribe.loader.crossRefs.ResolveAllCrossReferences();
            Scribe.loader.initer.DoAllPostLoadInits();
            Scribe.ForceStop();
            Assert.NotNull(job);
            Assert.Empty(errors.Messages);
            if (unknownKey)
            {
                Assert.Null(job.jobGiver);
                Assert.True(session.Report.Failed);
                Assert.Equal("Framework.ThinkTreeKeys", session.Report.FailedStep);
                Assert.DoesNotContain(Events(session), e => e.GetProperty("kind").GetString() == "ThinkKey");
                session.MarkPhase(UpgradePhase.A);
                session.MarkPhase(UpgradePhase.B);
                session.Complete();
                Assert.False(session.Verified);
                return;
            }
            Assert.Same(current.thinkRoot.subNodes[0], job.jobGiver);
            Assert.False(session.Report.Failed, session.Report.Error);
            using (JsonDocument trace = JsonDocument.Parse(session.ReadTrace.ToJson()))
            {
                Assert.Empty(trace.RootElement.GetProperty("errors").EnumerateArray());
                JsonElement[] events = [.. trace.RootElement.GetProperty("events").EnumerateArray()];
                Assert.Equal(2, events.Length);
                Assert.Equal("DefReference", events[0].GetProperty("kind").GetString());
                Assert.Equal("ThinkKey", events[1].GetProperty("kind").GetString());
                Assert.Equal("/savegame[1]/game[1]/job[1]/lastJobGiverKey[1]", events[1].GetProperty("path").GetString());
                Assert.Equal(canonical.ToString(System.Globalization.CultureInfo.InvariantCulture), events[1].GetProperty("newValue").GetString());
            }
            string saved = Path.Combine(directory, "saved.xml");
            Scribe.saver.InitSaving(saved, "savegame");
            Scribe_Deep.Look(ref job, "job");
            Scribe.saver.FinalizeSaving();
            var result = new XmlDocument(); result.Load(saved);
            Assert.Equal(canonical.ToString(), result.SelectSingleNode("//lastJobGiverKey")!.InnerText);
            active.SetValue(null, null);
            Scribe.loader.InitLoading(saved);
            Job? loaded = null;
            Scribe_Deep.Look(ref loaded, "job");
            Scribe.loader.crossRefs.ResolveAllCrossReferences();
            Scribe.loader.initer.DoAllPostLoadInits();
            Scribe.ForceStop();
            Assert.Same(current.thinkRoot.subNodes[0], loaded!.jobGiver);
        }
        finally
        {
            Scribe.ForceStop();
            active.SetValue(null, previousSession);
            harmony.UnpatchAll(harmony.Id);
            ThinkTreeKeyAssigner.Reset();
            LegacyThinkTreeKeys.Reset();
            DefDatabase<ThinkTreeDef>.Clear();
            foreach (ThinkTreeDef definition in originalDefs) { DefDatabase<ThinkTreeDef>.Add(definition); }
            foreach (string file in Directory.GetFiles(directory)) { File.Delete(file); }
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void SideDocumentsUnregisteredJobsAndCurrentSchemasNeverUseLegacyKeys()
    {
        LegacyThinkTreeKeys.Reset();
        var node = new JobGiver_RegisterPunisher();
        LegacyThinkTreeKeys.Capture(node, 0, single: true);
        ThinkTreeKeyAssigner.Reset();
        ThinkTreeKeyAssigner.AssignSingleKey(node, 0);
        int canonical = node.UniqueSaveKey;
        int oldKey = Gen.HashCombineInt(0, GenText.StableStringHash("JobGiver_MugirlPunisher"));
        XmlDocument document = SaveXml.Save("<job><lastJobGiverKey>" + oldKey + "</lastJobGiverKey></job>");
        SaveUpgradeSession legacy = SaveXml.Upgrade(document, SaveXml.Empty());
        var tree = new ThinkTreeDef { defName = "Test", thinkRoot = node };
        var job = new Job { jobGiverThinkTree = tree };
        XmlDocument side = SaveXml.Save("");
        LegacyThinkJobScope state = LegacyThinkTreeKeys.EnterJob(job, legacy, LoadSaveMode.LoadingVars, side.DocumentElement!["game"]);
        LegacyThinkTreeKeys.LeaveJob(state);
        state = LegacyThinkTreeKeys.EnterJob(job, legacy, LoadSaveMode.PostLoadInit, null);
        int key = oldKey;
        Assert.True(LegacyThinkTreeKeys.PrepareLookup(tree, ref key, out LegacyThinkLookup? lookup));
        Assert.Null(lookup);
        Assert.Equal(oldKey, key);
        LegacyThinkTreeKeys.LeaveJob(state);
        state = LegacyThinkTreeKeys.EnterJob(job, legacy, LoadSaveMode.LoadingVars, document.DocumentElement!["game"]!["job"]);
        LegacyThinkTreeKeys.LeaveJob(state);
        state = LegacyThinkTreeKeys.EnterJob(job, legacy, LoadSaveMode.PostLoadInit, null);
        Assert.True(LegacyThinkTreeKeys.PrepareLookup(tree, ref key, out lookup));
        Assert.NotNull(lookup);
        Assert.Equal(canonical, key);
        LegacyThinkTreeKeys.LeaveJob(state);
        SaveUpgradeSession current = SaveXml.Upgrade(SaveXml.Save("", stamp: SaveSchema.Current), SaveXml.Empty());
        state = LegacyThinkTreeKeys.EnterJob(job, current, LoadSaveMode.PostLoadInit, null);
        key = oldKey;
        Assert.True(LegacyThinkTreeKeys.PrepareLookup(tree, ref key, out lookup));
        Assert.Null(lookup);
        Assert.Equal(oldKey, key);
        LegacyThinkTreeKeys.LeaveJob(state);
        LegacyThinkTreeKeys.Reset();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OnlyTheSuccessfulActualLookupRecordsAnIdentityIncludingMixedMooGirlSaves(bool renamed, bool mixed)
    {
        using var scope = new LookupScope(renamed);
        if (mixed) { scope.Session.Report.State = UpgradeState.MooGirlEra; }
        // Re-enter after the state change, so mixed MooGirl detection cannot bypass registration.
        LegacyThinkJobScope nested = LegacyThinkTreeKeys.EnterJob(scope.Job, scope.Session, LoadSaveMode.PostLoadInit, null);
        try
        {
            int key = scope.OldKey;
            Assert.True(LegacyThinkTreeKeys.PrepareLookup(scope.Tree, ref key, out LegacyThinkLookup? lookup));
            Assert.NotNull(lookup);
            Assert.Equal(scope.Node.UniqueSaveKey, key);
            Assert.Empty(Events(scope.Session));
            bool found = scope.Tree.TryGetThinkNodeWithSaveKey(key, out ThinkNode? result);
            LegacyThinkTreeKeys.CompleteLookup(scope.Tree, lookup, ref result, ref found);
            Assert.True(found);
            Assert.Same(scope.Node, result);
            if (renamed) { Assert.Equal("ThinkKey", Assert.Single(Events(scope.Session)).GetProperty("kind").GetString()); }
            else { Assert.Empty(Events(scope.Session)); } // Traces contain actual rewrites, not identity reads.
            Assert.False(scope.Session.Report.Failed, scope.Session.Report.Error);
        }
        finally { LegacyThinkTreeKeys.LeaveJob(nested); }
    }

    [Fact]
    public void UnknownOldKeyCannotFallThroughToAnAccidentalCurrentIdentity()
    {
        using var scope = new LookupScope();
        int key = scope.Node.UniqueSaveKey; // This valid current key was never an identity in the old tree.
        Assert.NotEqual(scope.OldKey, key);
        scope.Source["lastJobGiverKey"]!.InnerText = key.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(scope.Tree.TryGetThinkNodeWithSaveKey(key, out _));
        Assert.False(LegacyThinkTreeKeys.PrepareLookup(scope.Tree, ref key, out LegacyThinkLookup? lookup));
        Assert.Null(lookup);
        AssertFailedWithoutTrace(scope.Session);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("other-node")]
    [InlineData("other-tree")]
    [InlineData("foreign-node-in-tree-result")]
    [InlineData("changed-key")]
    public void UnprovenResultsFailWithoutAReadTraceOrSchemaStamp(string fault)
    {
        using var scope = new LookupScope();
        int key = scope.OldKey;
        Assert.True(LegacyThinkTreeKeys.PrepareLookup(scope.Tree, ref key, out LegacyThinkLookup? lookup));
        ThinkNode? result = scope.Node;
        bool found = fault != "missing";
        ThinkTreeDef actualTree = scope.Tree;
        if (fault == "other-node")
        {
            result = new ThinkNode_Priority();
            SetKey(result, key);
        }
        if (fault == "other-tree") { actualTree = new ThinkTreeDef { thinkRoot = scope.Node }; }
        if (fault == "foreign-node-in-tree-result") { scope.Tree.thinkRoot = new ThinkNode_Priority(); }
        if (fault == "changed-key") { SetKey(scope.Node, unchecked(key + 1)); }
        LegacyThinkTreeKeys.CompleteLookup(actualTree, lookup, ref result, ref found);
        Assert.False(found);
        Assert.Null(result);
        AssertFailedWithoutTrace(scope.Session);
    }

    [Fact]
    public void WrongSavedTreeCannotBindTheGlobalAliasFromAnotherTree()
    {
        using var scope = new LookupScope();
        var wrongTree = new ThinkTreeDef { thinkRoot = new ThinkNode_Priority() };
        scope.Job.jobGiverThinkTree = wrongTree;
        int key = scope.OldKey;
        Assert.False(LegacyThinkTreeKeys.PrepareLookup(wrongTree, ref key, out LegacyThinkLookup? lookup));
        Assert.Null(lookup);
        AssertFailedWithoutTrace(scope.Session);
    }

    [Fact]
    public void ExceptionMarksTheSessionFailedAndNestedJobScopeAlwaysRestoresTheOuterReader()
    {
        using var scope = new LookupScope();
        LegacyThinkJobScope previous = LegacyThinkTreeKeys.EnterJob(new Job(), scope.Session, LoadSaveMode.PostLoadInit, null);
        try
        {
            int untouched = scope.OldKey;
            Assert.True(LegacyThinkTreeKeys.PrepareLookup(scope.Tree, ref untouched, out LegacyThinkLookup? unrelated));
            Assert.Null(unrelated);
            throw new InvalidOperationException("nested reader");
        }
        catch (InvalidOperationException) { }
        finally { LegacyThinkTreeKeys.LeaveJob(previous); }
        int key = scope.OldKey;
        Assert.True(LegacyThinkTreeKeys.PrepareLookup(scope.Tree, ref key, out LegacyThinkLookup? lookup));
        Assert.NotNull(lookup);
        var error = new InvalidOperationException("lookup failed");
        Assert.Same(error, Harmony_ThinkTreeDef_TryGetThinkNodeWithSaveKey.Finalizer(error, lookup));
        AssertFailedWithoutTrace(scope.Session);
    }

    [Fact]
    public void DefaultMinusOneAndOtherTreeProbesRemainOutsideTheAliasProtocol()
    {
        using var scope = new LookupScope();
        int key = -1;
        Assert.True(LegacyThinkTreeKeys.PrepareLookup(scope.Tree, ref key, out LegacyThinkLookup? lookup));
        Assert.Null(lookup);
        Assert.Equal(-1, key);
        var otherTree = new ThinkTreeDef { thinkRoot = new ThinkNode_Priority() };
        key = scope.OldKey;
        Assert.True(LegacyThinkTreeKeys.PrepareLookup(otherTree, ref key, out lookup));
        Assert.Null(lookup);
        Assert.Equal(scope.OldKey, key);
        Assert.False(scope.Session.Report.Failed);
        Assert.Empty(Events(scope.Session));
    }

    [Fact]
    public void ALookupThatDoesNotMatchItsActualSavedFieldFailsInsteadOfProducingAFalseTrace()
    {
        using var scope = new LookupScope();
        int key = scope.OldKey;
        scope.Source["lastJobGiverKey"]!.InnerText = "17";
        Assert.False(LegacyThinkTreeKeys.PrepareLookup(scope.Tree, ref key, out LegacyThinkLookup? lookup));
        Assert.Null(lookup);
        AssertFailedWithoutTrace(scope.Session);
    }

    [Fact]
    public void AJobRemovedFromTheMainDocumentCannotReuseItsFormerReadContext()
    {
        using var scope = new LookupScope();
        scope.Source.ParentNode!.RemoveChild(scope.Source);
        LegacyThinkJobScope previous = LegacyThinkTreeKeys.EnterJob(scope.Job, scope.Session, LoadSaveMode.PostLoadInit, null);
        try
        {
            int key = scope.OldKey;
            Assert.True(LegacyThinkTreeKeys.PrepareLookup(scope.Tree, ref key, out LegacyThinkLookup? lookup));
            Assert.Null(lookup);
            Assert.Equal(scope.OldKey, key);
        }
        finally { LegacyThinkTreeKeys.LeaveJob(previous); }
    }

    [Fact]
    public void CapturingARealEmptyAssignmentBoundaryRecoversFromAPreviousIncompleteStream()
    {
        LegacyThinkTreeKeys.Reset();
        try
        {
            LegacyThinkTreeKeys.Capture(new ThinkNode_Priority(), 0, single: true, assignedCount: 17);
            Assert.NotNull(LegacyThinkTreeKeys.Problem);
            LegacyThinkTreeKeys.Capture(new ThinkNode_Priority(), 0, single: true, assignedCount: 17);
            Assert.NotNull(LegacyThinkTreeKeys.Problem);
            var node = new ThinkNode_Priority();
            LegacyThinkTreeKeys.Capture(node, 0, single: true, assignedCount: 0);
            Assert.Null(LegacyThinkTreeKeys.Problem);
            // The recovery really captured one node; this stream count is now consistent.
            LegacyThinkTreeKeys.Capture(new ThinkNode_Priority(), 0, single: true, assignedCount: 1);
            Assert.Null(LegacyThinkTreeKeys.Problem);
        }
        finally { LegacyThinkTreeKeys.Reset(); }
    }

    [Fact]
    public void ShadowRestoresRandomStateEvenWhenNameCaptureThrows()
    {
        var ledger = new LegacyThinkTreeLedger(_ => throw new InvalidOperationException("name"));
        Rand.PushState(5187);
        int expected;
        try { expected = Rand.Int; }
        finally { Rand.PopState(); }
        Rand.PushState(5187);
        try
        {
            Assert.Throws<InvalidOperationException>(() => ledger.Capture([new ThinkNode_Priority()], 91));
            Assert.Equal(expected, Rand.Int);
        }
        finally { Rand.PopState(); }
    }

    [Theory]
    [InlineData("BeforeCorporateDuty")]
    [InlineData("FirstPublishedLayout")]
    public void DifferentPublishedLayoutsCannotSilentlyBindTheSameKeyToDifferentNodes(string profile)
    {
        using var scope = new LookupScope();
        var other = new JobGiver_RegisterPunisher();
        scope.Node.subNodes.Add(other);
        var ledger = (LegacyThinkTreeLedger)typeof(LegacyThinkTreeKeys)
            .GetField(profile, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        ledger.Reset();
        ledger.Capture([other], 0);
        Assert.Equal(scope.OldKey, ledger.KeyFor(other));

        int key = scope.OldKey;
        Assert.False(LegacyThinkTreeKeys.PrepareLookup(scope.Tree, ref key, out LegacyThinkLookup? lookup));
        Assert.Null(lookup);
        Assert.Equal(scope.OldKey, key);
        Assert.Contains("layouts disagree", scope.Session.Report.Error, StringComparison.Ordinal);
        AssertFailedWithoutTrace(scope.Session);
    }

    [Fact]
    public void AnExceptionWhileReadingTheActualTreeCannotLeaveTheUpgradeSuccessful()
    {
        using var scope = new LookupScope();
        scope.Node.subNodes.Add(null!);
        int key = scope.OldKey;
        Assert.False(LegacyThinkTreeKeys.PrepareLookup(scope.Tree, ref key, out LegacyThinkLookup? lookup));
        Assert.Null(lookup);
        Assert.Contains("Could not verify", scope.Session.Report.Error, StringComparison.Ordinal);
        AssertFailedWithoutTrace(scope.Session);
    }

    private static JsonElement[] Events(SaveUpgradeSession session)
    {
        using JsonDocument trace = JsonDocument.Parse(session.ReadTrace!.ToJson());
        Assert.Empty(trace.RootElement.GetProperty("errors").EnumerateArray());
        return [.. trace.RootElement.GetProperty("events").EnumerateArray().Select(e => e.Clone())];
    }

    private static void SetKey(ThinkNode node, int key) => typeof(ThinkNode)
        .GetField("uniqueSaveKeyInt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(node, key);

    private static void AssertFailedWithoutTrace(SaveUpgradeSession session)
    {
        Assert.True(session.Report.Failed);
        Assert.Equal("Framework.ThinkTreeKeys", session.Report.FailedStep);
        Assert.Empty(Events(session));
        session.MarkPhase(UpgradePhase.A);
        session.MarkPhase(UpgradePhase.B);
        session.Complete();
        Assert.False(session.Verified);
    }

    private sealed class LookupScope : IDisposable
    {
        private readonly LegacyThinkJobScope previous;
        internal ThinkNode Node { get; }
        internal ThinkTreeDef Tree { get; }
        internal SaveUpgradeSession Session { get; }
        internal Job Job { get; }
        internal XmlElement Source { get; }
        internal int OldKey { get; }

        internal LookupScope(bool renamed = true)
        {
            ThinkTreeKeyAssigner.Reset();
            LegacyThinkTreeKeys.Reset();
            Node = renamed ? new JobGiver_RegisterPunisher() : new ThinkNode_Priority();
            LegacyThinkTreeKeys.Capture(Node, 0, single: true, assignedCount: 0);
            ThinkTreeKeyAssigner.AssignSingleKey(Node, 0);
            OldKey = Gen.HashCombineInt(0, GenText.StableStringHash(renamed ? "JobGiver_MugirlPunisher" : Node.GetType().Name));
            Tree = new ThinkTreeDef { defName = "Test", thinkRoot = Node };
            XmlDocument document = SaveXml.Save("<job><lastJobGiverKey>" + OldKey + "</lastJobGiverKey></job>");
            Source = document.DocumentElement!["game"]!["job"]!;
            Session = SaveXml.Upgrade(document, SaveXml.Empty());
            Session.ReadTrace = new ValidationReadTrace();
            Job = new Job { jobGiverThinkTree = Tree };
            LegacyThinkJobScope reading = LegacyThinkTreeKeys.EnterJob(Job, Session, LoadSaveMode.LoadingVars, Source);
            LegacyThinkTreeKeys.LeaveJob(reading);
            previous = LegacyThinkTreeKeys.EnterJob(Job, Session, LoadSaveMode.PostLoadInit, null);
        }

        public void Dispose()
        {
            LegacyThinkTreeKeys.LeaveJob(previous);
            LegacyThinkTreeKeys.Reset();
            ThinkTreeKeyAssigner.Reset();
        }
    }

    private static ThinkNode_Priority Tree(ThinkNode changed) => new()
    {
        subNodes = [changed, new ThinkNode_Priority(), new ThinkNode_Priority()],
    };

    private static void CaptureCopies(ThinkNode node, LegacyThinkTreeLedger ledger)
    {
        foreach (ThinkNode child in node.subNodes) { CaptureCopies(child, ledger); }
        ledger.Capture([node], 0);
    }

    private sealed class JobGiver_MugirlPunisher : ThinkNode_JobGiver
    {
        protected override Job? TryGiveJob(Pawn pawn) => null;
    }
}
