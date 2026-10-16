using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Mugirl.Features.Milk;

// 产奶状态与结算：按实际活跃 delta 累积，保存未结算余量、满值通知窗口及可持久的续接动作。
public sealed partial class CompMilkProduction
{
    private const float GatherFullnessPerUse = 0.2f;
    private const int NoTick = -99999;

    private float mugirlMilkFullness;

    // 防止满值后每次结算都重复触发接管尝试。
    private bool mugirlMilkFullNotified;

    private int mugirlMilkLastFullNotifyTick = NoTick;

    // 已经过、尚未结算的活跃 tick 数。
    private int mugirlMilkPendingTicks;

    private IResourceAccrualContinuation? mugirlMilkAccrualContinuation;

    // 自动挤奶阈值（0-1，默认 0.8 = 80%）。
    private float mugirlMilkThreshold = 0.8f;

    public float MilkThreshold
    {
        get => mugirlMilkThreshold;
        set => mugirlMilkThreshold = value;
    }

    private int ResourceUpdateIntervalTicks => 60;

    private int FullResourceRetryTicks => 60;

    public float Fullness => mugirlMilkFullness;

    public bool IsFullNow => mugirlMilkFullness >= 1f;

    public bool ActiveAndFull => Active && mugirlMilkFullness >= 1f;

    private void ExposeProductionData()
    {
        Scribe_Values.Look(ref mugirlMilkFullness, "mugirlMilkFullness", 0f);
        Scribe_Values.Look(ref mugirlMilkFullNotified, "mugirlMilkFullNotified", false);
        Scribe_Values.Look(ref mugirlMilkLastFullNotifyTick, "mugirlMilkLastFullNotifyTick", NoTick);
        Scribe_Values.Look(ref mugirlMilkPendingTicks, "mugirlMilkPendingTicks", 0);
        Scribe_Deep.Look(ref mugirlMilkAccrualContinuation, "mugirlMilkAccrualContinuation");
        Scribe_Values.Look(ref mugirlMilkThreshold, "mugirlMilkThreshold", 0.8f);
    }

    internal void RestoreAccrualWindow(int pendingTicks) => mugirlMilkPendingTicks = pendingTicks;

    private void TickProduction(int delta)
    {
        if (delta <= 0 || !Active || !TickUtility.TryGetCurrentGameTick(out int currentTick))
        {
            return;
        }

        int updateInterval = Math.Max(1, ResourceUpdateIntervalTicks);
        if (mugirlMilkAccrualContinuation is { } continuation)
        {
            continuation.Apply(this, currentTick, updateInterval);
            mugirlMilkAccrualContinuation = null;
        }

        mugirlMilkPendingTicks += delta;
        if (mugirlMilkPendingTicks < updateInterval)
        {
            return;
        }

        int elapsedTicks = mugirlMilkPendingTicks;
        mugirlMilkPendingTicks = 0;

        // 乳房 profile 查询会遍历 hediffSet，只在结算窗口内执行。
        float multiplier = parent is Pawn pawn ? UnityMathf.Max(0f, GetProductionMultiplier(pawn)) : 1f;
        float perTick = MilkProductionMath.FullnessPerTick(ResourceAmount, GatherResourcesIntervalDays, multiplier);
        mugirlMilkFullness = MilkProductionMath.Accrue(mugirlMilkFullness, perTick, elapsedTicks);

        if (mugirlMilkFullness >= 1f)
        {
            if (!mugirlMilkFullNotified || currentTick >= mugirlMilkLastFullNotifyTick + FullResourceRetryTicks)
            {
                mugirlMilkFullNotified = true;
                mugirlMilkLastFullNotifyTick = currentTick;
                OnResourceBecameFull();
            }
        }
        else
        {
            mugirlMilkFullNotified = false;
            mugirlMilkLastFullNotifyTick = NoTick;
        }
    }

    public bool TryConsumeFullness()
    {
        if (!Active || mugirlMilkFullness < 1f)
        {
            return false;
        }

        mugirlMilkFullness = 0f;
        ResetManualChangeTracking();
        return true;
    }

