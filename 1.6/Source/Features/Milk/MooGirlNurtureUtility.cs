using System;
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
        private const int CompletedNurturePassionGainCount = 2;

        // StaticCacheLifecycle: process-level reflection cache for SkillRecord.passion; no game objects are retained.
        private static readonly FieldInfo SkillPassionField = AccessTools.Field(typeof(SkillRecord), "passion");

        // StaticCacheLifecycle: per-game Def lookup cache; reset by MooGirlStoryState on game init/new-game/load.
        private static HediffDef motherlyNurtureDef;
        private static HediffDef nurtureAfterglowDef;
        private static TraitDef nurturedTraitDef;

        public static HediffDef MotherlyNurtureDef => motherlyNurtureDef ?? (motherlyNurtureDef = DefDatabase<HediffDef>.GetNamedSilentFail("MooGirl_MotherlyNurture"));
        public static HediffDef NurtureAfterglowDef => nurtureAfterglowDef ?? (nurtureAfterglowDef = DefDatabase<HediffDef>.GetNamedSilentFail("MooGirl_NurtureAfterglow"));
        public static TraitDef NurturedTraitDef => nurturedTraitDef ?? (nurturedTraitDef = DefDatabase<TraitDef>.GetNamedSilentFail("MooGirl_NurturedByMooGirl"));

        internal static void ResetDefCache()
        {
            motherlyNurtureDef = null;
            nurtureAfterglowDef = null;
            nurturedTraitDef = null;
        }

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

            SkillRecord first = null;
            SkillRecord second = null;
            for (int i = 0; i < pawn.skills.skills.Count; i++)
            {
                SkillRecord skill = pawn.skills.skills[i];
                if (!CanImprovePassion(skill))
                {
                    continue;
                }

                if (IsBetterSkillCandidate(skill, first))
                {
                    second = first;
                    first = skill;
                }
                else if (IsBetterSkillCandidate(skill, second))
                {
                    second = skill;
                }
            }

            ImprovePassion(first);
            if (CompletedNurturePassionGainCount > 1)
            {
                ImprovePassion(second);
            }
        }

        private static bool CanImprovePassion(SkillRecord skill)
        {
            return skill != null && !skill.TotallyDisabled && skill.passion < Passion.Major;
        }

        private static bool IsBetterSkillCandidate(SkillRecord candidate, SkillRecord current)
        {
            if (candidate == null)
            {
                return false;
            }

            if (current == null)
            {
                return true;
            }

            if (candidate.Level != current.Level)
            {
                return candidate.Level > current.Level;
            }

            return candidate.XpTotalEarned > current.XpTotalEarned;
        }

        private static void ImprovePassion(SkillRecord skill)
        {
            if (skill == null)
            {
                return;
            }

            Passion next = skill.passion == Passion.None ? Passion.Minor : Passion.Major;
            SkillPassionField.SetValue(skill, next);
        }
    }
}
