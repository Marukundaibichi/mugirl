using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mugirl.Features.Incidents;
using Mugirl.SaveUpgrade;
using RimWorld;
using Verse;
using Verse.AI;

namespace Mugirl.Tests.SaveUpgrade;

[Collection("Reference aliases")]
public sealed class ThinkTreeHistoricalLayoutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoricalShadowsMatchRealOldGraphsWithoutChangingCurrentAssignmentsOrRandomState(bool firstPublished)
    {
        DutyDef[] original = [.. DefDatabase<DutyDef>.AllDefsListForReading];
        var harmony = new Harmony("Mugirl.Tests.ThinkTreeHistoricalLayouts");
        try
        {
            Dictionary<(string Tree, string Path), ThinkNode> old = Build(modern: false, firstPublished);
            Dictionary<(string Tree, string Path), int> oldKeys = old.ToDictionary(pair => pair.Key, pair => pair.Value.UniqueSaveKey);
            Dictionary<(string Tree, string Path), ThinkNode> unpatched;
            int expectedNext;
            using (Rand.Block(9817))
            {
                unpatched = Build(modern: true, firstPublished: false);
                expectedNext = Rand.Int;
            }
            Dictionary<(string Tree, string Path), int> currentKeys = unpatched.ToDictionary(pair => pair.Key, pair => pair.Value.UniqueSaveKey);

            Type[] hooks = [typeof(Harmony_ThinkTreeKeyAssigner_Reset), typeof(Harmony_ThinkTreeKeyAssigner_AssignKeys),
                typeof(Harmony_ThinkTreeKeyAssigner_AssignSingleKey), typeof(Harmony_ThinkTreeDef_ResolveReferences),
                typeof(Harmony_ThinkNode_DeepCopy_LegacyLayout)];
            foreach (Type hook in hooks) { harmony.CreateClassProcessor(hook).Patch(); }
            Dictionary<(string Tree, string Path), ThinkNode> current;
            using (Rand.Block(9817))
            {
                current = Build(modern: true, firstPublished: false);
                Assert.Equal(expectedNext, Rand.Int);
            }
            Assert.Null(LegacyThinkTreeKeys.Problem);
            Assert.Equal(currentKeys.OrderBy(p => p.Key), current.ToDictionary(p => p.Key, p => p.Value.UniqueSaveKey).OrderBy(p => p.Key));
            string field = firstPublished ? "FirstPublishedLayout" : "BeforeCorporateDuty";
            var ledger = (LegacyThinkTreeLedger)typeof(LegacyThinkTreeKeys).GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            foreach (((string tree, string path), int key) in oldKeys)
            {
                Assert.True(ledger.TryNode(key, out ThinkNode? mapped), tree + "/" + path + " key " + key);
                Assert.Same(current[(tree, path)], mapped);
            }
            Assert.False(LegacyThinkLayouts.InsideOmittedDuty);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            ThinkTreeKeyAssigner.Reset();
            LegacyThinkTreeKeys.Reset();
            DefDatabase<DutyDef>.Clear();
            foreach (DutyDef definition in original) { DefDatabase<DutyDef>.Add(definition); }
        }
    }

    [Fact]
    public void NestedCopyScopeRestoresOmissionAfterAnException()
    {
        DutyDef[] original = [.. DefDatabase<DutyDef>.AllDefsListForReading];
        try
        {
            DefDatabase<DutyDef>.Clear();
            var root = new ThinkNode_Priority();
            DefDatabase<DutyDef>.Add(new DutyDef { defName = "Mugirl_CorporateSupportHunt", thinkNode = root });
            LegacyThinkTreeKeys.Reset();
            LegacyThinkCopyScope outer = LegacyThinkLayouts.EnterCopy(root);
            try
            {
                Assert.True(LegacyThinkLayouts.InsideOmittedDuty);
                LegacyThinkCopyScope inner = LegacyThinkLayouts.EnterCopy(new StableGiver());
                try { throw new InvalidOperationException("control"); }
                catch (InvalidOperationException) { }
                finally { LegacyThinkLayouts.LeaveCopy(inner); }
                Assert.True(LegacyThinkLayouts.InsideOmittedDuty);
            }
            finally { LegacyThinkLayouts.LeaveCopy(outer); }
            Assert.False(LegacyThinkLayouts.InsideOmittedDuty);
        }
        finally
        {
            LegacyThinkTreeKeys.Reset();
            DefDatabase<DutyDef>.Clear();
            foreach (DutyDef definition in original) { DefDatabase<DutyDef>.Add(definition); }
        }
    }

    [Fact]
    public void ActualDeepCopyExceptionUnwindsEveryHistoricalScopeWithoutSwallowingTheException()
    {
        DutyDef[] original = [.. DefDatabase<DutyDef>.AllDefsListForReading];
        var harmony = new Harmony("Mugirl.Tests.ThinkTreeHistoricalCopyFailure");
        try
        {
            DefDatabase<DutyDef>.Clear();
            var root = Priority(new ThrowsDuringCopy());
            DefDatabase<DutyDef>.Add(new DutyDef { defName = "Mugirl_CorporateSupportHunt", thinkNode = root });
            LegacyThinkTreeKeys.Reset();
            harmony.CreateClassProcessor(typeof(Harmony_ThinkNode_DeepCopy_LegacyLayout)).Patch();
            ThrowsDuringCopy.Throw = true;
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => root.DeepCopy());
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.False(LegacyThinkLayouts.InsideOmittedDuty);

            ThrowsDuringCopy.Throw = false;
            ThinkNode independent = new ThinkNode_Priority().DeepCopy();
            Assert.True(LegacyThinkLayouts.ExistedBeforeCorporateDuty(independent));
        }
        finally
        {
            ThrowsDuringCopy.Throw = false;
            harmony.UnpatchAll(harmony.Id);
            ThinkTreeKeyAssigner.Reset();
            LegacyThinkTreeKeys.Reset();
            DefDatabase<DutyDef>.Clear();
            foreach (DutyDef definition in original) { DefDatabase<DutyDef>.Add(definition); }
        }
    }

    private static Dictionary<(string Tree, string Path), ThinkNode> Build(bool modern, bool firstPublished)
    {
        DefDatabase<DutyDef>.Clear();
        DefDatabase<DutyDef>.Add(new DutyDef { defName = "ExistingDuty", thinkNode = Priority(new StableGiver(), new StableGiver()) });
        if (modern)
        {
            DefDatabase<DutyDef>.Add(new DutyDef { defName = "Mugirl_CorporateSupportHunt", thinkNode = Priority(new StableGiver(), new ThinkNode_Priority()) });
        }
        ThinkTreeKeyAssigner.Reset();
        LegacyThinkTreeKeys.Reset();
        List<ThinkTreeDef> trees = [new ThinkTreeDef { defName = "Initial", thinkRoot = Priority(new ThinkNode_Duty(), new StableGiver()) }];
        if (modern || !firstPublished)
        {
            trees.Add(new ThinkTreeDef { defName = modern ? "Mugirl_Punisher" : "MugirlPunisher",
                thinkRoot = Priority(modern ? new JobGiver_RegisterPunisher() : new JobGiver_MugirlPunisher(), new ThinkNode_Duty()) });
        }
        ThinkNode first = modern || !firstPublished ? new ThinkNode_ConditionalNonPlayerWildMugirl() : new ThinkNode_ConditionalPawnKind();
        ThinkNode second = modern || !firstPublished ? new ThinkNode_ConditionalNonPlayerWildMugirl() : new ThinkNode_ConditionalPawnKind();
        first.subNodes.Add(new StableGiver());
        second.subNodes.Add(new StableGiver());
        trees.Add(new ThinkTreeDef { defName = "MugirlLike", thinkRoot = Priority(first, new ThinkNode_Duty(), second, new StableGiver()) });
        trees.Add(new ThinkTreeDef { defName = "Animal", thinkRoot = Priority(new ThinkNode_Duty(), new StableGiver(), new StableGiver()) });
        var result = new Dictionary<(string, string), ThinkNode>();
        foreach (ThinkTreeDef tree in trees)
        {
            tree.ResolveReferences();
            Add(tree.thinkRoot, string.Empty);
            void Add(ThinkNode node, string path)
            {
                result.Add((tree.defName == "MugirlPunisher" ? "Mugirl_Punisher" : tree.defName, path), node);
                for (int i = 0; i < node.subNodes.Count; i++) { Add(node.subNodes[i], path + "/" + i); }
            }
        }
        return result;
    }

    private static ThinkNode_Priority Priority(params ThinkNode[] nodes) => new() { subNodes = [.. nodes] };
    private sealed class ThrowsDuringCopy : ThinkNode_Priority
    {
        internal static bool Throw;
        public ThrowsDuringCopy()
        {
            if (Throw) { throw new InvalidOperationException("copy constructor control"); }
        }
    }
    private sealed class StableGiver : ThinkNode_JobGiver
    {
        public StableGiver() { }
        protected override Job? TryGiveJob(Pawn pawn) => throw new InvalidOperationException("The key replay must not execute a job giver.");
    }
    private sealed class JobGiver_MugirlPunisher : ThinkNode_JobGiver
    {
        public JobGiver_MugirlPunisher() { }
        protected override Job? TryGiveJob(Pawn pawn) => throw new InvalidOperationException("The key replay must not execute a job giver.");
    }
}
