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
                bool temporaryMilk = MugirlEventUtility.CanUseTemporaryMilk(MooPawn);
                if (!base.Active && !temporaryMilk)
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
            // 当前行为是在 comp 激活后尽快添加哺乳期 hediff。
            // 改为事件驱动或低频检查会改变添加时机。
            Pawn pawn = MooPawn;
            if (!Active || pawn?.health?.hediffSet == null)
            {
                return;
            }

            HediffDef lactationDef = MugirlRequiredDefs.Hediffs.MugirlLactation;
            if (lactationDef != null && !pawn.health.hediffSet.HasHediff(lactationDef))
            {
                pawn.health.AddHediff(lactationDef);
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
