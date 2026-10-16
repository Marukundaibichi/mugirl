namespace Mugirl.Features.Mounting;

// 载具离开地图或被销毁时放下骑手；读档生成后恢复炮塔 verb 的施放者登记。
public sealed partial class CompMount
{
    private const int FallbackDropRadius = 5;

    public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
    {
        base.PostDeSpawn(map, mode);
        if (HasMountedPawn && mode != DestroyMode.WillReplace && map is not null)
        {
            TryDropAt(parent.PositionHeld, map, mode);
        }
    }

    public override void PostDestroy(DestroyMode mode, Map previousMap)
    {
        base.PostDestroy(mode, previousMap);
        if (!HasMountedPawn)
        {
            return;
        }

        if (previousMap is not null)
        {
            TryDropAt(parent.PositionHeld, previousMap, mode);
        }
        else
        {
            MountedCombatController.NotifyDismounting(this, MountedPawn);
            mugirlMountRiders.ClearAndDestroyContentsOrPassToWorld(mode);
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

    private void TryDropAt(IntVec3 cell, Map map, DestroyMode fallbackMode)
    {
        Pawn? rider = MountedPawn;
        if (rider is null)
        {
            return;
        }

        MountedCombatController.NotifyDismounting(this, rider);
        Pawn? carrier = MooPawn;
        bool IsValid(IntVec3 c) => DismountCellFinder.IsValidCell(c, carrier, rider, map);

        if (cell.IsValid && mugirlMountRiders.TryDrop(rider, cell, map, ThingPlaceMode.Near, out Thing _, null, IsValid, playDropSound: true))
        {
            return;
        }

        if (CellFinder.TryFindRandomCellNear(cell, map, FallbackDropRadius, IsValid, out IntVec3 fallbackCell)
            && mugirlMountRiders.TryDrop(rider, fallbackCell, map, ThingPlaceMode.Near, out Thing _, null, IsValid, playDropSound: true))
        {
            return;
        }

        MugirlLog.WarningOnce(
            "Mount.DropAtFailed",
            "Mugirl.Mount.DropAtFailed".Translate(rider.LabelShortCap, parent.LabelShortCap).ToString());
        mugirlMountRiders.ClearAndDestroyContentsOrPassToWorld(fallbackMode);
    }
}
