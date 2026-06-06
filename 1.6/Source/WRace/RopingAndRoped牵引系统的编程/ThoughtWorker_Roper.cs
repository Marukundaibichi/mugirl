using RimWorld;
using Verse;

namespace MooGirl
{
    public class ThoughtWorker_Roper : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p?.Map == null || !p.Spawned)
            {
                return ThoughtState.Inactive;
            }

            // 思想只关心当前牵引者名下的关系，避免每次计算扫描玩家派系。
            int followCount = RopingService.CountMooGirlFollowers(p, 2);
            if (followCount == 0)
            {
                return ThoughtState.Inactive;
            }

            return ThoughtState.ActiveAtStage(followCount == 1 ? 0 : 1);
        }
    }
}
