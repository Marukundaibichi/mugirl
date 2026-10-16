using HarmonyLib;

namespace Mugirl.Features.WeaponWheel;

[HarmonyPatch(typeof(Pawn), nameof(Pawn.TryGetAttackVerb), [typeof(Thing), typeof(bool), typeof(bool)])]
[MugirlPatch(PatchModule.WeaponWheel, PatchRisk.High, "External combat mods can leave TryGetAttackVerb selecting a reserve wheel weapon that is not the active primary, aborting full-firepower cycling.")]
public static class Harmony_Pawn_TryGetAttackVerb_ReserveWeaponGuard
{
    // Harmony 按优先级从高到低执行后缀，最低优先级最后运行：本防线要在其他框架完成 Verb 替换之后再校验最终结果。
    // （1.6 写成 int.MaxValue，实际最先运行，之后的后缀仍可能重新选中储备武器。）
    [HarmonyPriority(int.MinValue)]
    public static void Postfix(
        Pawn __instance,
        Thing? target,
        bool allowManualCastWeapons,
        bool allowTurrets,
        ref Verb? __result)
    {
        Verb? selectedResult = __result;
        bool corrected = false;
        CompWeaponWheel? comp = WeaponWheelLookup.CompFor(__instance);
        if (comp != null && comp.TryCorrectAttackVerbSelection(
            selectedResult,
            allowManualCastWeapons,
            out Verb? correctedResult))
        {
            __result = correctedResult;
            corrected = true;
        }

        WeaponWheelDevLog.AttackVerbSelected(
            __instance,
            target,
            allowManualCastWeapons,
            allowTurrets,
            selectedResult,
            __result,
            corrected);
    }
}
