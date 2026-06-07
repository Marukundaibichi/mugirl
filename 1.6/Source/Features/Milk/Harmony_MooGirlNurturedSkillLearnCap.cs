using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MooGirl
{
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Learn))]
    public static class Harmony_MooGirlNurturedSkillLearnCap
    {
        // StaticCacheLifecycle: 进程级 SkillRecord 内部字段反射缓存；不持有游戏对象。
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(SkillRecord), "pawn");
        private static readonly FieldInfo XpSinceMidnightField = AccessTools.Field(typeof(SkillRecord), "xpSinceMidnight");
        private static readonly FieldInfo MaxFullRateXpPerDayField = AccessTools.Field(typeof(SkillRecord), "MaxFullRateXpPerDay");

        public static void Prefix(SkillRecord __instance)
        {
            Pawn pawn = PawnField?.GetValue(__instance) as Pawn;
            TraitDef nurturedTrait = MooGirlNurtureUtility.NurturedTraitDef;
            if (nurturedTrait == null || pawn?.story?.traits == null || !pawn.story.traits.HasTrait(nurturedTrait))
            {
                return;
            }

            if (XpSinceMidnightField == null || MaxFullRateXpPerDayField == null)
            {
                return;
            }

            object xpSinceMidnightValue = XpSinceMidnightField.GetValue(__instance);
            object baseCapValue = MaxFullRateXpPerDayField.GetValue(null);
            if (!(xpSinceMidnightValue is float xpSinceMidnight) || !(baseCapValue is int baseCap))
            {
                return;
            }

            float extraCap = baseCap * 1.5f;
            if (xpSinceMidnight > baseCap)
            {
                XpSinceMidnightField.SetValue(__instance, Mathf.Max(baseCap, xpSinceMidnight - extraCap));
            }
        }
    }
}
