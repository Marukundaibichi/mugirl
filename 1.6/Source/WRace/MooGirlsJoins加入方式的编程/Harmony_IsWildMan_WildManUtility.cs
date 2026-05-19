using System;
using AlienRace;
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
            if (p.kindDef == MooGirl_DefOf.MooGirl_EscapeWildSlave && !p.IsSubhuman)
            {
                __result = true; // 强制认定为野人
            }
        }
    }

    // 使用Harmony库对RecruitUtility类的Recruit方法进行后置补丁
    [HarmonyPatch(typeof(RecruitUtility), "Recruit")]
    public static class RecruitUtility_Recruit_Patch
    {
        // 后置补丁方法：在招募完成后修改pawn属性
        public static void Postfix(Pawn pawn, Faction faction, Pawn recruiter = null)
        {
            // 空值检查
            if (pawn == null) return;

            // 如果pawn是逃跑的野生奴隶，则将其kindDef改为预设的前逃跑状态
            if (pawn.kindDef == MooGirl_DefOf.MooGirl_EscapeWildSlave)
            {
                pawn.kindDef = MooGirl_DefOf.MooGirl_PreEscapeWildSlave;
            }
        }
    }
}