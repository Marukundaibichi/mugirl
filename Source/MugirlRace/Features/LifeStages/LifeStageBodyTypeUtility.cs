using Mugirl.Rendering;

namespace Mugirl.Features.LifeStages;

// 雪牛娘各生命阶段的身形：婴儿/新生儿用婴儿身形，儿童用儿童身形，青少年与成年用女性身形。身形改变后刷新渲染与头像。
internal static class LifeStageBodyTypeUtility
{
    public static bool NormalizeBodyType(Pawn? pawn, bool queueRenderRefresh = false)
    {
        if (!ModsConfig.BiotechActive || pawn?.story is null || !MugirlIdentity.HasMugirlBody(pawn))
        {
            return false;
        }

        BodyTypeDef? expectedBodyType = ExpectedBodyType(pawn);
        if (expectedBodyType is null || pawn.story.bodyType == expectedBodyType)
        {
            return false;
        }

        pawn.story.bodyType = expectedBodyType;
        RefreshVisuals(pawn, queueRenderRefresh);
        return true;
    }

    public static void RefreshVisuals(Pawn? pawn, bool queueRenderRefresh = false)
    {
        if (pawn is null || pawn.Destroyed)
        {
            return;
        }

        if (queueRenderRefresh && MugirlIdentity.IsMugirlPawn(pawn))
        {
            // NotifyPawnChanged 本身会立即刷新，避免同一调用重复标记渲染树。
            PawnRenderRefresh.NotifyPawnChanged(pawn);
        }
        else
        {
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        PortraitsCache.SetDirty(pawn);
    }

    public static int NormalizeLoadedPawns()
    {
        int changed = 0;
        List<Pawn> pawns = PawnsFinder.AllMapsWorldAndTemporary_Alive;
        for (int i = 0; i < pawns.Count; i++)
        {
            if (NormalizeBodyType(pawns[i]))
            {
                changed++;
            }
        }

        return changed;
    }

    private static BodyTypeDef? ExpectedBodyType(Pawn pawn)
    {
        DevelopmentalStage stage = pawn.DevelopmentalStage;
        if (stage.Baby() || stage.Newborn())
        {
            return BodyTypeDefOf.Baby;
        }

        if (pawn.ageTracker?.CurLifeStage == MugirlDefOf.Mugirl_Teenager)
        {
            return BodyTypeDefOf.Female;
        }

        if (stage.Child())
        {
            return BodyTypeDefOf.Child;
        }

        if (stage.Adult())
        {
            return BodyTypeDefOf.Female;
        }

        return null;
    }
}
