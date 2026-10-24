namespace Mugirl.Features.Restraints;

public class HediffComp_AddTrait : HediffComp_DelayedTrigger
{
    public HediffCompProperties_AddTrait? Props => props as HediffCompProperties_AddTrait;

    protected override int? ConfiguredTriggerTicks => Props?.triggerTicks;

    protected override string AgeScribeLabel => "mugirlAddTraitAge";

    protected override string TriggeredScribeLabel => "mugirlAddTraitTriggered";

    // 未获得特质（身份不匹配或已有该特质）时每 tick 重试：同一洗脑阶段里囚犯可能刚被奴役，下一 tick 就匹配“奴隶”条目。
    protected override bool TryTrigger(Pawn pawn)
    {
        HediffCompProperties_AddTrait? compProps = Props;
        if (compProps == null || !pawn.Spawned || !pawn.IsWearingCrackedBrainwashApparel())
        {
            return false;
        }

        if (compProps.pawnTypeTraitEntries != null)
        {
            for (int i = 0; i < compProps.pawnTypeTraitEntries.Count; i++)
            {
                HediffCompProperties_AddTrait.PawnTypeTraitEntry? entry = compProps.pawnTypeTraitEntries[i];
                if (entry?.trait != null
                    && MatchesPawnType(pawn, entry.pawnType)
                    && TryGainTrait(pawn, entry.trait))
                {
                    return true;
                }
            }
        }

        return TryGainTrait(pawn, compProps.traitDef);
    }

    private static bool TryGainTrait(Pawn pawn, TraitDef? traitDef)
    {
        TraitSet? traits = pawn.story?.traits;
        if (traits == null || traitDef == null || traits.HasTrait(traitDef))
        {
            return false;
        }

        traits.GainTrait(new Trait(traitDef));
        return true;
    }

    private static bool MatchesPawnType(Pawn pawn, RestraintPawnType pawnType)
    {
        return pawnType switch
        {
            RestraintPawnType.Colonist => pawn.IsColonist,
            RestraintPawnType.Slave => pawn.IsSlave,
            RestraintPawnType.Prisoner => pawn.IsPrisoner,
            RestraintPawnType.Free => !pawn.IsSlave && !pawn.IsPrisoner,
            RestraintPawnType.Hostile => PlayerFactionUtility.IsHostileToPlayer(pawn),
            _ => false,
        };
    }
}
