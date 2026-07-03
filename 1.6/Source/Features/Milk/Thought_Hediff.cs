using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Mugirl
{
    internal class Thought_Hediff : Thought_Memory
    {
        // 这个 thought 带有一次性副作用。保存该标记是为了保持旧行为：
        // hediff/joy 效果在第一次执行 MoodOffset 时应用。
        public bool added = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref added, "added", false);
        }

        public override float MoodOffset()
        {
            if (!added)
            {
                if (!ThoughtUtility.ThoughtNullified(pawn, def))
                {
                    ApplyThoughtEffects();
                }

                added = true;
            }

            return base.MoodOffset();
        }

        public override bool TryMergeWithExistingMemory(out bool showBubble)
        {
            bool merged = base.TryMergeWithExistingMemory(out showBubble);
            if (merged && def.hediff != null && !ThoughtUtility.ThoughtNullified(pawn, def))
            {
                AddOrRefreshDefHediff();
            }

            return merged;
        }

        private void ApplyThoughtEffects()
        {
            if (def.hediff != null)
            {
                AddOrRefreshDefHediff();
            }

            Thought_Hediff_Extension extension = def.GetModExtension<Thought_Hediff_Extension>();
            if (extension == null)
            {
                return;
            }

            ApplyExtensionHediff(extension.hediffToAffect, extension.partToAffect, extension.percentage);
            ApplyExtensionHediff(extension.secondHediffToAffect, extension.secondPartToAffect, extension.secondPercentage);

            if (extension.increaseJoy)
            {
                pawn.needs.joy.GainJoy(extension.extraJoy, JoyKindDefOf.Gluttonous);
            }
        }

        private void ApplyExtensionHediff(HediffDef hediffDef, BodyPartDef partDef, float severityOffset)
        {
            if (hediffDef == null)
            {
                return;
            }

            BodyPartRecord part = FirstPartWithDef(partDef);
            pawn.health.AddHediff(hediffDef, part, null, null);

            // 保持旧结果：添加 hediff 后，调整 pawn 身上该 def 的第一个 hediff，
            // 而不是假设刚刚添加的实例一定会被修改。
            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef, false);
            if (hediff != null)
            {
                hediff.Severity += severityOffset;
            }
        }

        private BodyPartRecord FirstPartWithDef(BodyPartDef partDef)
        {
            if (partDef == null || pawn?.RaceProps?.body == null)
            {
                return null;
            }

            List<BodyPartRecord> parts = pawn.RaceProps.body.GetPartsWithDef(partDef);
            return parts.Count > 0 ? parts[0] : null;
        }

        private void AddOrRefreshDefHediff()
        {
            MugirlFoodEffectUtility.AddOrRefreshHediff(pawn, def.hediff);
        }
    }

    public class Thought_Hediff_Extension : DefModExtension
    {
        public HediffDef hediffToAffect = null;
        public BodyPartDef partToAffect = null;
        public float percentage = 1f;

        public HediffDef secondHediffToAffect = null;
        public BodyPartDef secondPartToAffect = null;
        public float secondPercentage = 1f;

        public bool increaseJoy = false;
        public float extraJoy = 0f;
    }
}
