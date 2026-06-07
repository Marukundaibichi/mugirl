using RimWorld;
using UnityEngine;
using Verse;

namespace MooGirl
{
    // 抽象组件类：用于管理具有身体资源的实体（如动物乳房资源收集）
    public abstract class CompMooHasBodyResource : ThingComp
    {
        private const float GatherFullnessPerUse = 0.2f;

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
        public virtual bool Active => parent?.Faction != null;

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
            if (!Active || !MooGirlGameUtility.IsPlaying())
            {
                return;
            }

            int currentTick = CurrentGameTickOrFallback(lastResourceUpdateTick);
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

            // 乳房 profile 查询会访问 hediffSet，只在资源更新窗口内执行，避免每 tick 做无意义热路径查询。
            Pawn pawn = parent as Pawn;
            float intervalTicks = Mathf.Max(1f, GatherResourcesIntervalDays * 60000f);
            float targetFullnessGain = Mathf.Max(0f, ResourceAmount) / 100f;
            float multiplier = pawn != null ? Mathf.Max(0f, GetProductionMultiplier(pawn)) : 1f;
            float increment = targetFullnessGain * multiplier / intervalTicks;
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
            ThingDef resourceDef = ResourceDef;
            IntVec3 gatherPosition;
            Map map;
            // 如果未激活则记录错误并返回
            if (!Active || resourceDef == null || !TryGetGatherContext(doer, out map, out gatherPosition))
            {
                WarnInvalidGather(doer);
                return;
            }

            // 根据操作者的动物收集产量统计值随机判断是否收集成功
            if (!Rand.Chance(doer.GetStatValue(StatDefOf.AnimalGatherYield, true, -1)))
            {
                // 显示收集失败的文字提示
                ThrowProductWastedMote(doer, map);
            }
            else
            {
                // 计算实际收集数量：每 1% 奶量产出 1 份雪牛奶
                int amount = GenMath.RoundRandom(fullness * 100f);

                MooGirlMilkOutputUtility.SpawnStacksNear(resourceDef, amount, gatherPosition, map);
            }
            // 重置饱满度
            fullness = 0f;
            ResetManualChangeTracking();
        }

        public bool TryConsumeFullness()
        {
            if (!Active || fullness < 1f)
            {
                return false;
            }

            fullness = 0f;
            ResetManualChangeTracking();
            return true;
        }

        public bool DevFillToFull(bool triggerNotify = true)
        {
            if (!Active)
            {
                return false;
            }

            fullness = 1f;
            ResetManualChangeTracking();

            if (triggerNotify)
            {
                fullNotified = true;
                lastFullNotifyTick = CurrentGameTickOrFallback(lastFullNotifyTick);
                OnResourceBecameFull();
            }

            return true;
        }

        public int GetResourceAmountForCurrentFullness()
        {
            return GenMath.RoundRandom(fullness * 100f);
        }

        public int GetResourceAmountForNextGather()
        {
            return GenMath.RoundRandom(GetNextGatherFullness() * 100f);
        }

        public float GetNextGatherFullness()
        {
            return Mathf.Min(fullness, GatherFullnessPerUse);
        }

        public string GetProductionRateExplanation()
        {
            Pawn pawn = parent as Pawn;
            float intervalSeconds = Mathf.Max(1f, GatherResourcesIntervalDays * 1000f);
            float multiplier = pawn != null ? Mathf.Max(0f, GetProductionMultiplier(pawn)) : 1f;
            float percentGain = Mathf.Max(0f, ResourceAmount) * multiplier;
            return "MooGirl.Milk.ProductionRate".Translate(percentGain.ToString("0.#"), intervalSeconds.ToString("0.#"));
        }

        // 自动挤奶阈值（0-1，默认 0.8 = 80%）
        public float MilkThreshold = 0.8f;

        // 按百分比消耗饱满度（用于哺乳、喝奶等场景）
        public bool ConsumePercentage(float percentage)
        {
            if (fullness <= 0f || percentage <= 0f) return false;
            fullness = Mathf.Max(0f, fullness - percentage);
            ResetManualChangeTracking();
            return true;
        }

        // 按当前奶量百分比榨乳：每 1% 奶量产出 1 份雪牛奶，每次最多消耗 20% fullness
        public bool GatheredFixed(Pawn doer, out int milkAmount)
        {
            milkAmount = 0;
            Map map;
            IntVec3 gatherPosition;
            if (!Active || ResourceDef == null || fullness <= 0f || !TryGetGatherContext(doer, out map, out gatherPosition))
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
            fullness = Mathf.Max(0f, fullness - gatheredFullness);
            ResetManualChangeTracking();
        }

        private void ResetManualChangeTracking()
        {
            fullNotified = false;
            lastFullNotifyTick = -99999;
            lastResourceUpdateTick = CurrentGameTickOrFallback(lastResourceUpdateTick);
        }

        // 生产倍率虚方法：子类可重写以集成哺乳期等外部因素
        protected virtual float GetProductionMultiplier(Pawn pawn)
        {
            if (MooGirlBreastProfileUtility.TryGetYieldMultiplier(pawn, out float yieldMultiplier))
            {
                return yieldMultiplier;
            }

            return 1f;
        }

        private bool TryGetGatherContext(Pawn doer, out Map map, out IntVec3 gatherPosition)
        {
            map = null;
            gatherPosition = IntVec3.Invalid;
            if (doer == null || parent == null)
            {
                return false;
            }

            Map parentMap = parent.Map;
            Map doerMap = doer.Map;
            if (parentMap != null && doerMap != null && parentMap != doerMap)
            {
                return false;
            }

            map = doerMap ?? parentMap;
            if (map == null)
            {
                return false;
            }

            gatherPosition = doerMap != null ? doer.Position : parent.Position;
            return gatherPosition.IsValid;
        }

        private void ThrowProductWastedMote(Pawn doer, Map map)
        {
            if (doer == null || parent == null || map == null)
            {
                return;
            }

            MoteMaker.ThrowText((doer.DrawPos + parent.DrawPos) / 2f, map, "TextMote_ProductWasted".Translate(), 3.65f);
        }

        private void WarnInvalidGather(Pawn doer)
        {
            string doerLabel = doer?.LabelShortCap ?? "null";
            string parentLabel = parent?.LabelShortCap ?? "null";
            MooGirlLog.WarningOnce(SaveKey + ".GatherInactive", "MooGirl.Milk.GatherInactiveLog".Translate(doerLabel, parentLabel).ToString());
        }

        private static int CurrentGameTickOrFallback(int fallback)
        {
            return MooGirlTickUtility.CurrentGameTickOrFallback(fallback);
        }
    }
}
