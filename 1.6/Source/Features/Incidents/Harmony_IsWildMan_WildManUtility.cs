using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl
{
    // 让逃亡野生奴隶走原版野人入口，方便玩家用熟悉的交互收编。
    [HarmonyPatch(typeof(WildManUtility), "IsWildMan")]
    public static class IsWildMan_WildManUtility_Patch
    {
        public static void Postfix(Pawn p, ref bool __result)
        {
            if (__result) return;

            if (MugirlWildSlaveUtility.IsNonPlayerEscapeWildSlave(p) && !p.IsSubhuman)
            {
                __result = true;
            }
        }
    }

    // 招募成功后清理野生奴隶状态，避免后续仍被野人逻辑识别。
    [HarmonyPatch(typeof(RecruitUtility), "Recruit")]
    public static class RecruitUtility_Recruit_Patch
    {
        public static void Prefix(Pawn pawn, out bool __state)
        {
            __state = MugirlWildSlaveUtility.IsWildMugirl(pawn);
        }

        public static void Postfix(Pawn pawn, Faction faction, bool __state)
        {
            if (pawn == null) return;

            if (MugirlWildSlaveUtility.IsPlayerFaction(faction))
            {
                MugirlWildSlaveUtility.NormalizeAfterJoiningPlayer(pawn, __state);
            }
            else if (__state && MugirlWildSlaveUtility.IsWildMugirl(pawn) && Mugirl_DefOf.Mugirl_PreEscapeWildSlave != null)
            {
                pawn.ChangeKind(Mugirl_DefOf.Mugirl_PreEscapeWildSlave);
            }
        }
    }
}
