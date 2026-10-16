using System.Linq;
using Verse.AI.Group;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 阶段 B：DOM 中重置了状态图的 Lord（见 <see cref="LordPolicyStep"/>）重新进入起始 LordToil。
/// </summary>
/// <remarks>
/// 读档时 <c>Lord.SetJob(loading: true)</c> 只按保存的下标设置当前 LordToil，不调用 <c>Init</c>/<c>UpdateAllDuties</c>，
/// 成员会一直保留旧状态图的职责直到下一次转移。阶段 B 时地图与成员都已生成，<c>GotoToil</c> 走正常流程初始化 LordToil 并更新职责。
/// </remarks>
internal static class LordResetPolicy
{
    internal const string StepId = "Framework.LordReset";

    internal static int Apply(IReadOnlySet<int> lordIds)
    {
        ArgumentNullException.ThrowIfNull(lordIds);
        if (lordIds.Count == 0)
        {
            return 0;
        }

        int changed = 0;
        foreach (Map map in Find.Maps)
        {
            foreach (Lord lord in map.lordManager.lords.ToList())
            {
                if (!lordIds.Contains(lord.loadID) || lord.Graph is not { lordToils.Count: > 0 } graph)
                {
                    continue;
                }

                try
                {
                    lord.GotoToil(graph.StartingToil);
                    changed++;
                }
                catch (Exception exception)
                {
                    MugirlLog.Exception("Save upgrade could not restart lord " + lord.loadID + " at its starting toil.", exception);
                }
            }
        }

        return changed;
    }
}
