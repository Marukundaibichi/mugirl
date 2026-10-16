using HarmonyLib;
using Verse.AI;

namespace Mugirl.Features.Corporate;

// 拦截所有来源的 PanicFlee（受击、恐惧、心灵能力等）；火焰恐慌(PanicFleeFire)属于求生反应，不在拦截范围。
// 返回 false 时跳过原方法（包括其他模组在其中的 Transpiler 与 Harmony 3 infix），只对死士的 PanicFlee 生效。
[HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
[MugirlPatch(PatchModule.Corporate, PatchRisk.Medium, "Diehard support troopers can start the base panic-flee mental state.")]
internal static class Harmony_MentalStateHandler_TryStartMentalState_CorporateDiehard
{
    internal static bool Prefix(Pawn ___pawn, MentalStateDef stateDef)
    {
        return !(stateDef == MentalStateDefOf.PanicFlee && CorporateDiehardUtility.IsDiehard(___pawn));
    }
}
