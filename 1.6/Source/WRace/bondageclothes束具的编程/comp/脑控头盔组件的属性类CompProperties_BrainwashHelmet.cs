using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MooGirl
{
    // 定义脑控头盔组件的属性类，用于配置组件行为和参数
    public class CompProperties_BrainwashHelmet : CompProperties
    {
        public CompProperties_BrainwashHelmet()
        {
            // 指定组件类类型，关联到Comp_BrainwashHelmet
            compClass = typeof(Comp_BrainwashHelmet);
        }

        // 主动使用技能的冷却时间（tick数）
        public int useCooldownTicks = 480;

        // 激活按钮的显示文字、描述和图标路径
        public string activateLabel = "主动洗脑";
        public string activateDesc = "";
        public string activateIconPath = "UI/Commands/DesirePower";

        // Hediff转换触发的周期，改为范围形式，随机选择
        public IntRange ticksToChange = new IntRange(20000, 60000);

        // 阻挡转换的 Hediff，如果佩戴者拥有该 Hediff，则阻止周期性切换
        public HediffDef blockHediff;

        // 定义一组 Hediff 转换条目，表示从某个Hediff转换为另一个Hediff
        public List<HediffConvertEntry> convertList = new List<HediffConvertEntry>();

        // 默认添加的 Hediff（当无匹配转换时添加）
        public HediffDef defaultHediff;

        // Hediff 转换条目类，包含检查与添加的两个 Hediff 定义
        public class HediffConvertEntry
        {
            public HediffDef hediffToCheck;
            public HediffDef hediffToAdd;
        }
    }

    // 脑控头盔组件的具体实现
    public class Comp_BrainwashHelmet : Comp_AdvancedSlaveApparel
    {

        // 非阻挡状态下计数的 tick 数，用于周期性切换
        private int nonBlockedTicks = 0;

        // 当前周期内需要达到的随机 tick 数
        private int currentTicksToChange;

        // 记录上次主动使用的游戏tick，用于冷却判断
        private int lastManualUseTick = -999999;

        // 获取当前装备的穿戴者 Pawn
        public Pawn Wearer => parent.ParentHolder is Pawn_ApparelTracker tracker ? tracker.pawn : null;

        // 简化访问属性，获得对应的属性配置
        public CompProperties_BrainwashHelmet Props => (CompProperties_BrainwashHelmet)props;

        // 当组件生成完成或加载时调用，初始化当前周期需要的 tick 数
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            // 随机选择周期 tick 数
            currentTicksToChange = Props.ticksToChange.RandomInRange;
        }

        // 组件每个游戏 tick 调用
        public override void CompTick()
        {
            base.CompTick();

            Pawn pawn = Wearer;
            if (pawn == null || pawn.Dead || !pawn.Spawned || !ParentIsCracked())
                return;

            if (Props.blockHediff != null && pawn.health.hediffSet.HasHediff(Props.blockHediff))
            {
                nonBlockedTicks = 0;
                return;
            }

            if (currentTicksToChange > 0)
            {
                nonBlockedTicks++;
                if (nonBlockedTicks >= currentTicksToChange)
                {
                    nonBlockedTicks = 0;
                    currentTicksToChange = Props.ticksToChange.RandomInRange;

                    // 周期到达，执行转换
                    ExecuteHediffLogic(pawn);
                }
            }
        }


        // 提供额外的穿戴者 Gizmo (按钮)
        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            // 先返回基类的所有 Gizmo
            foreach (var g in base.CompGetWornGizmosExtra())
                yield return g;

            // 只有装备被破解且有穿戴者时显示主动洗脑按钮
            if (parent is AdvancedSlaveApparel slave && slave.IsCracked() && Wearer != null)
            {
                int currentTick = Find.TickManager.TicksGame;
                int cdLeft = (lastManualUseTick + Props.useCooldownTicks) - currentTick;
                bool canUse = cdLeft <= 0;

                // 计算冷却进度比例，0-1 之间
                float cooldownPercent = Mathf.InverseLerp(Props.useCooldownTicks, 0f, cdLeft);

                // 返回带冷却效果的按钮
                yield return new Command_ActionWithCooldown
                {
                    defaultLabel = Props.activateLabel,
                    defaultDesc = Props.activateDesc,
                    icon = ContentFinder<Texture2D>.Get(Props.activateIconPath),
                    action = () =>
                    {
                        if (!canUse) return;

                        // 主动触发 Hediff 转换逻辑
                        ExecuteHediffLogic(slave.Wearer);
                        lastManualUseTick = Find.TickManager.TicksGame;

                        // 显示消息提示
                        Messages.Message("MooGirl.BrainwashHelmet_ManualTrigger_Message".Translate(slave.Wearer.LabelShortCap), MessageTypeDefOf.PositiveEvent);
                    },
                    Disabled = !canUse,
                    cooldownPercentGetter = () => Mathf.Clamp01(cooldownPercent)
                };

                // 文化转换按钮（仅在意识形态激活时显示）
                if (ModsConfig.IdeologyActive)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "文化转换",
                        defaultDesc = "将穿戴者的文化改变为殖民地的主流文化。",
                        icon = ContentFinder<Texture2D>.Get(Props.activateIconPath),
                        action = () =>
                        {
                            GameComponent_BrainwashPerformance.StartFor(Wearer, GetBrainwashPerformanceHediffDefFor(Wearer));
                            MooGirl_IdeoUtility.AdoptPlayerPrimaryIdeo(Wearer);
                            Messages.Message("已将" + Wearer.LabelShortCap + "的文化转换为殖民地主流文化。", Wearer, MessageTypeDefOf.PositiveEvent);
                        }
                    };
                }
            }
        }

        private HediffDef GetBrainwashPerformanceHediffDefFor(Pawn pawn)
        {
            if (pawn?.health?.hediffSet != null && Props.convertList != null)
            {
                foreach (var entry in Props.convertList)
                {
                    if (entry?.hediffToCheck == null || entry.hediffToAdd == null)
                        continue;

                    if (pawn.health.hediffSet.GetFirstHediffOfDef(entry.hediffToCheck) != null)
                    {
                        return entry.hediffToAdd;
                    }
                }
            }

            return Props.defaultHediff;
        }

        // 核心 Hediff 转换逻辑
        private void ExecuteHediffLogic(Pawn pawn)
        {
            var convertList = Props.convertList;
            bool converted = false;

            // 遍历所有预设转换条目
            foreach (var entry in convertList)
            {
                if (entry.hediffToCheck == null || entry.hediffToAdd == null)
                    continue;

                // 查找 Pawn 身上是否已有需要被转换的 Hediff
                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(entry.hediffToCheck);
                if (existing != null)
                {
                    // 移除已有 Hediff
                    pawn.health.RemoveHediff(existing);

                    // 找到大脑部位进行添加
                    var brain = pawn.health.hediffSet.GetBrain();
                    if (brain != null && !pawn.health.hediffSet.HasHediff(entry.hediffToAdd))
                    {
                        pawn.health.AddHediff(entry.hediffToAdd, brain);
                    }

                    // 标记已转换，跳出循环
                    converted = true;
                    break;
                }
            }

            // 如果没有匹配的转换，添加默认 Hediff（如果存在且尚未添加）
            if (!converted && Props.defaultHediff != null && !pawn.health.hediffSet.HasHediff(Props.defaultHediff))
            {
                var brain = pawn.health.hediffSet.GetBrain();
                if (brain != null)
                {
                    pawn.health.AddHediff(Props.defaultHediff, brain);
                }
            }
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);

            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null)
                return;

            // 遍历 convertList，移除所有相关的 hediffToAdd
            if (Props.convertList != null)
            {
                foreach (var entry in Props.convertList)
                {
                    if (entry?.hediffToAdd == null) continue;

                    var hediffs = pawn.health.hediffSet.hediffs;
                    for (int i = hediffs.Count - 1; i >= 0; i--)
                    {
                        if (hediffs[i].def == entry.hediffToAdd)
                        {
                            pawn.health.RemoveHediff(hediffs[i]);
                        }
                    }
                }
            }

            // 移除默认添加的 Hediff
            if (Props.defaultHediff != null)
            {
                var hediffs = pawn.health.hediffSet.hediffs;
                for (int i = hediffs.Count - 1; i >= 0; i--)
                {
                    if (hediffs[i].def == Props.defaultHediff)
                    {
                        pawn.health.RemoveHediff(hediffs[i]);
                    }
                }
            }
        }


        // 保存/加载状态数据
        public override void PostExposeData()
        {
            base.PostExposeData();
            // 保存是否已执行转换的标志
            // 保存非阻挡计数 tick
            Scribe_Values.Look(ref nonBlockedTicks, "nonBlockedTicks", 0);
            // 保存主动使用的最后 tick
            Scribe_Values.Look(ref lastManualUseTick, "lastManualUseTick", -999999);
            // 保存当前周期所需 tick
            Scribe_Values.Look(ref currentTicksToChange, "currentTicksToChange");
        }
    }
}
