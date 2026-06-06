using RimWorld;
using Verse;

namespace MooGirl
{
    internal static class MooGirlRescueJoinUtility
    {
        public static void PrepareRescueJoinPawn(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            pawn.mindState.WillJoinColonyIfRescued = true;
            if (pawn.guest != null)
            {
                pawn.guest.Recruitable = true;
            }
        }

        public static bool WasRescuedByPlayer(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            if (pawn.HostFaction == Faction.OfPlayer)
            {
                return true;
            }

            Building_Bed bed = pawn.CurrentBed();
            return bed != null && bed.Faction == Faction.OfPlayer;
        }

        public static bool TryJoinPlayer(Pawn pawn, Pawn rescuer = null, bool sendLetter = true)
        {
            if (pawn == null || pawn.Dead || pawn.Faction == Faction.OfPlayer)
            {
                return false;
            }

            if (pawn.RaceProps?.Humanlike != true)
            {
                return false;
            }

            pawn.mindState.WillJoinColonyIfRescued = false;
            MooGirl_IdeoUtility.AdoptPlayerPrimaryIdeo(pawn);
            bool wasEscapeWildSlave = MooGirlWildSlaveUtility.IsEscapeWildSlave(pawn);

            string letterLabel;
            string letterText;
            InteractionWorker_RecruitAttempt.DoRecruit(
                rescuer,
                pawn,
                out letterLabel,
                out letterText,
                useAudiovisualEffects: false,
                sendLetter: false);

            MooGirl_IdeoUtility.AdoptPlayerPrimaryIdeo(pawn);
            if (pawn.Faction == Faction.OfPlayer)
            {
                MooGirlWildSlaveUtility.NormalizeAfterJoiningPlayer(pawn, wasEscapeWildSlave);
            }

            if (pawn.needs?.mood != null)
            {
                pawn.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.Rescued);
            }

            if (sendLetter && pawn.Faction == Faction.OfPlayer)
            {
                Find.LetterStack.ReceiveLetter(
                    "LetterLabelRescueeJoins".Translate(pawn.Named("PAWN")),
                    "LetterRescueeJoins".Translate(pawn.Named("PAWN")),
                    LetterDefOf.PositiveEvent,
                    pawn);
            }

            return pawn.Faction == Faction.OfPlayer;
        }
    }
}
