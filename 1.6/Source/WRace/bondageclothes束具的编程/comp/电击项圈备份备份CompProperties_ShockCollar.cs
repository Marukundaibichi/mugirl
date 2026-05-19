using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

//namespace MooGirl
//{
//    // 定义电击项圈的属性类，继承自CompProperties
//    public class CompProperties_ShockCollar : CompProperties
//    {
//        public CompProperties_ShockCollar()
//        {
//            // 设置对应的组件类
//            compClass = typeof(Comp_ShockCollar);
//        }

//        // 普通电击效果列表
//        public List<HediffDef> hediffDefs = new List<HediffDef>();
//        // 强力电击效果列表
//        public List<HediffDef> powerhediffDefs = new List<HediffDef>();
//        // 自动触发间隔时间（tick）
//        public int ticks = 60;
//        // 自动触发的概率百分比
//        public int rand = 50;

//        // 主动使用模式的冷却时间（tick）
//        public int useCooldownTicks = 480;

//        // 强力电击按钮显示文本
//        public string powerLabel = "Power Shock";
//        // 强力电击按钮描述文本
//        public string powerDesc = "";
//        // 强力电击按钮图标路径
//        public string powerIconPath = "UI/Commands/DesirePower";

//        // 普通电击按钮显示文本
//        public string commonLabel = "Common Shock";
//        // 普通电击按钮描述文本
//        public string commonDesc = "";
//        // 普通电击按钮图标路径
//        public string commonIconPath = "UI/Commands/DesirePower";
//    }

//    // 电击项圈组件实现，继承自高级奴隶服装组件
//    public class Comp_ShockCollar : Comp_AdvancedSlaveApparel
//    {
//        // 内部计时器
//        private int ticks = 0;
//        // 记录上次手动使用的时间点
//        private int lastManualUseTick = -99999;

//        // 获取佩戴者属性（只读）
//        public Pawn Wearer => parent.ParentHolder is Pawn_ApparelTracker tracker ? tracker.pawn : null;

//        // 获取组件属性（只读）
//        public CompProperties_ShockCollar Props => (CompProperties_ShockCollar)props;

//        // 每帧调用，处理自动电击逻辑
//        public override void CompTick()
//        {
//            Pawn pawn = Wearer;
//            // 检查佩戴者是否有效
//            if (pawn == null || pawn.Dead || !pawn.Spawned || !ParentIsCracked())
//                return;

//            base.CompTick();
//            ticks--;

//            // 计时器触发
//            if (ticks <= 0)
//            {
//                // 重置计时器
//                ticks = Props.ticks;

//                // 随机数生成器
//                System.Random rand = new System.Random();
//                // 计算触发概率
//                int triggerChance = rand.Next(0, 100);

//                // 概率触发电击
//                if (triggerChance < Props.rand)
//                {
//                    // 随机选择电击类型（30%概率强力电击）
//                    int choice = rand.Next(0, 100);
//                    if (choice < 30)
//                    {
//                        // 应用强力电击效果（避免重复添加）
//                        foreach (HediffDef powerHediff in Props.powerhediffDefs)
//                        {
//                            if (powerHediff != null && !Wearer.health.hediffSet.HasHediff(powerHediff))
//                            {
//                                Wearer.health.AddHediff(HediffMaker.MakeHediff(powerHediff, Wearer));
//                            }
//                        }
//                    }
//                    else
//                    {
//                        // 应用普通电击效果（避免重复添加）
//                        foreach (HediffDef hediffDef in Props.hediffDefs)
//                        {
//                            if (hediffDef != null && !Wearer.health.hediffSet.HasHediff(hediffDef))
//                            {
//                                Wearer.health.AddHediff(HediffMaker.MakeHediff(hediffDef, Wearer));
//                            }
//                        }
//                    }
//                }
//            }
//        }

//        // 保存/加载游戏时调用，处理数据持久化
//        public override void PostExposeData()
//        {
//            base.PostExposeData();
//            // 保存内部计时器状态
//            Scribe_Values.Look(ref ticks, "ticks", 0);
//            // 保存上次手动使用时间
//            Scribe_Values.Look(ref lastManualUseTick, "lastManualUseTick", -99999);
//        }

//        // 获取装备时显示的额外Gizmo（按钮）
//        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
//        {
//            // 先返回基类的Gizmo
//            foreach (var gizmo in base.CompGetWornGizmosExtra())
//                yield return gizmo;

//            // 只有当装备已破解且存在佩戴者时显示按钮
//            if (parent is AdvancedSlaveApparel slave && slave.IsCracked() && Wearer != null)
//            {
//                // 计算当前游戏时间和冷却剩余时间
//                int currentTick = Find.TickManager.TicksGame;
//                int cdLeft = (lastManualUseTick + Props.useCooldownTicks) - currentTick;
//                bool canUse = cdLeft <= 0;

//                // 计算冷却进度百分比（用于UI显示）
//                float cooldownPercent = Mathf.InverseLerp(Props.useCooldownTicks, 0f, cdLeft);

//                // 创建强力电击按钮
//                var powerCmd = new Command_ActionWithCooldown
//                {
//                    defaultLabel = Props.powerLabel,
//                    defaultDesc = canUse ? Props.powerDesc : ((string)"MooGirl.ShockCollar.Power.CooldownTicksLeft".Translate(cdLeft)),
//                    icon = ContentFinder<Texture2D>.Get(Props.powerIconPath),
//                    action = () =>
//                    {
//                        if (!canUse) return;

//                        // 应用强力电击效果
//                        foreach (var h in Props.powerhediffDefs)
//                        {
//                            if (!Wearer.health.hediffSet.HasHediff(h))
//                            {
//                                Wearer.health.AddHediff(HediffMaker.MakeHediff(h, Wearer));
//                            }
//                        }
//                        // 记录使用时间
//                        lastManualUseTick = Find.TickManager.TicksGame;
//                    },
//                    Disabled = !canUse,
//                    cooldownPercentGetter = () => Mathf.Clamp01(cooldownPercent)
//                };
//                yield return powerCmd;

//                // 创建普通电击按钮（逻辑同上）
//                var normalCmd = new Command_ActionWithCooldown
//                {
//                    defaultLabel = Props.commonLabel,
//                    defaultDesc = canUse ? Props.commonDesc : ((string)"MooGirl.ShockCollar.Common.CooldownTicksLeft".Translate(cdLeft)),
//                    icon = ContentFinder<Texture2D>.Get(Props.commonIconPath),
//                    action = () =>
//                    {
//                        if (!canUse) return;

//                        foreach (var h in Props.hediffDefs)
//                        {
//                            if (!Wearer.health.hediffSet.HasHediff(h))
//                            {
//                                Wearer.health.AddHediff(HediffMaker.MakeHediff(h, Wearer));
//                            }
//                        }
//                        lastManualUseTick = Find.TickManager.TicksGame;
//                    },
//                    Disabled = !canUse,
//                    cooldownPercentGetter = () => Mathf.Clamp01(cooldownPercent)
//                };
//                yield return normalCmd;
//            }
//        }
//    }
//}