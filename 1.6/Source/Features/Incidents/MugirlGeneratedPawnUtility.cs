using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Mugirl
{
    internal static class MugirlGeneratedPawnUtility
    {
        private const string MugirlSlaveBackstoryCategory = "Mugirl_Slave";

        public static bool HasMugirlSlaveBackstories(Pawn pawn)
        {
            if (pawn?.story == null)
            {
                return false;
            }

            return BackstoryHasCategory(pawn.story.Childhood, MugirlSlaveBackstoryCategory)
                && BackstoryHasCategory(pawn.story.Adulthood, MugirlSlaveBackstoryCategory);
        }

        private static bool BackstoryHasCategory(BackstoryDef backstory, string category)
        {
            return backstory?.spawnCategories != null
                && backstory.spawnCategories.Contains(category);
        }

        public static bool TryPassToWorld(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return false;
            }

            if (pawn.IsWorldPawn())
            {
                return true;
            }

            if (pawn.Spawned)
            {
                MugirlLog.WarningOnce(
                    "GeneratedPawnPassToWorldSpawned",
                    "Tried to pass a spawned generated pawn to world storage; leaving it on its current map.");
                return false;
            }

            return MugirlGameUtility.TryPassToWorld(pawn);
        }

        public static void Discard(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            if (pawn.Spawned)
            {
                MugirlLog.WarningOnce(
                    "GeneratedPawnDiscardSpawned",
                    "Tried to discard a spawned generated pawn; leaving it on its current map.");
                return;
            }

            MugirlGameUtility.TryRemoveWorldPawn(pawn);

            MugirlGameUtility.TryPassToWorldForDiscard(pawn);
        }
    }
}
