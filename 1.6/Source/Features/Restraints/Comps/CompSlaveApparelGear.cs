using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class CompSlaveApparelGear : CompUsable
    {
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
        {
            if (MooGirlGameUtility.IsCurrentMap(pawn.Map))
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
                    foreach (Pawn other in pawn.Map.mapPawns.AllPawnsSpawned)
                    {
                        if ((other != pawn) && other.IsWearingSlaveApparel() && (other.Downed || other.IsPrisonerOfColony || PawnSlaveStatusUtility.IsSlave(other)))
                        {
                            yield return this.MakeUnlockOption(
                                "MooGirl.FloatMenu.ActionOnTarget".Translate(FloatMenuOptionLabel(pawn), PawnSlaveStatusUtility.DisplayName(other)),
                                pawn,
                                other,
                                (other.IsPrisonerOfColony || PawnSlaveStatusUtility.IsSlave(other)) ? WorkTypeDefOf.Warden : null);
                        }
                    }
                }
            }
        }

    }
}
