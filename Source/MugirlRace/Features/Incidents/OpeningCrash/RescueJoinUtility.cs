namespace Mugirl.Features.Incidents;

internal static class RescueJoinUtility
{
    public static void PrepareRescueJoinPawn(Pawn? pawn)
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

    public static bool WasRescuedByPlayer(Pawn? pawn)
    {
        if (pawn == null || pawn.Destroyed || pawn.Dead)
        {
            return false;
        }

        if (PlayerFactionUtility.IsPlayerFaction(pawn.HostFaction))
        {
            return true;
        }

        Building_Bed? bed = pawn.CurrentBed();
        return bed != null && PlayerFactionUtility.IsPlayerFaction(bed.Faction);
    }

    public static bool TryJoinPlayer(Pawn? pawn, Pawn? rescuer = null, bool sendLetter = true)
    {
        if (pawn == null || pawn.Dead || pawn.Destroyed || PlayerFactionUtility.IsPlayerFaction(pawn.Faction) || pawn.mindState == null)
        {
            return false;
        }

        if (pawn.RaceProps?.Humanlike != true)
        {
            return false;
        }

        pawn.mindState.WillJoinColonyIfRescued = false;
        PlayerIdeoUtility.AdoptPlayerPrimaryIdeo(pawn);
        InteractionWorker_RecruitAttempt.DoRecruit(
            rescuer,
            pawn,
            out _,
            out _,
            useAudiovisualEffects: false,
            sendLetter: false);

        PlayerIdeoUtility.AdoptPlayerPrimaryIdeo(pawn);
        if (PlayerFactionUtility.IsPlayerFaction(pawn.Faction))
        {
            // SetFaction 补丁通常已清理过；补丁未能应用时这里仍保证事件状态被清除（重复调用无副作用）。
            WildMugirlUtility.CleanupAfterJoiningPlayer(pawn);
        }

        pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDefOf.Rescued);

        if (sendLetter && PlayerFactionUtility.IsPlayerFaction(pawn.Faction))
        {
            GameUtility.TryReceiveLetter(
                "LetterLabelRescueeJoins".Translate(pawn.Named("PAWN")),
                "LetterRescueeJoins".Translate(pawn.Named("PAWN")),
                LetterDefOf.PositiveEvent,
                pawn);
        }

        return PlayerFactionUtility.IsPlayerFaction(pawn.Faction);
    }
}