    public bool DevFillToFull(bool triggerNotify = true)
    {
        if (!Active)
        {
            return false;
        }

        mugirlMilkFullness = 1f;
        ResetManualChangeTracking();

        if (triggerNotify)
        {
            mugirlMilkFullNotified = true;
            mugirlMilkLastFullNotifyTick = TickUtility.CurrentGameTickOrFallback(mugirlMilkLastFullNotifyTick);
            OnResourceBecameFull();
        }

        return true;
    }

    public int GetResourceAmountForCurrentFullness()
    {
        return GenMath.RoundRandom(mugirlMilkFullness * 100f);
    }

    public int GetResourceAmountForNextGather()
    {
        return GenMath.RoundRandom(GetNextGatherFullness() * 100f);
    }

    public float GetNextGatherFullness()
    {
        return UnityMathf.Min(mugirlMilkFullness, GatherFullnessPerUse);
    }

    public string GetProductionRateExplanation()
    {
        float intervalSeconds = UnityMathf.Max(1f, GatherResourcesIntervalDays * MilkProductionMath.SecondsPerDayDisplay);
        float multiplier = parent is Pawn pawn ? UnityMathf.Max(0f, GetProductionMultiplier(pawn)) : 1f;
        float percentGain = UnityMathf.Max(0f, ResourceAmount) * multiplier;
        return "Mugirl.Milk.ProductionRate".Translate(
            percentGain.ToString("0.#", CultureInfo.InvariantCulture),
            intervalSeconds.ToString("0.#", CultureInfo.InvariantCulture));
    }

    // 按百分比消耗饱满度（用于哺乳、喝奶等场景）。
    public bool ConsumePercentage(float percentage)
    {
        if (mugirlMilkFullness <= 0f || percentage <= 0f)
        {
            return false;
        }

        mugirlMilkFullness = UnityMathf.Max(0f, mugirlMilkFullness - percentage);
        ResetManualChangeTracking();
        return true;
    }

    // 按当前奶量百分比榨乳：每 1% 奶量产出 1 份雪牛奶，每次最多消耗 20% mugirlMilkFullness。
    public bool GatheredFixed(Pawn doer, out int milkAmount)
    {
        milkAmount = 0;
        if (!Active || ResourceDef == null || mugirlMilkFullness <= 0f || !TryGetGatherMap(doer, out Map? map))
        {
            return false;
        }

        float gatheredFullness = GetNextGatherFullness();
        if (!Rand.Chance(doer.GetStatValue(StatDefOf.AnimalGatherYield, true, -1)))
        {
            ThrowProductWastedMote(doer, map);
            ConsumeGatheredFullness(gatheredFullness);
            return true;
        }

        milkAmount = GenMath.RoundRandom(gatheredFullness * 100f);
        ConsumeGatheredFullness(gatheredFullness);
        return true;
    }

    private void ConsumeGatheredFullness(float gatheredFullness)
    {
        mugirlMilkFullness = UnityMathf.Max(0f, mugirlMilkFullness - gatheredFullness);
        ResetManualChangeTracking();
    }

    // 手动改变奶量后重新开始结算窗口与满值通知。
    private void ResetManualChangeTracking()
    {
        mugirlMilkFullNotified = false;
        mugirlMilkLastFullNotifyTick = NoTick;
        mugirlMilkPendingTicks = 0;
        mugirlMilkAccrualContinuation = null;
    }

    // 采集者与产奶者必须在同一地图（或其中一方不在地图上），且采集位置有效。
    private bool TryGetGatherMap(Pawn? doer, [NotNullWhen(true)] out Map? map)
    {
        map = null;
        if (doer == null || parent == null)
        {
            return false;
        }

        Map? parentMap = parent.Map;
        Map? doerMap = doer.Map;
        if (parentMap != null && doerMap != null && parentMap != doerMap)
        {
            return false;
        }

        map = doerMap ?? parentMap;
        if (map == null)
        {
            return false;
        }

        IntVec3 gatherPosition = doerMap != null ? doer.Position : parent.Position;
        return gatherPosition.IsValid;
    }

    private void ThrowProductWastedMote(Pawn doer, Map map)
    {
        Vector3 midpoint = (doer.DrawPos.ToNumerics() + parent.DrawPos.ToNumerics()) / 2f;
        MoteMaker.ThrowText(midpoint.ToUnity(), map, "TextMote_ProductWasted".Translate(), 3.65f);
    }
}
