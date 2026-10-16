using System.Diagnostics.CodeAnalysis;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

/// <summary>在原键分配时点重放旧名字的键空间；只读节点，不修改原版的键集合或随机状态。</summary>
internal sealed class LegacyThinkTreeLedger(Func<ThinkNode, string> shortName)
{
    private readonly HashSet<int> assigned = [];
    private readonly Dictionary<int, ThinkNode> nodes = [];
    // ThinkNode.GetHashCode 取决于可变 uniqueSaveKeyInt，分配前后的对象索引必须按引用比较。
    private readonly Dictionary<ThinkNode, int> currentAssignments = new(ReferenceEqualityComparer.Instance);

    internal int AssignedCount => assigned.Count;

    internal void Reset()
    {
        assigned.Clear();
        nodes.Clear();
        currentAssignments.Clear();
    }

    internal void Capture(IEnumerable<ThinkNode> sequence, int seed)
    {
        Rand.PushState(seed);
        try
        {
            foreach (ThinkNode node in sequence)
            {
                int key = 0;
                for (ThinkNode? parent = node; parent is not null; parent = parent.parent)
                {
                    key = Gen.HashCombineInt(key, GenText.StableStringHash(shortName(parent)));
                }

                while (!assigned.Add(key))
                {
                    key ^= Rand.Int;
                }

                // 同一个节点之后可能重新分配；旧占用仍影响碰撞，但旧的临时身份不会写进最终存档。
                if (currentAssignments.TryGetValue(node, out int previous))
                {
                    nodes.Remove(previous);
                }

                currentAssignments[node] = key;
                nodes.Add(key, node);
            }
        }
        finally
        {
            Rand.PopState();
        }
    }

    internal bool TryNode(int legacyKey, [NotNullWhen(true)] out ThinkNode? node) => nodes.TryGetValue(legacyKey, out node);

    internal int KeyFor(ThinkNode node) => currentAssignments[node];
}
