using HarmonyLib;

namespace Mugirl.Features.WeaponWheel;

// PawnGenerator.GeneratePawn 另有服装、新生儿外观与拘束补丁的后缀；彼此不依赖顺序。
[HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), [typeof(PawnGenerationRequest)])]
[MugirlPatch(PatchModule.WeaponWheel, PatchRisk.Medium, "Hostile generated Mugirls keep only their ordinary primary weapon and cannot start a full-firepower cycle.")]
public static class Harmony_PawnGenerator_GeneratePawn_WeaponWheelLoadout
{
    [HarmonyPriority(Priority.Normal - 2)]
    public static void Postfix(PawnGenerationRequest request, Pawn __result)
    {
        WeaponWheelEnemyLoadoutGenerator.TryGenerateFor(__result, request);
    }
}
