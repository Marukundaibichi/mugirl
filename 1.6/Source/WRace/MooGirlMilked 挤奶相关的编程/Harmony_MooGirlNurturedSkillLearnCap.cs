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
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(SkillRecord), "pawn");
        private static readonly FieldInfo XpSinceMidnightField = AccessTools.Field(typeof(SkillRecord), "xpSinceMidnight");
        private static readonly FieldInfo MaxFullRateXpPerDayField = AccessTools.Field(typeof(SkillRecord), "MaxFullRateXpPerDay");

        public static void Prefix(SkillRecord __instance)
        {
            Pawn pawn = PawnField?.GetValue(__instance) as Pawn;
            if (pawn?.story?.traits == null || !pawn.story.traits.HasTrait(MooGirlNurtureUtility.NurturedTraitDef))
            {
                return;
            }

            if (XpSinceMidnightField == null || MaxFullRateXpPerDayField == null)
            {
                return;
            }

            float xpSinceMidnight = (float)XpSinceMidnightField.GetValue(__instance);
            int baseCap = (int)MaxFullRateXpPerDayField.GetValue(null);
            float extraCap = baseCap * 1.5f;
            if (xpSinceMidnight > baseCap)
            {
                XpSinceMidnightField.SetValue(__instance, Mathf.Max(baseCap, xpSinceMidnight - extraCap));
            }
        }
    }
}
