using RimWorld.Planet;
using Verse;

namespace MooGirl
{
    internal static class MooGirlGeneratedPawnUtility
    {
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
                MooGirlLog.WarningOnce(
                    "GeneratedPawnPassToWorldSpawned",
                    "Tried to pass a spawned generated pawn to world storage; leaving it on its current map.");
                return false;
            }

            return MooGirlGameUtility.TryPassToWorld(pawn);
        }

        public static void Discard(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            if (pawn.Spawned)
            {
                MooGirlLog.WarningOnce(
                    "GeneratedPawnDiscardSpawned",
                    "Tried to discard a spawned generated pawn; leaving it on its current map.");
                return;
            }

            MooGirlGameUtility.TryRemoveWorldPawn(pawn);

            MooGirlGameUtility.TryPassToWorldForDiscard(pawn);
        }
    }
}
