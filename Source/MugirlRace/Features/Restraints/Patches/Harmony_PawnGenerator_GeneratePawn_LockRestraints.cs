using HarmonyLib;

namespace Mugirl.Features.Restraints;

// 1.6 中由 MugirlPatchRegistry 手动注册，1.7 改为普通特性补丁。显式写出参数类型：GeneratePawn 有两个重载。
// 类型名排在同一目标的其他 Mugirl 后置补丁（Harmony_*）之后，仍按 1.6 的顺序最后执行。
[HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), typeof(PawnGenerationRequest))]
[MugirlPatch(PatchModule.Restraints, PatchRisk.High, "Generated slave apparel is not locked by the post-generation fallback.")]
public static class Harmony_PawnGenerator_GeneratePawn_LockRestraints
{
    [HarmonyPriority(Priority.Last)]
    public static void Postfix(Pawn? __result)
    {
        __result.EnsureWornRestraintLocks();
    }
}
