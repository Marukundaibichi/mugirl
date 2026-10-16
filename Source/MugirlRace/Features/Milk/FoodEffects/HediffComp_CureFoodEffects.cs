using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.Milk;

// 食物 Hediff 存在期间持续清除指定的负面 Hediff（食物中毒、毒素累积等）。
// 清除放在 CompPostTickInterval：按 Pawn 的更新间隔（最多 15 tick）执行，不再每 tick 线性扫描。
public sealed class HediffComp_CureFoodEffects : HediffComp
{
    private bool mugirlCureApplied;

    public HediffCompProperties_CureFoodEffects? CureProps => props as HediffCompProperties_CureFoodEffects;

    public void ReapplyCure()
    {
        mugirlCureApplied = false;
        ApplyCure();
    }

    public override void CompPostMake()
    {
        base.CompPostMake();
        ApplyCure();
    }

    public override void CompPostPostAdd(DamageInfo? dinfo)
    {
        base.CompPostPostAdd(dinfo);
        ApplyCure();
    }

    public override void CompPostTickInterval(ref float severityAdjustment, int delta)
    {
        base.CompPostTickInterval(ref severityAdjustment, delta);
        ApplyCure();
        if (TryGetRemoveHediffs(out List<HediffDef>? removeHediffs) && removeHediffs.Count > 0)
        {
            FoodEffectUtility.RemoveHediffs(Pawn, removeHediffs);
        }
    }

    public override void CompExposeData()
    {
        base.CompExposeData();
        Scribe_Values.Look(ref mugirlCureApplied, "mugirlCureApplied", false);
    }

    private void ApplyCure()
    {
        if (mugirlCureApplied)
        {
            return;
        }

        if (CureProps == null)
        {
            mugirlCureApplied = true;
            return;
        }

        if (!TryGetRemoveHediffs(out List<HediffDef>? removeHediffs))
        {
            return;
        }

        mugirlCureApplied = true;
        FoodEffectUtility.RemoveHediffs(Pawn, removeHediffs);
    }

    private bool TryGetRemoveHediffs([NotNullWhen(true)] out List<HediffDef>? removeHediffs)
    {
        removeHediffs = CureProps?.removeHediffs;
        return Pawn?.health?.hediffSet != null && removeHediffs != null;
    }
}
