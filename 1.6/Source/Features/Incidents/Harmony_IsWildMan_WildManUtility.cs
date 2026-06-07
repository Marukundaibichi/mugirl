using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MooGirl
{
    // 使用Harmony库对WildManUtility类的IsWildMan方法进行后置补丁
    [HarmonyPatch(typeof(WildManUtility), "IsWildMan")]
    public static class IsWildMan_WildManUtility_Patch
    {
        // 后置补丁方法：在原方法执行后修改返回值
        public static void Postfix(Pawn p, ref bool __result)
        {
            // 如果原方法已判定为野人则直接返回
            if (__result) return;

            // 检查pawn是否为逃跑的野生奴隶且不是亚人类
            if (MooGirlWildSlaveUtility.IsNonPlayerEscapeWildSlave(p) && !p.IsSubhuman)
            {
                __result = true; // 强制认定为野人
            }
        }
    }

    // 使用Harmony库对RecruitUtility类的Recruit方法进行后置补丁
    [HarmonyPatch(typeof(RecruitUtility), "Recruit")]
    public static class RecruitUtility_Recruit_Patch
    {
        public static void Prefix(Pawn pawn, out bool __state)
        {
            __state = MooGirlWildSlaveUtility.IsEscapeWildSlave(pawn);
        }

        // 后置补丁方法：在招募完成后修改pawn属性
        public static void Postfix(Pawn pawn, Faction faction, bool __state)
        {
            // 空值检查
            if (pawn == null) return;

            if (MooGirlWildSlaveUtility.IsPlayerFaction(faction))
            {
                MooGirlWildSlaveUtility.NormalizeAfterJoiningPlayer(pawn, __state);
            }
            else if (__state && MooGirlWildSlaveUtility.IsEscapeWildSlave(pawn) && MooGirl_DefOf.MooGirl_PreEscapeWildSlave != null)
            {
                pawn.ChangeKind(MooGirl_DefOf.MooGirl_PreEscapeWildSlave);
            }
        }
    }
}
