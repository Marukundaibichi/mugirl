using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Mugirl
{
    public partial class Comp_MugirlMount
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
                defaultLabel = "Mugirl.Mount.SelectRider".Translate(),
                defaultDesc = "Mugirl.Mount.SelectRiderDesc".Translate(),
                icon = TexCommand.SelectCarriedPawn,
                action = delegate
                {
                    Pawn currentRider = MountedPawn;
                    if (currentRider == null)
                    {
                        return;
                    }

                    MugirlSelectionUtility.SelectInPlaying(currentRider);
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "Mugirl.Mount.DismountRider".Translate(),
                defaultDesc = "Mugirl.Mount.DismountRiderDesc".Translate(),
                icon = TexCommand.DropCarriedPawn,
                action = delegate
                {
                    TryDismount();
                }
            };

            yield return new Command_Toggle
            {
                defaultLabel = fireAtWill ? "Mugirl.Mount.FireAtWill".Translate() : "Mugirl.Mount.HoldFire".Translate(),
                defaultDesc = fireAtWill ? "Mugirl.Mount.FireAtWillDesc".Translate() : "Mugirl.Mount.HoldFireDesc".Translate(),
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

            return "Mugirl.Mount.InspectRider".Translate(rider.LabelShort);
        }
    }
}
