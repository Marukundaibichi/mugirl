using HarmonyLib;

namespace Mugirl.Features.WeaponWheel;

[HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_PawnSpawned))]
[MugirlPatch(PatchModule.WeaponWheel, PatchRisk.High, "Downed pawns spawned onto a map keep their reserve wheel weapons.")]
public static class Harmony_Pawn_EquipmentTracker_Notify_PawnSpawned
{
    // 与原版一致：倒地且不在床上的 Pawn 生成到地图时丢下装备，储备武器一并处理。
    public static void Postfix(Pawn_EquipmentTracker __instance)
    {
        Pawn? pawn = __instance.pawn;
        if (pawn != null && pawn.Downed && !pawn.GetPosture().InBed())
        {
            WeaponWheelLookup.CompFor(pawn)?.HandleDropAndForbidEverything(false);
        }
    }
}
