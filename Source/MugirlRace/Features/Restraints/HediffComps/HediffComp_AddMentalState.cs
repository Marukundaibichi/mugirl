namespace Mugirl.Features.Restraints;

public class HediffComp_AddMentalState : HediffComp_DelayedTrigger
{
    public HediffCompProperties_AddMentalState? Props => props as HediffCompProperties_AddMentalState;

    protected override int? ConfiguredTriggerTicks => Props?.mentalStateTriggerTicks;

    protected override string AgeScribeLabel => "mugirlAddMentalStateAge";

    protected override string TriggeredScribeLabel => "mugirlAddMentalStateTriggered";

    // 条件满足即视为已触发（即使 Pawn 此刻无法进入精神状态），与 1.6 相同。
    protected override bool TryTrigger(Pawn pawn)
    {
        if (!PlayerFactionUtility.IsPlayerFaction(pawn.Faction) || !pawn.IsWearingUncrackedBrainwashApparel())
        {
            return false;
        }

        MentalStateDef? mentalStateDef = Props?.mentalStateDef;
        if (mentalStateDef != null && pawn.mindState?.mentalStateHandler != null && pawn.Spawned && !pawn.InMentalState)
        {
            // 1.7 原版没有 "TriggeredByHediff" 键，改用模组自己的 Keyed 文本。
            pawn.mindState.mentalStateHandler.TryStartMentalState(
                mentalStateDef,
                reason: "Mugirl.Restraints.TriggeredByHediff".Translate(),
                forceWake: true);
        }

        return true;
    }
}
