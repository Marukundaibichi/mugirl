namespace Mugirl.Features.Milk;

// 雪牛娘产奶组件：负责产奶条件、设备接管、哺乳期倍率和显示 Gizmo。
// sealed：GetComp<CompMilkProduction>() 在非雪牛娘 Pawn 上未命中时可直接返回（原版对密封类型跳过线性扫描）。
public sealed partial class CompMilkProduction : ThingComp
{
    private CompMilkingDevice? cachedMilkingDevice;

    // 仅缓存列表位置，不保存 Hediff 引用或缺失结果。每次使用都检查当前位置，
    // 移除、重排、读档或替换 hediffSet 后会自动重新定位，无需延后恢复哺乳状态。
    private int cachedLactationHediffIndex = -1;

    public CompProperties_MilkProduction? Props => props as CompProperties_MilkProduction;

    // 产奶是身体能力，不依赖阵营；坠舱、迁徙及旧档野人加入玩家前同样可以互动。
    public bool Active => CanProduceMilk(MilkPawn);

    public bool IsManagedByMilkingDevice => GetActiveMilkingDevice() != null;

    private float GatherResourcesIntervalDays => Props?.milkIntervalDays ?? 1f;

    private float ResourceAmount => Props?.milkAmount ?? 0f;

    private ThingDef? ResourceDef => Props?.milkDef;

    private Pawn? MilkPawn => parent as Pawn;

    public CompMilkingDevice? GetActiveMilkingDevice()
    {
        List<Apparel>? wornApparel = MilkPawn?.apparel?.WornApparel;
        if (wornApparel == null)
        {
            cachedMilkingDevice = null;
            return null;
        }

        if (cachedMilkingDevice != null && cachedMilkingDevice.CanManageMilkSource(this))
        {
            return cachedMilkingDevice;
        }

        for (int i = 0; i < wornApparel.Count; i++)
        {
            CompMilkingDevice? milkingDevice = wornApparel[i].TryGetComp<CompMilkingDevice>();
            if (milkingDevice != null && milkingDevice.CanManageMilkSource(this))
            {
                cachedMilkingDevice = milkingDevice;
                return milkingDevice;
            }
        }

        cachedMilkingDevice = null;
        return null;
    }

    public bool TryHandleFullByMilkingDevice()
    {
        if (!IsFullNow)
        {
            return false;
        }

        CompMilkingDevice? milkingDevice = GetActiveMilkingDevice();
        return milkingDevice != null && milkingDevice.TryAcceptFullMilk(this);
    }

    public override string? CompInspectStringExtra()
    {
        if (!Active)
        {
            return null;
        }

        // 显示格式进入翻译键，避免不同语言下冒号和空格规则固定在 C# 中。
        string displayName = MugirlText.Resolve(Props?.displayString ?? "Mugirl.Milk.FullnessDisplay");
        return "Mugirl.Milk.FullnessInspect".Translate(displayName, Fullness.ToStringPercent()).ToString();
    }

    // 哺乳期 Hediff 在基础产量之外调整生产倍率；只在产奶结算窗口（60 tick）中调用。
    private float GetProductionMultiplier(Pawn pawn)
    {
        float multiplier = BreastProfileUtility.TryGetYieldMultiplier(pawn, out float yieldMultiplier) ? yieldMultiplier : 1f;
        Hediff? lactationHediff = pawn.health?.hediffSet?.GetFirstHediffOfDef(MilkDefOf.Mugirl_Lactation);
        HediffComp_Lactation? lactation = lactationHediff?.TryGetComp<HediffComp_Lactation>();
        if (lactation != null)
        {
            multiplier *= lactation.ProductionMultiplier;
        }

        return multiplier;
    }

    public override void CompTickInterval(int delta)
    {
        base.CompTickInterval(delta);
        TickProduction(delta);
        EnsureLactationHediff();
    }

    private void OnResourceBecameFull()
    {
        TryHandleFullByMilkingDevice();
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        ExposeProductionData();
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            cachedLactationHediffIndex = -1;
            cachedMilkingDevice = null;
        }
    }

    // 选中雪牛娘时显示奶量槽；开发模式下附带调试按钮。
    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        Pawn? pawn = MilkPawn;
        if (!Active || pawn == null)
        {
            yield break;
        }

        yield return new Gizmo_MilkGauge(this, "Mugirl.Milk.Gauge.Label".Translate(), "Mugirl.Milk.Gauge.Desc".Translate());

        if (!Prefs.DevMode)
        {
            yield break;
        }

        yield return new Command_Action
        {
            defaultLabel = "Mugirl.Milk.FeedingDebug.Button".Translate(),
            defaultDesc = "Mugirl.Milk.FeedingDebug.ButtonDesc".Translate(),
            icon = TexCommand.DesirePower,
            action = () => Dialog_MilkFeedingLayoutDebug.Open(pawn),
        };

        yield return new Command_Action
        {
            defaultLabel = "Mugirl.Milk.DevFill.Label".Translate(),
            defaultDesc = "Mugirl.Milk.DevFill.Desc".Translate(),
            icon = TexCommand.DesirePower,
            action = DevFillFromGizmo,
        };
    }

    // 手动榨乳完成后的喷乳反馈。
    public void SpawnMilkEffect()
    {
        MilkEffectUtility.SpawnMilkSpray(MilkPawn);
    }

    private void DevFillFromGizmo()
    {
        Pawn? pawn = MilkPawn;
        if (pawn == null)
        {
            return;
        }

        if (DevFillToFull(triggerNotify: false))
        {
            Messages.Message("Mugirl.Milk.DevFill.Success".Translate(pawn.LabelShortCap), pawn, MessageTypeDefOf.PositiveEvent);
        }
        else
        {
            Messages.Message("Mugirl.Milk.DevFill.Failed".Translate(pawn.LabelShortCap), pawn, MessageTypeDefOf.RejectInput, historical: false);
        }
    }

    private bool CanProduceMilk(Pawn? pawn)
    {
        CompProperties_MilkProduction? milkProps = Props;
        return pawn != null
            && milkProps != null
            && (!milkProps.milkFemaleOnly || pawn.gender == Gender.Female)
            && pawn.ageTracker?.CurLifeStage?.reproductive == true
            && pawn.RaceProps?.Humanlike == true;
    }

    private void EnsureLactationHediff()
    {
        Pawn? pawn = MilkPawn;
        List<Hediff>? hediffs = pawn?.health?.hediffSet?.hediffs;
        HediffDef lactationDef = MilkDefOf.Mugirl_Lactation;
        if (pawn == null || hediffs == null)
        {
            cachedLactationHediffIndex = -1;
            return;
        }

        // 常态只做 O(1) 位置校验；存在目标时不必重复基础 Active 判断。
        if (cachedLactationHediffIndex >= 0 && cachedLactationHediffIndex < hediffs.Count
            && hediffs[cachedLactationHediffIndex].def == lactationDef)
        {
            return;
        }

        cachedLactationHediffIndex = -1;
        if (!Active)
        {
            return;
        }

        for (int i = 0; i < hediffs.Count; i++)
        {
            if (hediffs[i].def == lactationDef)
            {
                cachedLactationHediffIndex = i;
                return;
            }
        }

        // AddHediff 可能被兼容补丁阻止或重入修改列表，不假定新条目一定追加成功，下一次结算再按实际列表定位。
        pawn.health.AddHediff(lactationDef);
    }
}
