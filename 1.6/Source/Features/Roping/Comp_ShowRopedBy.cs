using RimWorld;
using Verse;

namespace MooGirl
{
    public class CompProperties_ShowRopedBy : CompProperties
    {
        public CompProperties_ShowRopedBy()
        {
            this.compClass = typeof(CompShowRopedBy);
        }
    }

    public class CompShowRopedBy : ThingComp
    {
        public Pawn Pawn => this.parent as Pawn;

        public override string CompInspectStringExtra()
        {
            if (Pawn == null || Pawn.roping == null)
            {
                return null;
            }

            Pawn_RopeTracker roping = Pawn.roping;

            // 被其他角色牵引。
            if (roping.IsRopedByPawn)
            {
                return "MooGirl.RopedByPawn".Translate();
            }

            // 被原版拴点牵引。
            if (roping.IsRopedToHitchingPost)
            {
                return "MooGirl.RopedToHitchingPost".Translate();
            }

            // 被地图坐标牵引。
            if (roping.IsRopedToSpot)
            {
                return "MooGirl.RopedToSpot".Translate();
            }

            return null;
        }
    }
}
