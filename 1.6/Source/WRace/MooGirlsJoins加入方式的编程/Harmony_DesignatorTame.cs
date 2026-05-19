using System;
using AlienRace;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MooGirl
{
    // ---------------- Patch TameUtility.CanTame ----------------
    [HarmonyPatch(typeof(TameUtility), nameof(TameUtility.CanTame))]
    public static class TameUtility_CanTame_Patch
    {
        // 后置补丁：如果pawn是逃跑野生奴隶，则返回true
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (pawn == null) return;

            if (pawn.kindDef == MooGirl_DefOf.MooGirl_EscapeWildSlave)
            {
                __result = true;
            }
        }
    }

    // ---------------- Patch Designator_Tame.CanDesignateThing ----------------
    [HarmonyPatch(typeof(Designator_Tame), nameof(Designator_Tame.CanDesignateThing))]
    public static class DesignatorTame_CanDesignateThing_Patch
    {
        // 后置补丁：如果pawn是逃跑野生奴隶，则允许Tame UI显示
        public static void Postfix(Thing t, ref AcceptanceReport __result)
        {
            if (t == null) return;

            Pawn pawn = t as Pawn;
            if (pawn == null) return;

            if (pawn.kindDef == MooGirl_DefOf.MooGirl_EscapeWildSlave)
            {
                __result = true;
            }
        }
    }
}