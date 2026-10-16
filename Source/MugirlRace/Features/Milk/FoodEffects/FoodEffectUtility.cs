namespace Mugirl.Features.Milk;

// 食物效果的通用操作：移除指定 Hediff、添加或刷新带时限的食物 Hediff。
internal static class FoodEffectUtility
{
    internal static void RemoveHediffs(Pawn? pawn, List<HediffDef>? removeHediffs)
    {
        HediffSet? hediffSet = pawn?.health?.hediffSet;
        if (pawn == null || hediffSet == null || removeHediffs == null)
        {
            return;
        }

        for (int i = 0; i < removeHediffs.Count; i++)
        {
            HediffDef? hediffDef = removeHediffs[i];
            if (hediffDef == null)
            {
                continue;
            }

            Hediff? hediff;
            while ((hediff = hediffSet.GetFirstHediffOfDef(hediffDef)) != null)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }
    }

    internal static void AddOrRefreshHediff(Pawn? pawn, HediffDef? hediffDef, float minimumSeverity = -1f)
    {
        if (pawn?.health?.hediffSet == null || hediffDef == null)
        {
            return;
        }

        Hediff? hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
        if (hediff == null)
        {
            hediff = HediffMaker.MakeHediff(hediffDef, pawn);
            if (minimumSeverity > 0f)
            {
                hediff.Severity = minimumSeverity;
            }

            pawn.health.AddHediff(hediff);
        }
        else if (minimumSeverity > 0f && hediff.Severity < minimumSeverity)
        {
            hediff.Severity = minimumSeverity;
        }

        hediff.TryGetComp<HediffComp_Disappears>()?.ResetElapsedTicks();
        hediff.TryGetComp<HediffComp_CureFoodEffects>()?.ReapplyCure();
    }
}
