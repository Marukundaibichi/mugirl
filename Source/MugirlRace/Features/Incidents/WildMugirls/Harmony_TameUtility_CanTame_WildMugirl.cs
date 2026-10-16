using HarmonyLib;

namespace Mugirl.Features.Incidents;

// 逃亡野生奴隶不是普通动物，但沿用驯服指令作为“接触并收编”的入口。
// 原版 Designator_Tame.CanDesignateThing 先调用本补丁后的 CanTame，再检查 Scaria 与已有指定（1.6.9676 与 1.7 均已核对），
// 所以不再单独补丁 Designator；1.6 的那个补丁会无视已有指定与 Scaria 强行返回 true。
[HarmonyPatch(typeof(TameUtility), nameof(TameUtility.CanTame))]
[MugirlPatch(PatchModule.Incidents, PatchRisk.Medium, "Escape wild slaves are no longer accepted by the tame entry point.")]
internal static class Harmony_TameUtility_CanTame_WildMugirl
{
    public static void Postfix(Pawn pawn, ref bool __result)
    {
        // 与原版相同，属于人形派系的 Pawn 不能驯服（1.6 漏了这一条，例如世界 Pawn 被重新分配派系后回到地图）。
        if (WildMugirlUtility.IsNonPlayerWildMugirl(pawn)
            && (pawn.Faction == null || !pawn.Faction.def.humanlikeFaction))
        {
            __result = true;
        }
    }
}
