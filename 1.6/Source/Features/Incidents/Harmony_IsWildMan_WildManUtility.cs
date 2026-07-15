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

            if (MugirlWildSlaveUtility.IsNonPlayerWildMugirl(p) && !p.IsSubhuman)
            {
                __result = true;
            }
        }
    }

    // 招募成功后只清理事件与动物式状态；野人行为资格由 PawnKind 与阵营共同决定。
    [HarmonyPatch(typeof(RecruitUtility), "Recruit")]
    public static class RecruitUtility_Recruit_Patch
    {
        public static void Postfix(Pawn pawn, Faction faction)
        {
            if (pawn == null) return;

            if (MugirlWildSlaveUtility.IsPlayerFaction(faction))
            {
                MugirlWildSlaveUtility.CleanupAfterJoiningPlayer(pawn);
            }
        }
    }
}
