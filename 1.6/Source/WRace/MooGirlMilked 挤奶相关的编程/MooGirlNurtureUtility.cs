using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MooGirl
{
    public static class MooGirlNurtureUtility
    {
        public const float ChildInteractionProgressMin = 0.07f;
        public const float ChildInteractionProgressMax = 0.10f;
        public const float BabyFullFeedProgress = 0.04f;
        public const int AfterglowTicks = 27000000;

        private static readonly FieldInfo SkillPassionField = AccessTools.Field(typeof(SkillRecord), "passion");

        private static HediffDef motherlyNurtureDef;
        private static HediffDef nurtureAfterglowDef;
        private static TraitDef nurturedTraitDef;

        public static HediffDef MotherlyNurtureDef => motherlyNurtureDef ?? (motherlyNurtureDef = DefDatabase<HediffDef>.GetNamedSilentFail("MooGirl_MotherlyNurture"));
        public static HediffDef NurtureAfterglowDef => nurtureAfterglowDef ?? (nurtureAfterglowDef = DefDatabase<HediffDef>.GetNamedSilentFail("MooGirl_NurtureAfterglow"));
        public static TraitDef NurturedTraitDef => nurturedTraitDef ?? (nurturedTraitDef = DefDatabase<TraitDef>.GetNamedSilentFail("MooGirl_NurturedByMooGirl"));

        public static bool CanStartNurture(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return false;
            }

            HediffDef afterglow = NurtureAfterglowDef;
            return afterglow == null || !pawn.health.hediffSet.HasHediff(afterglow);
        }

        public static void AddChildNurtureProgress(Pawn child, Pawn feeder)
        {
            AddNurtureProgress(child, Rand.Range(ChildInteractionProgressMin, ChildInteractionProgressMax), feeder);
        }

        public static void AddBabyNurtureProgress(Pawn baby, Pawn feeder, int delta)
        {
            float fullFeedTicks = Mathf.Max(1f, ChildcareUtility.FullFeedSessionTicks);
            AddNurtureProgress(baby, BabyFullFeedProgress * Mathf.Max(1, delta) / fullFeedTicks, feeder);
        }

        public static void AddNurtureProgress(Pawn pawn, float amount, Pawn feeder = null)
        {
            if (pawn?.health == null || amount <= 0f || !CanStartNurture(pawn))
            {
                return;
            }

            HediffDef def = MotherlyNurtureDef;
            if (def == null)
            {
                return;
            }

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(def, pawn);
                hediff.Severity = 0.001f;
                pawn.health.AddHediff(hediff);
            }

            hediff.Severity = Mathf.Min(hediff.Severity + amount, def.maxSeverity);
            hediff.TryGetComp<HediffComp_MooGirlNurtureProgress>()?.TryComplete(feeder);
        }

        public static void CompleteNurture(Pawn pawn, Hediff hediff, Pawn feeder = null)
        {
            if (pawn?.health == null)
            {
                return;
            }

            HediffDef afterglow = NurtureAfterglowDef;
            if (afterglow != null && pawn.health.hediffSet.HasHediff(afterglow))
            {
                if (hediff != null)
                {
                    pawn.health.RemoveHediff(hediff);
                }
                return;
            }

            TraitDef traitDef = NurturedTraitDef;
            if (traitDef != null && pawn.story?.traits != null && !pawn.story.traits.HasTrait(traitDef))
            {
                pawn.story.traits.GainTrait(new Trait(traitDef));
            }

            ImproveTopSkillPassions(pawn);

            if (afterglow != null)
            {
                Hediff afterglowHediff = HediffMaker.MakeHediff(afterglow, pawn);
                pawn.health.AddHediff(afterglowHediff);
            }

            if (hediff != null && pawn.health.hediffSet.hediffs.Contains(hediff))
            {
                pawn.health.RemoveHediff(hediff);
            }

            Messages.Message("MooGirl.Nurture.Completed".Translate(pawn.Named("PAWN")), pawn, MessageTypeDefOf.PositiveEvent);
        }

        private static void ImproveTopSkillPassions(Pawn pawn)
        {
            if (pawn?.skills?.skills == null || SkillPassionField == null)
            {
                return;
            }

            IEnumerable<SkillRecord> candidates = pawn.skills.skills
                .Where(skill => skill != null && !skill.TotallyDisabled && skill.passion < Passion.Major)
                .OrderByDescending(skill => skill.Level)
                .ThenByDescending(skill => skill.XpTotalEarned)
                .Take(2);

            foreach (SkillRecord skill in candidates)
            {
                Passion next = skill.passion == Passion.None ? Passion.Minor : Passion.Major;
                SkillPassionField.SetValue(skill, next);
            }
        }
    }

    public class HediffCompProperties_MooGirlNurtureProgress : HediffCompProperties
    {
        public HediffCompProperties_MooGirlNurtureProgress()
        {
            compClass = typeof(HediffComp_MooGirlNurtureProgress);
        }
    }

    public class HediffComp_MooGirlNurtureProgress : HediffComp
    {
        private bool completed;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            if (!completed && parent.Severity >= parent.def.maxSeverity)
            {
                TryComplete();
            }
        }

        public void TryComplete(Pawn feeder = null)
        {
            if (completed || parent.Severity < parent.def.maxSeverity)
            {
                return;
            }

            completed = true;
            MooGirlNurtureUtility.CompleteNurture(Pawn, parent, feeder);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref completed, "completed", false);
        }
    }

    [HarmonyPatch(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.GrowthPointsPerDay), MethodType.Getter)]
    public static class Harmony_MooGirlNurtureGrowthPoints
    {
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_AgeTracker), "pawn");

        public static void Postfix(Pawn_AgeTracker __instance, ref float __result)
        {
            if (__result <= 0f)
            {
                return;
            }

            Pawn pawn = PawnField?.GetValue(__instance) as Pawn;
            if (pawn?.health?.hediffSet?.HasHediff(MooGirlNurtureUtility.NurtureAfterglowDef) == true)
            {
                __result *= 2.5f;
                return;
            }

            Hediff nurture = pawn?.health?.hediffSet?.GetFirstHediffOfDef(MooGirlNurtureUtility.MotherlyNurtureDef);
            if (nurture == null)
            {
                return;
            }

            __result *= GrowthFactorForSeverity(nurture.Severity);
        }

        private static float GrowthFactorForSeverity(float severity)
        {
            if (severity >= 0.70f)
            {
                return 2.5f;
            }
            if (severity >= 0.40f)
            {
                return 2f;
            }
            return 1.5f;
        }
    }

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
