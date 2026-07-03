using RimWorld;
using Verse;

namespace Mugirl
{
    internal static class Mugirl_IdeoUtility
    {
        internal static void AdoptPlayerPrimaryIdeo(Pawn pawn)
        {
            if (pawn == null || !ModsConfig.IdeologyActive)
            {
                return;
            }

            Faction playerFaction = Faction.OfPlayerSilentFail;
            Ideo primaryIdeo = playerFaction?.ideos?.PrimaryIdeo;
            if (primaryIdeo == null || pawn.ideo == null || pawn.Ideo == primaryIdeo)
            {
                return;
            }

            pawn.ideo.SetIdeo(primaryIdeo);
        }
    }
}
