using RimWorld;
using Verse;

namespace MooGirl
{
    public class ThoughtWorker_Roped : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p?.Map == null || !p.Spawned)
                return ThoughtState.Inactive;

            // 必须是 MooGirl + 正在执行跟随工作
            if (p.RaceProps?.body == MooGirl_DefOf.MooGirlBody &&
                p.CurJob?.def == MooGirl_DefOf.Job_FollowRoper)
            {
                // 直接激活 Stage 0
                return ThoughtState.ActiveAtStage(0);
            }

            return ThoughtState.Inactive;
        }
    }
}
