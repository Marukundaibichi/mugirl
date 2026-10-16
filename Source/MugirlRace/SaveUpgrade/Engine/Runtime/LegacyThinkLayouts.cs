using System.Linq;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

/// <summary>已发布 0.4.7/0.5.0 尚无巨企支援职责；仅在独立键重放中省略后来加入的节点。</summary>
internal static class LegacyThinkLayouts
{
    private const string IntroducedDuty = "Mugirl_CorporateSupportHunt";
    internal const string IntroducedTree = "Mugirl_Punisher";
    // StaticCacheLifecycle: Def 键重建时清空。只记录当前图和其真实 DeepCopy 的对象身份，不改图。
    private static readonly HashSet<ThinkNode> omittedDutyNodes = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<ThinkNode> earlierConditions = new(ReferenceEqualityComparer.Instance);
    private static ThinkNode? dutyRoot;
    private static bool dutyRead;
    [ThreadStatic]
    private static bool insideOmittedDuty;
    [ThreadStatic]
    private static bool copyingEarlierCondition;

    internal static bool InsideOmittedDuty => insideOmittedDuty;

    internal static void Reset(ThinkTreeDef? activeTree = null)
    {
        omittedDutyNodes.Clear();
        earlierConditions.Clear();
        dutyRoot = null;
        dutyRead = false;
        // 不清除正在展开的调用链状态：原版 Reset 被内联时，首个 AssignSingleKey 的空集合也会触发重建。
        if (activeTree is not null) { EnterTree(activeTree); }
    }

    internal static void EnterTree(ThinkTreeDef tree)
    {
        if (tree.defName != "MugirlLike" || tree.thinkRoot is not ThinkNode_Priority root) { return; }
        ThinkNode[] conditions = [.. root.subNodes.Where(n => n.GetType() == typeof(Features.Incidents.ThinkNode_ConditionalNonPlayerWildMugirl))];
        if (conditions.Length == 2)
        {
            foreach (ThinkNode node in conditions) { earlierConditions.Add(node); }
        }
    }

    internal static LegacyThinkCopyScope EnterCopy(ThinkNode source)
    {
        if (!dutyRead)
        {
            dutyRoot = DefDatabase<DutyDef>.GetNamedSilentFail(IntroducedDuty)?.thinkNode;
            dutyRead = true;
        }
        bool omitted = insideOmittedDuty || ReferenceEquals(source, dutyRoot) || omittedDutyNodes.Contains(source);
        var scope = new LegacyThinkCopyScope(insideOmittedDuty, omitted, copyingEarlierCondition, earlierConditions.Contains(source));
        insideOmittedDuty = omitted;
        copyingEarlierCondition = scope.EarlierCondition;
        return scope;
    }

    internal static void Copied(ThinkNode result, LegacyThinkCopyScope scope)
    {
        if (scope.OmittedDuty) { omittedDutyNodes.Add(result); }
        if (scope.EarlierCondition) { earlierConditions.Add(result); }
    }

    internal static void AssigningCopy(ThinkNode node)
    {
        // 基类 DeepCopy 在返回（Postfix）之前就给新对象分配键，因此需在该分配入口传递源节点的历史标签。
        if (copyingEarlierCondition) { earlierConditions.Add(node); }
    }

    internal static void LeaveCopy(LegacyThinkCopyScope scope)
    {
        insideOmittedDuty = scope.PreviousOmittedDuty;
        copyingEarlierCondition = scope.PreviousEarlierCondition;
    }

    internal static bool ExistedBeforeCorporateDuty(ThinkNode node) => !omittedDutyNodes.Contains(node);
    internal static bool WasPawnKindCondition(ThinkNode node) => earlierConditions.Contains(node);
}
