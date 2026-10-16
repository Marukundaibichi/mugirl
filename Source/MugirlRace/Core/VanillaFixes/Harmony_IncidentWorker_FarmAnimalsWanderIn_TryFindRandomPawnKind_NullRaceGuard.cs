using HarmonyLib;

namespace Mugirl.Core;

// 私有目标：按名字并写明完整参数类型，签名变化时注册失败而不是补到别的重载上。
[HarmonyPatch(typeof(IncidentWorker_FarmAnimalsWanderIn), "TryFindRandomPawnKind",
    new[] { typeof(Map), typeof(PawnKindDef) }, new[] { ArgumentType.Normal, ArgumentType.Out })]
[MugirlPatch(PatchModule.VanillaFixes, PatchRisk.Medium, "Null-race PawnKindDefs throw inside vanilla farm-animal incident filtering again.")]
internal static class Harmony_IncidentWorker_FarmAnimalsWanderIn_TryFindRandomPawnKind_NullRaceGuard
{
    private static Exception? Finalizer(Exception? __exception, ref bool __result)
    {
        if (__exception == null)
        {
            return null;
        }

        NullRacePawnKindGuard.ReportBrokenPawnKindDefsOnce("FarmAnimalsWanderIn.CanFireNow");
        __result = false;
        return null;
    }
}
