using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class CompSlaveApperalGear : CompUsable
    {
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
        {
            if ((pawn.Map != null) && (pawn.Map == Find.CurrentMap))
            {
                if (!pawn.CanReserve(parent))
                {
                    yield return new FloatMenuOption(
                        "MooGirl.FloatMenu.ActionOnReserved".Translate(FloatMenuOptionLabel(pawn), "MooGirl.Reserved".Translate()),
                        null,
                        MenuOptionPriority.DisabledOption);
                }
                else if (pawn.CanReach(parent, PathEndMode.Touch, Danger.Some))
                {
                    foreach (Pawn other in pawn.Map.mapPawns.AllPawns)
                    {
                        if ((other != pawn) && other.Spawned && (other.Downed || other.IsPrisonerOfColony || PawnBool.is_slave(other)))
                        {
                            yield return this.MakeUnlockOption(
                                "MooGirl.FloatMenu.ActionOnTarget".Translate(FloatMenuOptionLabel(pawn), PawnBool.get_pawnname(other)),
                                pawn,
                                other,
                                (other.IsPrisonerOfColony || PawnBool.is_slave(other)) ? WorkTypeDefOf.Warden : null);
                        }
                    }
                }
            }
        }

    }
}
