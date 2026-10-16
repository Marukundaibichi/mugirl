namespace Mugirl.Features.Appearance;

/// <summary>保存 Lovin 临时印记及其到期 tick；到期在 tick 与渲染读取时检查。</summary>
public sealed class CompBodyAccessory : ThingComp
{
    private int mugirlAccessoryVariant;
    private int mugirlAccessoryExpiresAt = -1;

    private CompProperties_BodyAccessory? Props => props as CompProperties_BodyAccessory;

    internal int TemporaryVariant
    {
        get
        {
            ExpireIfNeeded(notifyGraphics: false);
            return mugirlAccessoryVariant;
        }
    }

    internal bool TryApplyLovinMark()
    {
        CompProperties_BodyAccessory? bodyProps = Props;
        if (bodyProps is null || !TickUtility.TryGetCurrentGameTick(out int currentTick))
        {
            return false;
        }

        int selectedVariant = BodyAccessoryVariants.SelectLovinMark(
            Rand.Value,
            bodyProps.lovinBiteChance,
            bodyProps.lovinHandprintChance);
        if (selectedVariant == BodyAccessoryVariants.None)
        {
            return false;
        }

        (int minDuration, int maxDuration) = BodyAccessoryVariants.NormalizeMarkDuration(
            bodyProps.lovinMarkDurationTicks.min,
            bodyProps.lovinMarkDurationTicks.max);
        mugirlAccessoryVariant = selectedVariant;
        mugirlAccessoryExpiresAt = currentTick + Rand.RangeInclusive(minDuration, maxDuration);
        NotifyAppearanceChanged();
        return true;
    }

    public override void CompTick()
    {
        base.CompTick();
        if (mugirlAccessoryVariant != BodyAccessoryVariants.None)
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
        Scribe_Values.Look(ref mugirlAccessoryVariant, "mugirlAccessoryVariant", BodyAccessoryVariants.None);
        Scribe_Values.Look(ref mugirlAccessoryExpiresAt, "mugirlAccessoryExpiresAt", -1);

        // 只接受 Lovin 印记；其他下标来自改动过的存档或旧版本，读档后清掉以免覆盖梳妆台选择。
        if (Scribe.mode == LoadSaveMode.PostLoadInit
            && mugirlAccessoryVariant != BodyAccessoryVariants.None
            && !BodyAccessoryVariants.IsLovinMark(mugirlAccessoryVariant))
        {
            ClearTemporaryMark();
        }
    }

    private void ExpireIfNeeded(bool notifyGraphics)
    {
        if (mugirlAccessoryVariant == BodyAccessoryVariants.None
            || !TickUtility.TryGetCurrentGameTick(out int currentTick)
            || currentTick < mugirlAccessoryExpiresAt)
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
        mugirlAccessoryVariant = BodyAccessoryVariants.None;
        mugirlAccessoryExpiresAt = -1;
    }

    private void NotifyAppearanceChanged()
    {
        if (parent is not Pawn pawn)
        {
            return;
        }

        pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        PortraitsCache.SetDirty(pawn);
    }
}
