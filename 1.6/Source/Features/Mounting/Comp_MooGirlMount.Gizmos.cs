using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MooGirl
{
    public partial class Comp_MooGirlMount
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            Pawn rider = MountedPawn;
            if (rider == null)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "MooGirl.Mount.SelectRider".Translate(),
                defaultDesc = "MooGirl.Mount.SelectRiderDesc".Translate(),
                icon = TexCommand.SelectCarriedPawn,
                action = delegate
                {
                    Pawn currentRider = MountedPawn;
                    if (currentRider == null)
                    {
                        return;
                    }

                    MooGirlSelectionUtility.SelectInPlaying(currentRider);
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "MooGirl.Mount.DismountRider".Translate(),
                defaultDesc = "MooGirl.Mount.DismountRiderDesc".Translate(),
                icon = TexCommand.DropCarriedPawn,
                action = delegate
                {
                    TryDismount();
                }
            };

            yield return new Command_Toggle
            {
                defaultLabel = fireAtWill ? "MooGirl.Mount.FireAtWill".Translate() : "MooGirl.Mount.HoldFire".Translate(),
                defaultDesc = fireAtWill ? "MooGirl.Mount.FireAtWillDesc".Translate() : "MooGirl.Mount.HoldFireDesc".Translate(),
                icon = fireAtWill ? TexCommand.FireAtWill : TexCommand.CannotShoot,
                isActive = () => fireAtWill,
                toggleAction = delegate
                {
                    fireAtWill = !fireAtWill;
                }
            };
        }

        public override string CompInspectStringExtra()
        {
            Pawn rider = MountedPawn;
            if (rider == null)
            {
                return null;
            }

            return "MooGirl.Mount.InspectRider".Translate(rider.LabelShort);
        }
    }
}
