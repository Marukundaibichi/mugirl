using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

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

        // 显示字符串键名，用于本地化显示
        public string displayString = "MilkFullness";
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
                return this.Props.milkIntervalDays;
            }
        }

        // 获取每次产奶的量（从属性中读取）
        protected override float ResourceAmount
        {
            get
            {
                return this.Props.milkAmount;
            }
        }

        // 获取产的奶对应的物品定义（从属性中读取）
        protected override ThingDef ResourceDef
        {
            get
            {
                return this.Props.milkDef;
            }
        }

        // 获取保存键名（组合显示字符串）
        protected override string SaveKey
        {
            get
            {
                return "milkFullness" + this.Props.displayString;
            }
        }

        // 便捷属性，获取转换后的组件属性
        public CompProperties_MooMilkable Props
        {
            get
            {
                return (CompProperties_MooMilkable)this.props;
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
                // 先检查基类激活状态
                if (!base.Active)
                {
                    return false;
                }

                // 尝试将父对象转换为Pawn（角色）
                Pawn pawn = this.parent as Pawn;
                if (pawn == null)
                {
                    return false;
                }

                // 检查条件：
                // 1. 如果限制女性则检查性别
                // 2. 检查是否处于可繁殖生命周期阶段
                // 3. 检查是否是人类like种族
                return (!this.Props.milkFemaleOnly || pawn.gender == Gender.Female) &&
                       pawn.ageTracker.CurLifeStage.reproductive &&
                       pawn.RaceProps.Humanlike;
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
            // 返回本地化的显示字符串和当前饱满度百分比
            return this.Props.displayString.Translate() + ": " + base.Fullness.ToStringPercent();
        }

        private Pawn MooPawn => parent as Pawn;

        // 哺乳期生产倍率集成
        protected override float GetProductionMultiplier(Pawn pawn)
        {
            float multiplier = base.GetProductionMultiplier(pawn);

            HediffDef lactationDef = DefDatabase<HediffDef>.GetNamedSilentFail("MooGirl_Lactation");
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

            if (Active && MooPawn != null)
            {
                HediffDef lactationDef = DefDatabase<HediffDef>.GetNamedSilentFail("MooGirl_Lactation");
                if (lactationDef != null && !MooPawn.health.hediffSet.HasHediff(lactationDef))
                {
                    MooPawn.health.AddHediff(lactationDef);
                }
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
            yield return new Gizmo_MilkGauge(this, "泌乳量",
                "雪牛娘当前的乳汁饱满度。红线标记为自动挤奶阈值，到达后将由殖民者自动进行挤奶工作。");

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Dev: 奶量加满",
                    defaultDesc = "开发者模式下立刻将当前雪牛娘的奶量增加到最大。",
                    icon = TexCommand.DesirePower,
                    action = () =>
                    {
                        if (DevFillToFull(triggerNotify: false))
                        {
                            Messages.Message($"已将 {MooPawn.LabelShortCap} 的奶量加满。", MooPawn, MessageTypeDefOf.PositiveEvent);
                        }
                        else
                        {
                            Messages.Message($"{MooPawn.LabelShortCap} 当前无法加满奶量。", MooPawn, MessageTypeDefOf.RejectInput);
                        }
                    }
                };
            }
        }

        // 喷乳特效：污物 + 文字提示 + 音效
        public void SpawnMilkEffect()
        {
            if (MooPawn?.Map == null) return;

            // 生成 MilkFilth 污物（朝前方喷出）
            ThingDef milkFilth = DefDatabase<ThingDef>.GetNamedSilentFail("MooGirlMilkFilth");
            if (milkFilth != null)
            {
                for (int i = 0; i < Rand.RangeInclusive(1, 3); i++)
                {
                    IntVec3 pos = MooPawn.Position + MooPawn.Rotation.FacingCell
                        + new IntVec3(Rand.RangeInclusive(-1, 1), 0, Rand.RangeInclusive(0, 1));
                    if (pos.InBounds(MooPawn.Map))
                    {
                        Thing filth = ThingMaker.MakeThing(milkFilth);
                        GenPlace.TryPlaceThing(filth, pos, MooPawn.Map, ThingPlaceMode.Near);
                    }
                }
            }

            // 文字提示
            MoteMaker.ThrowText(MooPawn.DrawPos, MooPawn.Map, "喷乳！", Color.cyan, 3f);

            // 播放音效
            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail("MooGirl_Milking_Sound");
            sound?.PlayOneShot(new TargetInfo(MooPawn.Position, MooPawn.Map));
        }

    }
}
