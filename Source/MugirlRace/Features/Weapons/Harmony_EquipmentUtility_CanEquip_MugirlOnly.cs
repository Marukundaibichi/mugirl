using HarmonyLib;

namespace Mugirl.Features.Weapons;

// 带 MugirlOnlyEquipmentExtension 的武器只允许雪牛娘装备。目标是四参数重载；两参数重载内部转调它，一并覆盖。
[HarmonyPatch(typeof(EquipmentUtility), nameof(EquipmentUtility.CanEquip), [typeof(Thing), typeof(Pawn), typeof(string), typeof(bool)],
    [ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal])]
[MugirlPatch(PatchModule.Weapons, PatchRisk.Medium, "Mugirl-only weapons can be equipped by other races.")]
internal static class Harmony_EquipmentUtility_CanEquip_MugirlOnly
{
    private static void Postfix(Thing? thing, Pawn? pawn, ref string? cantReason, ref bool __result)
    {
        MugirlOnlyEquipmentExtension? restriction = thing?.def?.GetModExtension<MugirlOnlyEquipmentExtension>();
        if (!__result || restriction?.enabled != true || MugirlIdentity.IsMugirlPawn(pawn))
        {
            return;
        }

        __result = false;
        cantReason = "Mugirl.Weapon.MugirlOnly".Translate();
    }
}
