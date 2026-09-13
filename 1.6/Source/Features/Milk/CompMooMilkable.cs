using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Mugirl
{
    // 可产奶组件的 XML 配置。
    public class CompProperties_MooMilkable : CompProperties
    {
        public CompProperties_MooMilkable()
        {
            this.compClass = typeof(CompMooMilkable);
        }

        // 显示名可能来自 XML 实际文本，也可能使用默认 Keyed。
        public string displayString = "Mugirl.Milk.FullnessDisplay";
        // 存档字段名必须与显示文本解耦，避免翻译调整影响存档结构。
        public string saveKey = "milkFullness";
        public float milkAmount = 1f;
        public bool milkFemaleOnly = true;
        public ThingDef milkDef;
        public float milkIntervalDays;
    }

    // 雪牛娘产奶组件：负责产奶条件、设备接管、哺乳期倍率和显示 Gizmo。
    public class CompMooMilkable : CompMooHasBodyResource
    {
        private Comp_MilkingDevice cachedMilkingDevice;

        // 仅缓存列表位置，不保存 Hediff 引用或缺失结果。每次使用都检查当前位置，
        // 移除、重排、读档或替换 hediffSet 后会自动重新定位，无需延后恢复哺乳状态。
        private int cachedLactationHediffIndex = -1;

        protected override float GatherResourcesIntervalDays
        {
            get
            {
                return this.Props?.milkIntervalDays ?? 1f;
            }
        }

        protected override float ResourceAmount
        {
            get
            {
                return this.Props?.milkAmount ?? 0f;
            }
        }

        protected override ThingDef ResourceDef
        {
            get
            {
                return this.Props?.milkDef;
            }
        }

        protected override string SaveKey
        {
            get
            {
                return Props?.saveKey ?? "milkFullness";
            }
        }

        public CompProperties_MooMilkable Props
        {
            get
            {
                return props as CompProperties_MooMilkable;
            }
        }

        public bool IsManagedByMilkingDevice
        {
            get
            {
                return GetActiveMilkingDevice() != null;
            }
        }

        public Comp_MilkingDevice GetActiveMilkingDevice()
        {
            Pawn pawn = parent as Pawn;
            if (pawn?.apparel?.WornApparel == null)
            {
                cachedMilkingDevice = null;
                return null;
            }

            if (cachedMilkingDevice != null && cachedMilkingDevice.CanManageMilkSource(this))
            {
                return cachedMilkingDevice;
            }

            for (int i = 0; i < pawn.apparel.WornApparel.Count; i++)
            {
                Comp_MilkingDevice milkingDevice = pawn.apparel.WornApparel[i].TryGetComp<Comp_MilkingDevice>();
                if (milkingDevice != null && milkingDevice.CanManageMilkSource(this))
                {
                    cachedMilkingDevice = milkingDevice;
                    return milkingDevice;
                }
            }

            cachedMilkingDevice = null;
            return null;
        }

        protected override void OnResourceBecameFull()
        {
            base.OnResourceBecameFull();
            TryHandleFullByMilkingDevice();
        }

        public bool TryHandleFullByMilkingDevice()
        {
            if (!IsFullNow)
            {
                return false;
            }

            Comp_MilkingDevice milkingDevice = GetActiveMilkingDevice();
            return milkingDevice != null && milkingDevice.TryAcceptFullMilk(this);
        }

        public override bool Active
        {
            get
            {
                if (!base.Active && !MugirlEventUtility.CanUseTemporaryMilk(MooPawn))
                {
                    return false;
                }

                return CanProduceMilk(MooPawn);
            }
        }

        public override string CompInspectStringExtra()
        {
            if (!this.Active)
            {
                return null;
            }
            // 显示格式进入翻译键，避免不同语言下冒号和空格规则固定在 C# 中。
            CompProperties_MooMilkable milkProps = Props;
            return "Mugirl.Milk.FullnessInspect".Translate(MugirlText.Resolve(milkProps?.displayString ?? "Mugirl.Milk.FullnessDisplay"), base.Fullness.ToStringPercent()).ToString();
        }

        private Pawn MooPawn => parent as Pawn;

        // 哺乳期 Hediff 可以在基础产量之外继续调整生产倍率。
        protected override float GetProductionMultiplier(Pawn pawn)
        {
            float multiplier = base.GetProductionMultiplier(pawn);
            if (pawn?.health?.hediffSet == null)
            {
                return multiplier;
            }

            HediffDef lactationDef = MugirlRequiredDefs.Hediffs.MugirlLactation;
            if (lactationDef != null)
            {
                Hediff lactationHediff = pawn.health.hediffSet.GetFirstHediffOfDef(lactationDef);
                if (lactationHediff != null)
                {
                    HediffComp_Lactation comp = lactationHediff.TryGetComp<HediffComp_Lactation>();
                    if (comp != null)
                    {
                        multiplier *= comp.ProductionMultiplier;
                    }
                }
            }

            return multiplier;
        }

        public override void CompTick()
        {
            base.CompTick();
            EnsureLactationHediff();
        }

        private bool CanProduceMilk(Pawn pawn)
        {
            CompProperties_MooMilkable milkProps = Props;
            return pawn != null
                && milkProps != null
                && (!milkProps.milkFemaleOnly || pawn.gender == Gender.Female)
                && pawn.ageTracker?.CurLifeStage?.reproductive == true
                && pawn.RaceProps?.Humanlike == true;
        }

        private void EnsureLactationHediff()
        {
            Pawn pawn = MooPawn;
            List<Hediff> hediffs = pawn?.health?.hediffSet?.hediffs;
            HediffDef lactationDef = MugirlRequiredDefs.Hediffs.MugirlLactation;
            if (hediffs == null || lactationDef == null)
            {
                cachedLactationHediffIndex = -1;
                return;
            }

            // 常态只做 O(1) 位置校验；存在目标时不必重复基础 CompTick 的 Active 判断。
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

            // 保留原来的逐 tick 添加时机；AddHediff 可能被兼容补丁阻止或重入修改列表，
            // 不假定新条目一定追加成功，下一次 tick 再按实际列表定位。
            pawn.health.AddHediff(lactationDef);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                cachedLactationHediffIndex = -1;
                cachedMilkingDevice = null;
            }
        }

        // 选中雪牛娘时显示奶量槽；开发模式下附带填满按钮。
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!Active || MooPawn == null)
            {
                yield break;
            }

            yield return new Gizmo_MilkGauge(this,
                "Mugirl.Milk.Gauge.Label".Translate(),
                "Mugirl.Milk.Gauge.Desc".Translate());

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Mugirl.Milk.DevFill.Label".Translate(),
                    defaultDesc = "Mugirl.Milk.DevFill.Desc".Translate(),
                    icon = TexCommand.DesirePower,
                    action = () =>
                    {
                        Pawn pawn = MooPawn;
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
                };
            }
        }

        // 手动榨乳完成后的喷乳反馈。
        public void SpawnMilkEffect()
        {
            MugirlMilkEffectUtility.SpawnMilkSpray(MooPawn);
        }

    }
}
