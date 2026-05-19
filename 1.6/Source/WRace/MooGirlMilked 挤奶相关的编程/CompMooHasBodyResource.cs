using RimWorld;
using UnityEngine;
using Verse;

namespace MooGirl
{
    // 抽象组件类：用于管理具有身体资源的实体（如动物乳房资源收集）
    public abstract class CompMooHasBodyResource : ThingComp
    {
        // 抽象属性：资源收集间隔天数（由子类实现）
        protected abstract float GatherResourcesIntervalDays { get; }

        // 抽象属性：每次收集的资源量（由子类实现）
        protected abstract float ResourceAmount { get; }

        // 抽象属性：资源对应的物品定义（由子类实现）
        protected abstract ThingDef ResourceDef { get; }

        // 抽象属性：存档键名（由子类实现）
        protected abstract string SaveKey { get; }

        // 当前资源饱满度（0-1之间）
        private float fullness;

        // 防止满值后每 tick 重复触发接管尝试
        private bool fullNotified;

        private int lastFullNotifyTick = -99999;

        private int lastResourceUpdateTick = -99999;

        protected virtual int ResourceUpdateIntervalTicks => 60;

        protected virtual int FullResourceRetryTicks => 60;

        // 公开只读属性：获取当前饱满度
        public float Fullness => fullness;

        // 公开只读属性：当前是否已满，供设备安全接管产物逻辑
        public bool IsFullNow => fullness >= 1f;

        // 虚拟属性：是否激活（默认检查父对象是否有派系）
        public virtual bool Active => parent.Faction != null;

        // 复合属性：是否激活且饱满度已满
        public bool ActiveAndFull => Active && fullness >= 1f;

        // 重写方法：处理数据暴露（用于存档读写）
        public override void PostExposeData()
        {
            base.PostExposeData();
            // 读写饱满度数据
            Scribe_Values.Look(ref fullness, SaveKey, 0f);
            Scribe_Values.Look(ref fullNotified, SaveKey + "_fullNotified", false);
            Scribe_Values.Look(ref lastFullNotifyTick, SaveKey + "_lastFullNotifyTick", -99999);
            Scribe_Values.Look(ref lastResourceUpdateTick, SaveKey + "_lastResourceUpdateTick", -99999);
            Scribe_Values.Look(ref MilkThreshold, SaveKey + "_MilkThreshold", 0.8f);
        }

