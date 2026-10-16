using HarmonyLib;

namespace Mugirl.Features.Corporate;

// 每生成一个 Pawn 都会经过：以 Def 引用比较代替 defName 字符串比较。
[HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), typeof(PawnGenerationRequest))]
[MugirlPatch(PatchModule.Corporate, PatchRisk.Low, "Ideologies that suppress apparel can omit corporate uniforms.")]
internal static class Harmony_PawnGenerator_GeneratePawn_CorporateUniforms
{
    [HarmonyPriority(Priority.Normal)]
    private static void Postfix(Pawn __result)
    {
        if (CorporateUniforms.IsCorporateEmployee(__result?.kindDef))
        {
            CorporateUniforms.EnsureRequiredApparel(__result);
        }
    }
}
