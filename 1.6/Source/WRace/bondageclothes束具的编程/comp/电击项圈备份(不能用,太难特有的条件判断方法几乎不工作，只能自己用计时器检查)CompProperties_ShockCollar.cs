//using RimWorld;
//using System.Collections.Generic;
//using UnityEngine;
//using Verse;

//namespace MooGirl
//{
//    // 定义电击项圈的属性类，继承自CompProperties
//    public class CompProperties_ShockCollar : CompProperties
//    {
//        public CompProperties_ShockCollar()
//        {
//            compClass = typeof(Comp_ShockCollar);
//        }

//        // 普通电击效果列表
//        public List<HediffDef> hediffDefs = new List<HediffDef>();
//        // 强力电击效果列表
//        public List<HediffDef> powerhediffDefs = new List<HediffDef>();
//        // 自动触发间隔时间（tick）
//        public int ticks = 60;
//        // 自动触发的概率百分比（0-100）
//        public int rand = 50;
//        // 强力电击的概率百分比（0-100）
//        public int powerShockChance = 30;

//        // 主动使用模式的冷却时间（tick）
//        public int useCooldownTicks = 480;

//        // 强力电击按钮显示文本
//        public string powerLabel = "Power Shock";
//        public string powerDesc = "";
//        public string powerIconPath = "UI/Commands/DesirePower";

//        // 普通电击按钮显示文本
//        public string commonLabel = "Common Shock";
//        public string commonDesc = "";
//        public string commonIconPath = "UI/Commands/DesirePower";
//    }

//    // 电击项圈组件实现
//    public class Comp_ShockCollar : Comp_AdvancedSlaveApparel
//    {
//        // 内部计时器
//        private int ticksUntilNextCheck = 0;
//        // 记录上次手动使用的时间点
//        private int lastManualUseTick = -99999;
//        // 缓存的佩戴者引用
//        private Pawn cachedWearer = null;
//        // 缓存是否已破解的状态
//        private bool cachedIsCracked = false;

//        // 获取组件属性
//        public CompProperties_ShockCollar Props => (CompProperties_ShockCollar)props;

//        // 当装备被穿戴时调用 - 初始化缓存
//        public override void Notify_Equipped(Pawn pawn)
//        {
//            base.Notify_Equipped(pawn);
//            cachedWearer = pawn;
//            cachedIsCracked = ParentIsCracked();
//            ticksUntilNextCheck = Props.ticks;
//        }

//        // 当装备被卸下时调用 - 清除缓存
//        public override void Notify_Unequipped(Pawn pawn)
//        {
//            base.Notify_Unequipped(pawn);
//            cachedWearer = null;
//        }

//        // 使用CompTickRare代替CompTick（每250tick执行一次，约4秒）
//        // 对于自动电击效果来说，这个频率已经足够了
//        public override void CompTickRare()
//        {
//            base.CompTickRare();

//            // 提前返回优化 - 先做最轻量的检查
//            if (cachedWearer == null || !cachedIsCracked)
//                return;

//            // 然后检查pawn状态（这些检查比较轻量）
//            if (cachedWearer.Dead || !cachedWearer.Spawned)
//            {
//                cachedWearer = null; // 清除无效缓存
//                return;
//            }

//            // 减少计时器（CompTickRare每次是250 ticks）
//            ticksUntilNextCheck -= 250;

//            // 计时器触发
//            if (ticksUntilNextCheck <= 0)
//            {
//                // 重置计时器
//                ticksUntilNextCheck = Props.ticks;

//                // 使用RimWorld内置的随机数系统（性能更好）
//                // 概率触发电击
//                if (Rand.Chance(Props.rand / 100f))
//                {
//                    // 随机选择电击类型
//                    if (Rand.Chance(Props.powerShockChance / 100f))
//                    {
//                        ApplyHediffs(Props.powerhediffDefs);
//                    }
//                    else
//                    {
//                        ApplyHediffs(Props.hediffDefs);
//                    }
//                }
//            }
//        }

