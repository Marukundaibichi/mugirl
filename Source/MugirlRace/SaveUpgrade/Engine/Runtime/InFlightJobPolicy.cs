using System.Linq;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 旧档读入后的默认工作策略（阶段 B）：按 <see cref="InFlightJobSelection"/> 结束进行中的工作，移除排队的同类工作；
/// 被步骤认领的类型除外。
/// </summary>
/// <remarks>
/// JobDriver 在 PostLoadInit 用保存的 <c>curToilIndex</c> 重建 Toil，旧版构建的 Toil 序列可能不同，继续执行会跳到错误的 Toil。
/// 阶段 B 时地图与 Pawn 已生成，<c>EndCurrentJob(InterruptForced)</c> 走正常清理并释放预约；阶段 A 时 Pawn 尚未生成，结束工作会访问空地图。
/// 不立即开始新工作，Pawn 在下一次工作检查时自行选择。
/// </remarks>
internal static class InFlightJobPolicy
{
    internal const string StepId = "Framework.InFlightJobs";

    /// <param name="tables">本存档适用的表格视图（用于把旧类型名解析为当前类型）。</param>
    internal static int Apply(UpgradeTables tables, InFlightJobSelection selection)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.IsEmpty)
        {
            return 0;
        }

        HashSet<Type> ended = Resolve(tables, selection.Drivers);
        HashSet<Type> claimed = Resolve(tables, selection.Claimed);
        bool Ends(Type? driver) =>
            driver is not null && !claimed.Contains(driver) && ended.Contains(driver);

        int changed = 0;
        foreach (Map map in Find.Maps)
        {
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (pawn.jobs is not { } jobs)
                {
                    continue;
                }

                try
                {
                    if (jobs.curDriver is { } driver && Ends(driver.GetType()))
                    {
                        jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                        changed++;
                    }

                    int queued = jobs.jobQueue.Count;
                    jobs.jobQueue.RemoveAll(pawn, job => Ends(job?.def?.driverClass));
                    changed += queued - jobs.jobQueue.Count;
                }
                catch (Exception exception)
                {
                    MugirlLog.Exception("Save upgrade could not end the legacy job of " + pawn.ToStringSafe() + ".", exception);
                    // 由 RunAction 标记本次升级失败；不能在部分清理失败后盖上当前 schema。
                    throw;
                }
            }
        }

        return changed;
    }

    private static HashSet<Type> Resolve(UpgradeTables tables, IReadOnlyList<string> legacyNames)
    {
        HashSet<Type> types = [];
        foreach (string name in legacyNames)
        {
            Type? type = tables.FindType(name)?.New ?? GenTypes.GetTypeInAnyAssembly(name);
            if (type is not null)
            {
                types.Add(type);
            }
        }

        return types;
    }
}
