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
        public string activateLabel = "MooGirl.Restraints.BrainwashHelmet.ActivateLabel";
        public string activateDesc = "MooGirl.Restraints.BrainwashHelmet.ActivateDesc";
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
        public CompProperties_BrainwashHelmet Props => props as CompProperties_BrainwashHelmet;

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureCurrentTicksToChangeInitialized();
        }

        // 当组件生成完成或加载时调用，初始化当前周期需要的 tick 数
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (!respawningAfterLoad)
            {
                EnsureCurrentTicksToChangeInitialized();
            }
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            EnsureCurrentTicksToChangeInitialized();
        }

        private void EnsureCurrentTicksToChangeInitialized()
        {
            if (currentTicksToChange > 0)
                return;

            CompProperties_BrainwashHelmet helmetProps = Props;
            if (helmetProps == null)
            {
                return;
            }

            // 装备可能直接生成到穿戴栏，未必经过地图生成入口；只补非法/未初始化值，避免读档后覆盖保存的剩余周期。
            currentTicksToChange = Mathf.Max(1, helmetProps.ticksToChange.RandomInRange);
        }

        // 组件每个游戏 tick 调用
        public override void CompTick()
        {
            base.CompTick();

            Pawn pawn = Wearer;
            CompProperties_BrainwashHelmet helmetProps = Props;
            if (helmetProps == null || pawn?.health?.hediffSet == null || pawn.Dead || !pawn.Spawned || !ParentIsCracked())
                return;

            if (helmetProps.blockHediff != null && pawn.health.hediffSet.HasHediff(helmetProps.blockHediff))
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
                    currentTicksToChange = Mathf.Max(1, helmetProps.ticksToChange.RandomInRange);

                    // 周期到达，执行转换
                    ExecuteHediffLogic(pawn, helmetProps);
                }
            }
        }

        // 提供额外的穿戴者 Gizmo (按钮)
        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            // 先返回基类的所有 Gizmo
            foreach (var g in base.CompGetWornGizmosExtra())
                yield return g;

            CompProperties_BrainwashHelmet helmetProps = Props;
            if (helmetProps == null)
            {
                yield break;
            }

            // 只有装备被破解且有穿戴者时显示主动洗脑按钮
            if (parent is AdvancedSlaveApparel slave && slave.IsCracked() && Wearer != null)
            {
                int cdLeft = ManualCooldownTicksLeft(helmetProps);
                bool canUse = cdLeft <= 0;

                // 计算冷却进度比例，0-1 之间
                float cooldownPercent = ManualCooldownPercent(helmetProps);
                Texture2D activateIcon = GetCommandIcon(helmetProps.activateIconPath);

                // 返回带冷却效果的按钮
                yield return new Command_ActionWithCooldown
                {
                    defaultLabel = MooGirlText.Resolve(helmetProps.activateLabel),
                    defaultDesc = MooGirlText.Resolve(helmetProps.activateDesc),
                    icon = activateIcon,
                    action = () =>
                    {
                        CompProperties_BrainwashHelmet currentProps = Props;
                        if (!(parent is AdvancedSlaveApparel currentSlave) || !currentSlave.IsCracked() || !ManualUseReady(currentProps, out int currentTick))
                        {
                            return;
                        }
                        Pawn wearer = Wearer;
                        if (wearer?.health?.hediffSet == null)
                        {
                            return;
                        }

                        // 主动触发 Hediff 转换逻辑
                        ExecuteHediffLogic(wearer, currentProps);
                        lastManualUseTick = currentTick;

                        // 显示消息提示
                        Messages.Message("MooGirl.Restraints.BrainwashHelmet.ManualTriggerMessage".Translate(wearer.LabelShortCap), MessageTypeDefOf.PositiveEvent);
                    },
                    Disabled = !canUse,
                    cooldownPercentGetter = () => ManualCooldownPercent(Props, cooldownPercent)
                };

                // 文化转换按钮（仅在意识形态激活时显示）
                if (ModsConfig.IdeologyActive)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "MooGirl.Restraints.BrainwashHelmet.IdeoConvertLabel".Translate(),
                        defaultDesc = "MooGirl.Restraints.BrainwashHelmet.IdeoConvertDesc".Translate(),
                        icon = activateIcon,
                        action = () =>
                        {
                            CompProperties_BrainwashHelmet currentProps = Props;
                            if (currentProps == null || !(parent is AdvancedSlaveApparel currentSlave) || !currentSlave.IsCracked())
                            {
                                return;
                            }

                            Pawn wearer = Wearer;
                            if (wearer == null)
                            {
                                return;
                            }

                            GameComponent_BrainwashPerformance.StartFor(wearer, GetBrainwashPerformanceHediffDefFor(wearer, currentProps));
                            MooGirl_IdeoUtility.AdoptPlayerPrimaryIdeo(wearer);
                            Messages.Message("MooGirl.Restraints.BrainwashHelmet.IdeoConvertMessage".Translate(wearer.LabelShortCap), wearer, MessageTypeDefOf.PositiveEvent);
                        }
                    };
                }
            }
        }

        private HediffDef GetBrainwashPerformanceHediffDefFor(Pawn pawn, CompProperties_BrainwashHelmet helmetProps)
        {
            if (pawn?.health?.hediffSet != null && helmetProps?.convertList != null)
            {
                foreach (var entry in helmetProps.convertList)
                {
                    if (entry?.hediffToCheck == null || entry.hediffToAdd == null)
                        continue;

                    if (pawn.health.hediffSet.GetFirstHediffOfDef(entry.hediffToCheck) != null)
                    {
                        return entry.hediffToAdd;
                    }
                }
            }

            return helmetProps?.defaultHediff;
        }

        // 核心 Hediff 转换逻辑
        private void ExecuteHediffLogic(Pawn pawn, CompProperties_BrainwashHelmet helmetProps)
        {
            var convertList = helmetProps?.convertList;
            bool converted = false;

            // 遍历所有预设转换条目
            if (pawn?.health?.hediffSet != null && convertList != null)
            {
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
            }

            // 如果没有匹配的转换，添加默认 Hediff（如果存在且尚未添加）
            if (!converted && pawn?.health?.hediffSet != null && helmetProps?.defaultHediff != null && !pawn.health.hediffSet.HasHediff(helmetProps.defaultHediff))
            {
                var brain = pawn.health.hediffSet.GetBrain();
                if (brain != null)
                {
                    pawn.health.AddHediff(helmetProps.defaultHediff, brain);
                }
            }
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);

            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null)
                return;

            CompProperties_BrainwashHelmet helmetProps = Props;
            if (helmetProps == null)
            {
                return;
            }

            // 遍历 convertList，移除所有相关的 hediffToAdd
            if (helmetProps.convertList != null)
            {
                foreach (var entry in helmetProps.convertList)
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
            if (helmetProps.defaultHediff != null)
            {
                var hediffs = pawn.health.hediffSet.hediffs;
                for (int i = hediffs.Count - 1; i >= 0; i--)
                {
                    if (hediffs[i].def == helmetProps.defaultHediff)
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
            Scribe_Values.Look(ref currentTicksToChange, "currentTicksToChange", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureCurrentTicksToChangeInitialized();
            }
        }

        private static int CurrentGameTickOrFallback(int fallback)
        {
            return MooGirlTickUtility.CurrentGameTickOrFallback(fallback);
        }

        private static Texture2D GetCommandIcon(string iconPath)
        {
            return string.IsNullOrEmpty(iconPath) ? TexCommand.DesirePower : ContentFinder<Texture2D>.Get(iconPath, false) ?? TexCommand.DesirePower;
        }

        private bool ManualUseReady(CompProperties_BrainwashHelmet helmetProps, out int currentTick)
        {
            currentTick = CurrentGameTickOrFallback(lastManualUseTick);
            if (helmetProps == null)
            {
                return false;
            }

            return ManualCooldownTicksLeft(helmetProps, currentTick) <= 0;
        }

        private int ManualCooldownTicksLeft(CompProperties_BrainwashHelmet helmetProps)
        {
            return ManualCooldownTicksLeft(helmetProps, CurrentGameTickOrFallback(lastManualUseTick));
        }

        private int ManualCooldownTicksLeft(CompProperties_BrainwashHelmet helmetProps, int currentTick)
        {
            if (helmetProps == null)
            {
                return int.MaxValue;
            }

            int cooldownTicks = Mathf.Max(1, helmetProps.useCooldownTicks);
            return Mathf.Max(0, (lastManualUseTick + cooldownTicks) - currentTick);
        }

        private float ManualCooldownPercent(CompProperties_BrainwashHelmet helmetProps, float fallback = 0f)
        {
            if (helmetProps == null)
            {
                return Mathf.Clamp01(fallback);
            }

            int cooldownTicks = Mathf.Max(1, helmetProps.useCooldownTicks);
            int cdLeft = ManualCooldownTicksLeft(helmetProps);
            return cdLeft <= 0 ? 1f : Mathf.Clamp01(1f - (float)cdLeft / cooldownTicks);
        }
    }
}
