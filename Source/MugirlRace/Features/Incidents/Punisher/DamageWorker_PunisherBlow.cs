namespace Mugirl.Features.Incidents;

// XML DamageDef workerClass：惩罚者的拳击对 Pawn 只造成少量伤害并击昏玩家的人形 Pawn，对建筑造成大量伤害。
public class DamageWorker_PunisherBlow : DamageWorker_Blunt
{
    private const float PawnDamage = 4f;
    private const float BuildingDamage = 350f;

    public override DamageResult Apply(DamageInfo dinfo, Thing thing)
    {
        bool fromPunisher = dinfo.Instigator is Pawn instigator && instigator.def == IncidentsDefOf.Mugirl_PunisherRace;
        Pawn? pawn = thing as Pawn;
        if (fromPunisher && pawn != null)
        {
            dinfo.SetAmount(PawnDamage);
        }
        else if (fromPunisher && thing is Building)
        {
            dinfo.SetAmount(BuildingDamage);
        }

        DamageResult result = base.Apply(dinfo, thing);
        if (fromPunisher && pawn != null && !pawn.Dead
            && PlayerFactionUtility.IsPlayerFaction(pawn.Faction) && pawn.RaceProps.Humanlike)
        {
            HediffDef knockoutDef = IncidentsDefOf.Mugirl_PunisherKnockout;
            Hediff? existing = pawn.health.hediffSet.GetFirstHediffOfDef(knockoutDef);
            if (existing == null)
            {
                pawn.health.AddHediff(knockoutDef);
            }
            else
            {
                existing.Severity = knockoutDef.initialSeverity;
            }
        }

        return result;
    }
}
