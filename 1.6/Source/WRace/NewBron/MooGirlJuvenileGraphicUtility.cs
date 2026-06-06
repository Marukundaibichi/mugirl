using RimWorld;
using Verse;

namespace MooGirl
{
    internal static class MooGirlJuvenileGraphicUtility
    {
        public static bool NormalizeBodyType(Pawn pawn)
        {
            if (!ModsConfig.BiotechActive || pawn?.story == null || !IsMooGirl(pawn))
            {
                return false;
            }

            BodyTypeDef expectedBodyType = null;
            if (pawn.DevelopmentalStage.Baby() || pawn.DevelopmentalStage.Newborn())
            {
                expectedBodyType = BodyTypeDefOf.Baby;
            }
            else if (pawn.DevelopmentalStage.Child())
            {
                expectedBodyType = BodyTypeDefOf.Child;
            }

            if (expectedBodyType == null || pawn.story.bodyType == expectedBodyType)
            {
                return false;
            }

            pawn.story.bodyType = expectedBodyType;
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            PortraitsCache.SetDirty(pawn);
            return true;
        }

        public static int NormalizeLoadedPawns()
        {
            int changed = 0;
            var pawns = PawnsFinder.AllMapsWorldAndTemporary_Alive;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (NormalizeBodyType(pawns[i]))
                {
                    changed++;
                }
            }

            return changed;
        }

        private static bool IsMooGirl(Pawn pawn)
        {
            return pawn?.def == MooGirl_DefOf.MooGirl || pawn?.RaceProps?.body == MooGirl_DefOf.MooGirlBody;
        }
    }
}
