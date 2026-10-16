namespace Mugirl.Features.Restraints;

public class HediffComp_AddThought : HediffComp_DelayedTrigger
{
    public HediffCompProperties_AddThought? Props => props as HediffCompProperties_AddThought;

    protected override int? ConfiguredTriggerTicks => Props?.triggerTicks;

    protected override string AgeScribeLabel => "mugirlAddThoughtAge";

    protected override string TriggeredScribeLabel => "mugirlAddThoughtTriggered";

    protected override bool TryTrigger(Pawn pawn)
    {
        if (!pawn.Spawned
            || pawn.needs?.mood == null
            || !pawn.IsWearingCrackedBrainwashApparel()
            || !PlayerFactionUtility.IsPlayerFaction(pawn.Faction)
            || !pawn.IsColonist)
        {
            return false;
        }

        ThoughtDef? thoughtDef = Props?.thoughtDef;
        MemoryThoughtHandler? memories = pawn.needs.mood.thoughts?.memories;
        if (thoughtDef != null && memories != null)
        {
            memories.TryGainMemory(thoughtDef);
        }

        return true;
    }
}
