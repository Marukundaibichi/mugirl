using Verse;

namespace MooGirl
{
    public partial class Comp_MooGirlMount
    {
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            if (HasMountedPawn && mode != DestroyMode.WillReplace && map != null)
            {
                TryDropAt(parent.PositionHeld, map, mode);
            }
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            if (HasMountedPawn && previousMap != null)
            {
                TryDropAt(parent.PositionHeld, previousMap, mode);
            }
            else if (HasMountedPawn)
            {
                MountedCombatController.NotifyDismounting(this);
                innerContainer.ClearAndDestroyContentsOrPassToWorld(mode);
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (HasMountedPawn)
            {
                MountedCombatController.NotifyMounted(this);
            }
        }

        private void TryDropAt(IntVec3 cell, Map map, DestroyMode fallbackMode = DestroyMode.Vanish)
        {
            Pawn rider = MountedPawn;
            if (rider == null || map == null)
            {
                return;
            }

            MountedCombatController.NotifyDismounting(this);
            Pawn carrier = MooPawn;
            if (cell.IsValid && innerContainer.TryDrop(rider, cell, map, ThingPlaceMode.Near, out Thing _, null, c => MountedPawnUtility.DismountCellValidator(c, carrier, rider, map)))
            {
                return;
            }

            IntVec3 fallbackCell;
            bool foundFallback = CellFinder.TryFindRandomCellNear(
                cell,
                map,
                5,
                c => MountedPawnUtility.DismountCellValidator(c, carrier, rider, map),
                out fallbackCell);

            if (foundFallback && innerContainer.TryDrop(rider, fallbackCell, map, ThingPlaceMode.Near, out Thing _, null, c => MountedPawnUtility.DismountCellValidator(c, carrier, rider, map)))
            {
                return;
            }

            MooGirlLog.WarningOnce(
                "Mount.DropAtFailed",
                "MooGirl.Mount.DropAtFailed".Translate(rider.LabelShortCap, parent.LabelShortCap).ToString());
            innerContainer.ClearAndDestroyContentsOrPassToWorld(fallbackMode);
        }
    }
}
