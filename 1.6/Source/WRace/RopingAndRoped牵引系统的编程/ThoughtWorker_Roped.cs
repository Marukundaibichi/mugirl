using RimWorld;
using Verse;

namespace MooGirl
{
    public class ThoughtWorker_Roped : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p?.Map == null || !p.Spawned)
            {
                return ThoughtState.Inactive;
            }

            // 被牵引心情保持旧判定：雪牛娘身体且正在跟随牵引者。
            if (RopingService.IsMooGirlRopee(p) && RopingService.IsFollowingRoper(p))
            {
                return ThoughtState.ActiveAtStage(0);
            }

            return ThoughtState.Inactive;
        }
    }
}
