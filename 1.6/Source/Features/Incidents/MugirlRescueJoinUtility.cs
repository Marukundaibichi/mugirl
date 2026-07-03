using RimWorld;
using Verse;

namespace Mugirl
{
    internal static class MugirlRescueJoinUtility
    {
        public static void PrepareRescueJoinPawn(Pawn pawn)
        {
            if (pawn?.mindState == null)
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
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return false;
            }

            if (MugirlWildSlaveUtility.IsPlayerFaction(pawn.HostFaction))
            {
                return true;
            }

            Building_Bed bed = pawn.CurrentBed();
            return bed != null && MugirlWildSlaveUtility.IsPlayerFaction(bed.Faction);
        }

        public static bool TryJoinPlayer(Pawn pawn, Pawn rescuer = null, bool sendLetter = true)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed || MugirlWildSlaveUtility.IsPlayerFaction(pawn.Faction) || pawn.mindState == null)
            {
                return false;
            }

            if (pawn.RaceProps?.Humanlike != true)
            {
                return false;
            }

            pawn.mindState.WillJoinColonyIfRescued = false;
            Mugirl_IdeoUtility.AdoptPlayerPrimaryIdeo(pawn);
            bool wasEscapeWildSlave = MugirlWildSlaveUtility.IsEscapeWildSlave(pawn);

            string letterLabel;
            string letterText;
            InteractionWorker_RecruitAttempt.DoRecruit(
                rescuer,
                pawn,
                out letterLabel,
                out letterText,
                useAudiovisualEffects: false,
                sendLetter: false);

            Mugirl_IdeoUtility.AdoptPlayerPrimaryIdeo(pawn);
            if (MugirlWildSlaveUtility.IsPlayerFaction(pawn.Faction))
            {
                MugirlWildSlaveUtility.NormalizeAfterJoiningPlayer(pawn, wasEscapeWildSlave);
            }

            if (pawn.needs?.mood?.thoughts?.memories != null)
            {
                pawn.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.Rescued);
            }

            if (sendLetter && MugirlWildSlaveUtility.IsPlayerFaction(pawn.Faction))
            {
                MugirlGameUtility.TryReceiveLetter(
                    "LetterLabelRescueeJoins".Translate(pawn.Named("PAWN")),
                    "LetterRescueeJoins".Translate(pawn.Named("PAWN")),
                    LetterDefOf.PositiveEvent,
                    pawn);
            }

            return MugirlWildSlaveUtility.IsPlayerFaction(pawn.Faction);
        }
    }
}
