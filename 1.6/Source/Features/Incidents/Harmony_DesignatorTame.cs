using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MooGirl
{
    // 逃亡野生奴隶不是普通动物，但沿用驯服指令作为“接触并收编”的入口。
    [HarmonyPatch(typeof(TameUtility), nameof(TameUtility.CanTame))]
    public static class TameUtility_CanTame_Patch
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (pawn == null) return;

            if (MooGirlWildSlaveUtility.IsNonPlayerEscapeWildSlave(pawn))
            {
                __result = true;
            }
        }
    }

    // Designator 单独控制 UI 可见性，必须与 CanTame 结果保持一致。
    [HarmonyPatch(typeof(Designator_Tame), nameof(Designator_Tame.CanDesignateThing))]
    public static class DesignatorTame_CanDesignateThing_Patch
    {
        public static void Postfix(Thing t, ref AcceptanceReport __result)
        {
            if (t == null) return;

            Pawn pawn = t as Pawn;
            if (pawn == null) return;

            if (MooGirlWildSlaveUtility.IsNonPlayerEscapeWildSlave(pawn))
            {
                __result = true;
            }
        }
    }
}
