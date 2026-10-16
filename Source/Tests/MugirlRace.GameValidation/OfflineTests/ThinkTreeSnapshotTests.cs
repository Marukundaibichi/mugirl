using System.Reflection;
using System.Text.Json.Nodes;
using Mugirl.GameValidation.Core;
using Verse.AI;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

[Collection("A5 real Scribe")]
public sealed class ThinkTreeSnapshotTests
{
    [Fact]
    public void ActualOrderKeysParentsAndConfigurationAreReadWithoutNodeMethods()
    {
        var first = new ProbeNode { Config = "literal null  ", Definition = new ThingDef { defName = "ConfiguredDef" } };
        var child = new ProbeNode { parent = first };
        first.subNodes.Add(child);
        FieldInfo key = typeof(ThinkNode).GetField("uniqueSaveKeyInt", BindingFlags.Instance | BindingFlags.NonPublic)!;
        key.SetValue(first, 123);
        key.SetValue(child, -456);
        var second = new ProbeNode();
        JsonObject capture = ThinkTreeSnapshot.Capture([new ThinkTreeDef { defName = "Z", thinkRoot = first }, new ThinkTreeDef { defName = "A", thinkRoot = second }], null);
        JsonArray trees = capture["trees"]!.AsArray();
        Assert.Equal("Z", trees[0]!["defName"]!.GetValue<string>());
        Assert.Equal("A", trees[1]!["defName"]!.GetValue<string>());
        JsonArray nodes = trees[0]!["nodes"]!.AsArray();
        Assert.Equal("[]", nodes[0]!["path"]!.ToJsonString());
        Assert.Equal("[0]", nodes[1]!["path"]!.ToJsonString());
        Assert.Equal("[]", nodes[1]!["parentPath"]!.ToJsonString());
        Assert.Equal(-456, nodes[1]!["uniqueSaveKey"]!.GetValue<int>());
        JsonArray fields = nodes[0]!["fields"]!.AsArray();
        Assert.Equal("literal null  ", fields.Single(f => f!["name"]!.GetValue<string>() == nameof(ProbeNode.Config))!["value"]!.GetValue<string>());
        Assert.Equal("ConfiguredDef", fields.Single(f => f!["name"]!.GetValue<string>() == nameof(ProbeNode.Definition))!["value"]!["defName"]!.GetValue<string>());
        Assert.Equal(typeof(DangerousObject).FullName, fields.Single(f => f!["name"]!.GetValue<string>() == nameof(ProbeNode.Unknown))!["value"]!["unsupportedType"]!.GetValue<string>());
        Assert.Equal(123, (int)key.GetValue(first)!);
        Assert.Same(first, child.parent);
        Assert.Single(first.subNodes);
        Assert.Empty(capture["errors"]!.AsArray());
    }

    [Fact]
    public void RepeatedNodeAndCollectionCyclesAreRecordedWithoutRecursion()
    {
        var root = new ProbeNode();
        root.subNodes.Add(root);
        root.Values.Add(root.Values);
        JsonObject capture = ThinkTreeSnapshot.Capture([new ThinkTreeDef { defName = "Cycle", thinkRoot = root }], null);
        Assert.Single(capture["errors"]!.AsArray());
        Assert.Contains("cycleType", capture.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("referencePath", capture.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ShadowProfilesReadExistingReferenceDictionariesWithoutReplayingKeysOrRandom()
    {
        var node = new ProbeNode();
        Profiles.Set(node);
        var errors = new JsonArray();
        Rand.PushState(32451);
        int expected = Rand.Int;
        Rand.PopState();
        Rand.PushState(32451);
        try
        {
            IReadOnlyDictionary<string, Dictionary<ThinkNode, int>> result = ThinkTreeSnapshot.ReadProfiles(typeof(Profiles), errors);
            Assert.Empty(errors);
            Assert.Equal(11, result["Ledger"][node]);
            Assert.Equal(22, result["BeforeCorporateDuty"][node]);
            Assert.Equal(33, result["FirstPublishedLayout"][node]);
            Assert.Equal(expected, Rand.Int);
            result["Ledger"][node] = 44;
            Assert.Equal(11, ThinkTreeSnapshot.ReadProfiles(typeof(Profiles), errors)["Ledger"][node]);
        }
        finally { Rand.PopState(); }
    }

    [Fact]
    public void MissingAndValueHashBasedShadowLedgersRemainErrors()
    {
        var errors = new JsonArray();
        Assert.Empty(ThinkTreeSnapshot.ReadProfiles(null, errors));
        Assert.Empty(errors);
        Assert.Empty(ThinkTreeSnapshot.ReadProfiles(typeof(BadProfiles), errors));
        Assert.Equal(3, errors.Count);
    }

    private sealed class LedgerData(Dictionary<ThinkNode, int> values)
    {
        private readonly Dictionary<ThinkNode, int> currentAssignments = values;
        public Dictionary<ThinkNode, int> Values => currentAssignments;
    }

    private static class Profiles
    {
        private static readonly LedgerData Ledger = new(new Dictionary<ThinkNode, int>(ReferenceEqualityComparer.Instance));
        private static readonly LedgerData BeforeCorporateDuty = new(new Dictionary<ThinkNode, int>(ReferenceEqualityComparer.Instance));
        private static readonly LedgerData FirstPublishedLayout = new(new Dictionary<ThinkNode, int>(ReferenceEqualityComparer.Instance));
        public static void Set(ThinkNode node)
        {
            Ledger.Values.Clear(); BeforeCorporateDuty.Values.Clear(); FirstPublishedLayout.Values.Clear();
            Ledger.Values.Add(node, 11); BeforeCorporateDuty.Values.Add(node, 22); FirstPublishedLayout.Values.Add(node, 33);
        }
    }

    private static class BadProfiles
    {
        private static readonly LedgerData Ledger = new(new Dictionary<ThinkNode, int>());
        public static object KeepFieldReferenced => Ledger;
    }

    private sealed class ProbeNode : ThinkNode
    {
        public string Config = "value";
        public Def? Definition;
        public readonly object Unknown = new DangerousObject();
        public readonly List<object> Values = [];
        public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams) => throw new InvalidOperationException("Must not execute");
        public override void ResolveReferences() => throw new InvalidOperationException("Must not resolve");
        public override ThinkNode DeepCopy(bool resolve = true) => throw new InvalidOperationException("Must not copy");
        public override string ToString() => throw new InvalidOperationException("Must not stringify");
    }

    private sealed class DangerousObject
    {
        public override string ToString() => throw new InvalidOperationException("Must not stringify unknown configuration");
    }
}
