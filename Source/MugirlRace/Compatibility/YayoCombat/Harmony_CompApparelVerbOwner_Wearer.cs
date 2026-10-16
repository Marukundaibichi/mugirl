using HarmonyLib;
using Mugirl.Features.WeaponWheel;

namespace Mugirl.Compatibility.YayoCombat;

// 目标是只返回一个表达式的非虚 getter，极易被 JIT 内联。只有在 Mod 构造函数中打补丁（调用方尚未编译）才对调用方可见，
// 不能改成延迟补丁（MugirlPatch.Deferred）或在读档后再打。
[HarmonyPatch(typeof(CompApparelVerbOwner), nameof(CompApparelVerbOwner.Wearer), MethodType.Getter)]
[MugirlPatch(PatchModule.YayoCombat, PatchRisk.Medium, "Reloadable reserve weapons cannot resolve their wheel pawn as wearer.")]
internal static class Harmony_CompApparelVerbOwner_Wearer
{
    public static bool Prepare() => YayoCombatCompatibility.Active;

    public static void Postfix(CompApparelVerbOwner __instance, ref Pawn? __result)
    {
        if (__result is null && __instance.parent?.ParentHolder is CompWeaponWheel wheel)
        {
            __result = wheel.Pawn;
        }
    }
}
