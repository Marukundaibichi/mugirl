using RimWorld;
using System.Collections.Generic;
using Verse;

namespace MooGirl
{
    // 定义可产奶组件的属性类，继承自CompProperties基类
    public class CompProperties_MooMilkable : CompProperties
    {
        // 构造函数，指定关联的组件类为CompMooMilkable
        public CompProperties_MooMilkable()
        {
            this.compClass = typeof(CompMooMilkable);
        }

        // 显示名可能来自 XML 实际文本，也可能使用默认 Keyed。
        public string displayString = "MooGirl.Milk.FullnessDisplay";
        // 存档字段名必须与显示文本解耦，避免翻译调整影响存档结构。
        public string saveKey = "milkFullness";
        // 每次产奶的量
        public float milkAmount = 1f;
        // 是否仅限女性可产奶
        public bool milkFemaleOnly = true;
        // 产的奶对应的物品定义
        public ThingDef milkDef;
        // 产奶间隔天数
        public float milkIntervalDays;
    }

    // 实际处理产奶逻辑的组件类，继承自CompMooHasBodyResource
    public class CompMooMilkable : CompMooHasBodyResource
    {
        private Comp_MilkingDevice cachedMilkingDevice;

        // 获取产奶间隔天数（从属性中读取）
        protected override float GatherResourcesIntervalDays
        {
            get
            {
                return this.Props?.milkIntervalDays ?? 1f;
            }
        }

        // 获取每次产奶的量（从属性中读取）
        protected override float ResourceAmount
        {
            get
            {
                return this.Props?.milkAmount ?? 0f;
            }
        }

        // 获取产的奶对应的物品定义（从属性中读取）
        protected override ThingDef ResourceDef
        {
            get
            {
                return this.Props?.milkDef;
            }
        }

        // 获取保存键名（组合显示字符串）
        protected override string SaveKey
        {
            get
            {
                return Props?.saveKey ?? "milkFullness";
            }
        }

        // 便捷属性，获取转换后的组件属性
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

        // 判断组件是否处于激活状态
        public override bool Active
        {
            get
            {
                if (!base.Active)
                {
                    return false;
                }

                return CanProduceMilk(MooPawn);
            }
        }

        // 获取组件额外的检查字符串（用于UI显示）
        public override string CompInspectStringExtra()
        {
            // 如果组件未激活则不显示
            if (!this.Active)
            {
                return null;
            }
            // 显示格式进入翻译键，避免不同语言下冒号和空格规则固定在 C# 中。
            CompProperties_MooMilkable milkProps = Props;
            return "MooGirl.Milk.FullnessInspect".Translate(MooGirlText.Resolve(milkProps?.displayString ?? "MooGirl.Milk.FullnessDisplay"), base.Fullness.ToStringPercent()).ToString();
        }

        private Pawn MooPawn => parent as Pawn;

        // 哺乳期生产倍率集成
        protected override float GetProductionMultiplier(Pawn pawn)
        {
            float multiplier = base.GetProductionMultiplier(pawn);
            if (pawn?.health?.hediffSet == null)
            {
                return multiplier;
            }

            HediffDef lactationDef = MooGirlRequiredDefs.Hediffs.MooGirlLactation;
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

        // 自动添加哺乳期 Hediff
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

            HediffDef lactationDef = MooGirlRequiredDefs.Hediffs.MooGirlLactation;
            if (lactationDef != null && !pawn.health.hediffSet.HasHediff(lactationDef))
            {
                pawn.health.AddHediff(lactationDef);
            }
        }

        // Gizmo：奶量量杯可视化
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!Active || MooPawn == null)
            {
                yield break;
            }

            // 奶量槽
            yield return new Gizmo_MilkGauge(this,
                "MooGirl.Milk.Gauge.Label".Translate(),
                "MooGirl.Milk.Gauge.Desc".Translate());

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "MooGirl.Milk.DevFill.Label".Translate(),
                    defaultDesc = "MooGirl.Milk.DevFill.Desc".Translate(),
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
                            Messages.Message("MooGirl.Milk.DevFill.Success".Translate(pawn.LabelShortCap), pawn, MessageTypeDefOf.PositiveEvent);
                        }
                        else
                        {
                            Messages.Message("MooGirl.Milk.DevFill.Failed".Translate(pawn.LabelShortCap), pawn, MessageTypeDefOf.RejectInput, historical: false);
                        }
                    }
                };
            }
        }

        // 喷乳特效：污物 + 文字提示 + 音效
        public void SpawnMilkEffect()
        {
            MooGirlMilkEffectUtility.SpawnMilkSpray(MooPawn);
        }

    }
}