//        // 提取公共方法 - 应用健康效果
//        private void ApplyHediffs(List<HediffDef> hediffs)
//        {
//            if (cachedWearer == null || hediffs == null)
//                return;

//            // 批量处理，减少多次访问health.hediffSet
//            var hediffSet = cachedWearer.health.hediffSet;

//            foreach (HediffDef hediffDef in hediffs)
//            {
//                // 提前检查null
//                if (hediffDef == null)
//                    continue;

//                // 避免重复添加相同效果
//                if (!hediffSet.HasHediff(hediffDef))
//                {
//                    cachedWearer.health.AddHediff(hediffDef);
//                }
//            }
//        }

//        // 保存/加载游戏时调用
//        public override void PostExposeData()
//        {
//            base.PostExposeData();
//            Scribe_Values.Look(ref ticksUntilNextCheck, "ticksUntilNextCheck", 0);
//            Scribe_Values.Look(ref lastManualUseTick, "lastManualUseTick", -99999);

//            // 加载后重新建立缓存
//            if (Scribe.mode == LoadSaveMode.PostLoadInit)
//            {
//                if (parent.ParentHolder is Pawn_ApparelTracker tracker)
//                {
//                    cachedWearer = tracker.pawn;
//                }
//                cachedIsCracked = ParentIsCracked();
//            }
//        }

//        // 获取装备时显示的按钮
//        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
//        {
//            foreach (var gizmo in base.CompGetWornGizmosExtra())
//                yield return gizmo;

//            // 只有当装备已破解且存在佩戴者时显示按钮
//            if (!cachedIsCracked || cachedWearer == null)
//                yield break;

//            // 计算冷却信息（只计算一次）
//            int currentTick = Find.TickManager.TicksGame;
//            int cdLeft = Mathf.Max(0, (lastManualUseTick + Props.useCooldownTicks) - currentTick);
//            bool canUse = cdLeft <= 0;
//            float cooldownPercent = canUse ? 1f : Mathf.Clamp01(1f - (float)cdLeft / Props.useCooldownTicks);

//            // 强力电击按钮 - 使用string类型
//            string powerDesc = canUse
//                ? Props.powerDesc
//                : ((string)"MooGirl.ShockCollar.Power.CooldownTicksLeft".Translate(cdLeft));

//            yield return CreateShockCommand(
//                Props.powerLabel,
//                powerDesc,
//                Props.powerIconPath,
//                canUse,
//                cooldownPercent,
//                () => ApplyHediffs(Props.powerhediffDefs)
//            );

//            // 普通电击按钮 - 使用string类型
//            string commonDesc = canUse
//                ? Props.commonDesc
//                : ((string)"MooGirl.ShockCollar.Common.CooldownTicksLeft".Translate(cdLeft));

//            yield return CreateShockCommand(
//                Props.commonLabel,
//                commonDesc,
//                Props.commonIconPath,
//                canUse,
//                cooldownPercent,
//                () => ApplyHediffs(Props.hediffDefs)
//            );
//        }

//        // 提取公共方法 - 创建电击命令按钮
//        private Command_ActionWithCooldown CreateShockCommand(
//            string label,
//            string desc,
//            string iconPath,
//            bool enabled,
//            float cooldownPercent,
//            System.Action applyAction)
//        {
//            return new Command_ActionWithCooldown
//            {
//                defaultLabel = label,
//                defaultDesc = desc,
//                icon = ContentFinder<Texture2D>.Get(iconPath),
//                action = () =>
//                {
//                    if (!enabled) return;
//                    applyAction();
//                    lastManualUseTick = Find.TickManager.TicksGame;
//                },
//                Disabled = !enabled,
//                cooldownPercentGetter = () => cooldownPercent
//            };
//        }
//    }
//}