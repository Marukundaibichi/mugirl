using RimWorld;
using Verse;
using System.Collections.Generic;

namespace MooGirl
{
    // 定义一个继承自ThoughtWorker的类，用于处理特定角色的思考状态
    public class ThoughtWorker_Roper : ThoughtWorker
    {
        // 重写基类方法，返回当前角色的思考状态
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            // 检查角色是否存在地图且已生成，否则返回非激活状态
            if (p?.Map == null || !p.Spawned)
                return ThoughtState.Inactive;

            int followCount = 0; // 初始化跟随者计数器
            // 遍历玩家派系中所有已生成的pawn（可替换为AllPawns.Spawned以包含所有派系）
            foreach (var follower in p.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                // 跳过非MooGirlBody种族的pawn
                if (follower.RaceProps?.body != MooGirl_DefOf.MooGirlBody) continue;

                var job = follower.CurJob; // 获取当前pawn的工作
                // 检查pawn是否正在执行"跟随Roper"的工作且目标是指定角色p
                if (job?.def != MooGirl_DefOf.Job_FollowRoper) continue;
                if (job.targetA.Thing != p) continue;

                followCount++; // 有效跟随者计数增加
                if (followCount >= 2) break; // 已有2个跟随者时提前终止循环
            }

            // 根据跟随者数量返回不同的思考状态
            if (followCount == 0)
                return ThoughtState.Inactive; // 无跟随者时返回非激活状态
            // 1个跟随者返回阶段0，2个及以上返回阶段1
            return ThoughtState.ActiveAtStage(followCount == 1 ? 0 : 1);
        }
    }
}