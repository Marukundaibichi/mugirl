using RimWorld;
using System;
using Verse;

namespace Mugirl.Features.Appearance
{
    /// <summary>
    /// 身体附件的临时状态。基础变体仍由 HAR 保存；Lovin 印记只在渲染时覆盖，
    /// 到期后无需改写梳妆台选择即可恢复原外观。
    /// </summary>
    public sealed class CompProperties_MugirlBodyAccessory : CompProperties
    {
        public float lovinBiteChance = 0.25f;
        public float lovinHandprintChance = 0.25f;
        public IntRange lovinMarkDurationTicks = new IntRange(60000, 180000);

        public CompProperties_MugirlBodyAccessory()
        {
            compClass = typeof(CompMugirlBodyAccessory);
        }
    }

    public sealed class CompMugirlBodyAccessory : ThingComp
    {
        private int temporaryVariant;
        private int temporaryVariantExpiresAt = -1;

        private CompProperties_MugirlBodyAccessory Props => props as CompProperties_MugirlBodyAccessory;

        internal int TemporaryVariant
        {
            get
            {
                ExpireIfNeeded(notifyGraphics: false);
                return temporaryVariant;
            }
        }

        internal bool TryApplyLovinMark()
        {
            CompProperties_MugirlBodyAccessory bodyProps = Props;
            if (bodyProps == null || !MugirlTickUtility.TryGetCurrentGameTick(out int currentTick))
            {
                return false;
            }

            float roll = Rand.Value;
            int selectedVariant;
            if (roll < bodyProps.lovinBiteChance)
            {
                selectedVariant = MugirlBodyAccessoryVariants.LovinBite;
            }
            else if (roll < bodyProps.lovinBiteChance + bodyProps.lovinHandprintChance)
            {
                selectedVariant = MugirlBodyAccessoryVariants.LovinHandprint;
            }
            else
            {
                return false;
            }

            int minDuration = Math.Max(1, bodyProps.lovinMarkDurationTicks.min);
            int maxDuration = Math.Max(minDuration, bodyProps.lovinMarkDurationTicks.max);
            temporaryVariant = selectedVariant;
            temporaryVariantExpiresAt = currentTick + Rand.RangeInclusive(minDuration, maxDuration);
            NotifyAppearanceChanged();
            return true;
        }

        public override void CompTick()
        {
            base.CompTick();
            if (temporaryVariant != MugirlBodyAccessoryVariants.None)
            {
                ExpireIfNeeded(notifyGraphics: true);
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            ExpireIfNeeded(notifyGraphics: false);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref temporaryVariant, "temporaryBodyAccessoryVariant", MugirlBodyAccessoryVariants.None);
            Scribe_Values.Look(ref temporaryVariantExpiresAt, "temporaryBodyAccessoryVariantExpiresAt", -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit
                && temporaryVariant != MugirlBodyAccessoryVariants.None
                && temporaryVariant != MugirlBodyAccessoryVariants.LovinBite
                && temporaryVariant != MugirlBodyAccessoryVariants.LovinHandprint)
            {
                ClearTemporaryMark();
            }
        }

        private void ExpireIfNeeded(bool notifyGraphics)
        {
            if (temporaryVariant == MugirlBodyAccessoryVariants.None
                || !MugirlTickUtility.TryGetCurrentGameTick(out int currentTick)
                || currentTick < temporaryVariantExpiresAt)
            {
                return;
            }

            ClearTemporaryMark();
            if (notifyGraphics)
            {
                NotifyAppearanceChanged();
            }
        }

        private void ClearTemporaryMark()
        {
            temporaryVariant = MugirlBodyAccessoryVariants.None;
            temporaryVariantExpiresAt = -1;
        }

        private void NotifyAppearanceChanged()
        {
            Pawn pawn = parent as Pawn;
            if (pawn == null)
            {
                return;
            }

            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            PortraitsCache.SetDirty(pawn);
        }
    }
}