        // 重写方法：每帧调用，处理资源增长逻辑
        public override void CompTick()
        {
            // 如果未激活则直接返回
            if (!Active)
            {
                return;
            }

            // 尝试将父对象转换为Pawn类型
            Pawn pawn = parent as Pawn;
            if (pawn != null)
            {
                // 根据角色拥有的乳房相关Hediff（健康状态）设置资源增长速度系数
                if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("HugeBreasts"), false) ||
                    pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("BionicBreasts"), false) ||
                    pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("SlimeBreasts"), false) ||
                    pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("GR_MuffaloMammaries"), false))
                {
                    BreastSizeDays = 3f; // 大型/特殊乳房生长速度
                }
                else if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("Breasts"), false) ||
                         pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("HydraulicBreasts"), false) ||
                         (pawn.gender == Gender.Female && DefDatabase<HediffDef>.GetNamedSilentFail("Breasts") == null))
                {
                    BreastSizeDays = 1.2f; // 普通女性乳房生长速度
                }
                else if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("SmallBreasts"), false))
                {
                    BreastSizeDays = 1f; // 小型乳房生长速度
                }
                else if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("LargeBreasts"), false) ||
                         pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("ArchotechBreasts"), false))
                {
                    BreastSizeDays = 1.5f; // 大型乳房生长速度
                }
                else if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("FlatBreasts"), false) ||
                         pawn.gender == Gender.Male)
                {
                    BreastSizeDays = 0.85f; // 男性/平胸生长速度
                }
            }

            int currentTick = Find.TickManager.TicksGame;
            int updateInterval = Mathf.Max(1, ResourceUpdateIntervalTicks);
            if (lastResourceUpdateTick < 0)
            {
                lastResourceUpdateTick = currentTick;
                return;
            }

            int elapsedTicks = currentTick - lastResourceUpdateTick;
            if (elapsedTicks < updateInterval)
            {
                return;
            }

            lastResourceUpdateTick = currentTick;

            // 每 60 tick 恢复 0.12% 奶量。
            float increment = 0.0012f / updateInterval;
            // 增加饱满度并限制最大值
            fullness += increment * elapsedTicks;
            if (fullness > 1f)
            {
                fullness = 1f;
            }

            if (fullness >= 1f)
            {
                if (!fullNotified || currentTick >= lastFullNotifyTick + FullResourceRetryTicks)
                {
                    fullNotified = true;
                    lastFullNotifyTick = currentTick;
                    OnResourceBecameFull();
                }
            }
            else
            {
                fullNotified = false;
                lastFullNotifyTick = -99999;
            }
        }

        protected virtual void OnResourceBecameFull()
        {
        }

        // 方法：处理资源收集行为
        public void Gathered(Pawn doer)
        {
            // 如果未激活则记录错误并返回
            if (!Active)
            {
                Log.Error($"MooGil. {doer} gathered body resources while not Active: {parent}");
                return;
            }

            // 根据操作者的动物收集产量统计值随机判断是否收集成功
            if (!Rand.Chance(doer.GetStatValue(StatDefOf.AnimalGatherYield, true, -1)))
            {
                // 显示收集失败的文字提示
                MoteMaker.ThrowText((doer.DrawPos + parent.DrawPos) / 2f, parent.Map, "TextMote_ProductWasted".Translate(), 3.65f);
            }
            else
            {
                // 再次检查乳房类型设置收集产量系数
                Pawn pawn = parent as Pawn;
                if (pawn != null)
                {
                    if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("HugeBreasts"), false) ||
                        pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("BionicBreasts"), false) ||
                        pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("SlimeBreasts"), false) ||
                        pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("GR_MuffaloMammaries"), false))
                    {
                        BreastSize = 1.5f; // 大型乳房产量系数
                    }
                    else if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("Breasts"), false) ||
                             pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("HydraulicBreasts"), false) ||
                             (pawn.gender == Gender.Female && DefDatabase<HediffDef>.GetNamedSilentFail("Breasts") == null))
                    {
                        BreastSize = 1f; // 普通乳房产量系数
                    }
                    else if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("SmallBreasts"), false))
                    {
                        BreastSize = 0.75f; // 小型乳房产量系数
                    }
                    else if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("LargeBreasts"), false) ||
                             pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("ArchotechBreasts"), false))
                    {
                        BreastSize = 1.25f; // 大型乳房产量系数
                    }
                    else if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("FlatBreasts"), false) ||
                             pawn.gender == Gender.Male)
                    {
                        BreastSize = 0.5f; // 男性/平胸产量系数
                    }
                }

                // 计算实际收集数量：每 1% 奶量产出 1 份雪牛奶
                int amount = GenMath.RoundRandom(fullness * 100f);

                // 分批生成资源物品
                while (amount > 0)
                {
                    // 计算当前批次数量（不超过物品堆叠上限）
                    int stack = Mathf.Clamp(amount, 1, ResourceDef.stackLimit);
                    amount -= stack;

                    // 创建物品并尝试放置到世界中
                    Thing thing = ThingMaker.MakeThing(ResourceDef);
                    thing.stackCount = stack;
                    GenPlace.TryPlaceThing(thing, doer.Position, doer.Map, ThingPlaceMode.Near);
                }
            }
            // 重置饱满度
            fullness = 0f;
            fullNotified = false;
            lastFullNotifyTick = -99999;
            lastResourceUpdateTick = Find.TickManager.TicksGame;
        }

        public bool TryConsumeFullness()
        {
            if (!Active || fullness < 1f)
            {
                return false;
            }

            fullness = 0f;
            fullNotified = false;
            lastFullNotifyTick = -99999;
            lastResourceUpdateTick = Find.TickManager.TicksGame;
            return true;
        }

        public bool DevFillToFull(bool triggerNotify = true)
        {
            if (!Active)
            {
                return false;
            }

            fullness = 1f;
            fullNotified = false;
            lastFullNotifyTick = -99999;
            lastResourceUpdateTick = Find.TickManager.TicksGame;

            if (triggerNotify)
            {
                fullNotified = true;
                lastFullNotifyTick = Find.TickManager.TicksGame;
                OnResourceBecameFull();
            }

            return true;
        }

        public int GetResourceAmountForCurrentFullness()
        {
            Pawn pawn = parent as Pawn;
            if (pawn != null)
            {
                if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("HugeBreasts"), false) ||
                    pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("BionicBreasts"), false) ||
                    pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("SlimeBreasts"), false) ||
                    pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("GR_MuffaloMammaries"), false))
                {
                    BreastSize = 1.5f;
                }
                else if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("Breasts"), false) ||
                         pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("HydraulicBreasts"), false) ||
                         (pawn.gender == Gender.Female && DefDatabase<HediffDef>.GetNamedSilentFail("Breasts") == null))
                {
                    BreastSize = 1f;
                }
                else if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("SmallBreasts"), false))
                {
                    BreastSize = 0.75f;
                }
                else if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("LargeBreasts"), false) ||
                         pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("ArchotechBreasts"), false))
                {
                    BreastSize = 1.25f;
                }
                else if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("FlatBreasts"), false) ||
                         pawn.gender == Gender.Male)
                {
                    BreastSize = 0.5f;
                }
            }

            return GenMath.RoundRandom(fullness * 100f);
        }

        // 自动挤奶阈值（0-1，默认 0.8 = 80%）
        public float MilkThreshold = 0.8f;

        // 按百分比消耗饱满度（用于哺乳、喝奶等场景）
        public bool ConsumePercentage(float percentage)
        {
            if (fullness <= 0f || percentage <= 0f) return false;
            fullness = Mathf.Max(0f, fullness - percentage);
            fullNotified = false;
            lastFullNotifyTick = -99999;
            lastResourceUpdateTick = Find.TickManager.TicksGame;
            return true;
        }

        // 按当前奶量百分比榨乳：每 1% 奶量产出 1 份雪牛奶，消耗所有 fullness
        public bool GatheredFixed(Pawn doer, out int milkAmount)
        {
            milkAmount = 0;
            if (!Active || fullness <= 0f)
            {
                return false;
            }

            if (!Rand.Chance(doer.GetStatValue(StatDefOf.AnimalGatherYield, true, -1)))
            {
                MoteMaker.ThrowText((doer.DrawPos + parent.DrawPos) / 2f, parent.Map, "TextMote_ProductWasted".Translate(), 3.65f);
                ResetFullness();
                return true;
            }

            milkAmount = GenMath.RoundRandom(fullness * 100f);
            ResetFullness();
            return true;
        }

        private void ResetFullness()
        {
            fullness = 0f;
            fullNotified = false;
            lastFullNotifyTick = -99999;
            lastResourceUpdateTick = Find.TickManager.TicksGame;
        }

        // 生产倍率虚方法：子类可重写以集成哺乳期等外部因素
        protected virtual float GetProductionMultiplier(Pawn pawn)
        {
            return 1f;
        }

        // 提取乳房大小判断逻辑为独立方法
        private void UpdateBreastSize(Pawn pawn)
        {
            if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("HugeBreasts"), false) ||
                pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("BionicBreasts"), false) ||
                pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("SlimeBreasts"), false) ||
                pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("GR_MuffaloMammaries"), false))
            {
                BreastSize = 1.5f;
            }
            else if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("Breasts"), false) ||
                     pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("HydraulicBreasts"), false) ||
                     (pawn.gender == Gender.Female && DefDatabase<HediffDef>.GetNamedSilentFail("Breasts") == null))
            {
                BreastSize = 1f;
            }
            else if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("SmallBreasts"), false))
            {
                BreastSize = 0.75f;
            }
            else if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("LargeBreasts"), false) ||
                     pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("ArchotechBreasts"), false))
            {
                BreastSize = 1.25f;
            }
            else if (pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail("FlatBreasts"), false) ||
                     pawn.gender == Gender.Male)
            {
                BreastSize = 0.5f;
            }
        }

        // 保护字段：乳房产量系数（影响收集数量）
        protected float BreastSize = 1f;
        // 保护字段：乳房生长速度系数（影响饱满度增长速度）
        protected float BreastSizeDays = 1f;
    }
}
