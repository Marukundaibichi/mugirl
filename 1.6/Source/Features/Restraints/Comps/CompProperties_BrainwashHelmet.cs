using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MooGirl
{
    // 脑控头盔配置：周期性 Hediff 转换和主动按钮参数。
    public class CompProperties_BrainwashHelmet : CompProperties
    {
        public CompProperties_BrainwashHelmet()
        {
            compClass = typeof(Comp_BrainwashHelmet);
        }

        public int useCooldownTicks = 480;

        public string activateLabel = "MooGirl.Restraints.BrainwashHelmet.ActivateLabel";
        public string activateDesc = "MooGirl.Restraints.BrainwashHelmet.ActivateDesc";
        public string activateIconPath = "UI/Commands/DesirePower";

        // 每轮转换随机抽取一个周期，避免所有装备同 tick 触发。
        public IntRange ticksToChange = new IntRange(20000, 60000);

        // 阻挡转换的 Hediff，如果佩戴者拥有该 Hediff，则阻止周期性切换
        public HediffDef blockHediff;

        // 转换列表按顺序检查，命中后移除检查 Hediff 并添加目标 Hediff。
        public List<HediffConvertEntry> convertList = new List<HediffConvertEntry>();

        // 默认添加的 Hediff（当无匹配转换时添加）
        public HediffDef defaultHediff;

        public class HediffConvertEntry
        {
            public HediffDef hediffToCheck;
            public HediffDef hediffToAdd;
        }
    }

    // 脑控头盔运行逻辑：未破解时自动周期转换，破解后提供主动控制。
    public class Comp_BrainwashHelmet : Comp_AdvancedSlaveApparel
    {

        private int nonBlockedTicks = 0;

        private int currentTicksToChange;

        private int lastManualUseTick = -999999;

        public Pawn Wearer => parent.ParentHolder is Pawn_ApparelTracker tracker ? tracker.pawn : null;

        public CompProperties_BrainwashHelmet Props => props as CompProperties_BrainwashHelmet;

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureCurrentTicksToChangeInitialized();
        }

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

                    ExecuteHediffLogic(pawn, helmetProps);
                }
            }
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (var g in base.CompGetWornGizmosExtra())
                yield return g;

            CompProperties_BrainwashHelmet helmetProps = Props;
            if (helmetProps == null)
            {
                yield break;
            }

            // 主动按钮只在装备破解后显示；点击时仍会重新检查状态和冷却。
            if (parent is AdvancedSlaveApparel slave && slave.IsCracked() && Wearer != null)
            {
                int cdLeft = ManualCooldownTicksLeft(helmetProps);
                bool canUse = cdLeft <= 0;

                float cooldownPercent = ManualCooldownPercent(helmetProps);
                Texture2D activateIcon = GetCommandIcon(helmetProps.activateIconPath);

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

                        ExecuteHediffLogic(wearer, currentProps);
                        lastManualUseTick = currentTick;

                        Messages.Message("MooGirl.Restraints.BrainwashHelmet.ManualTriggerMessage".Translate(wearer.LabelShortCap), MessageTypeDefOf.PositiveEvent);
                    },
                    Disabled = !canUse,
                    cooldownPercentGetter = () => ManualCooldownPercent(Props, cooldownPercent)
                };

                // Ideology 激活时额外提供文化转换按钮。
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

        // 核心转换逻辑：优先执行 convertList，未命中时添加默认 Hediff。
        private void ExecuteHediffLogic(Pawn pawn, CompProperties_BrainwashHelmet helmetProps)
        {
            var convertList = helmetProps?.convertList;
            bool converted = false;

            if (pawn?.health?.hediffSet != null && convertList != null)
            {
                foreach (var entry in convertList)
                {
                    if (entry.hediffToCheck == null || entry.hediffToAdd == null)
                        continue;

                    Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(entry.hediffToCheck);
                    if (existing != null)
                    {
                        pawn.health.RemoveHediff(existing);

                        // 洗脑类 Hediff 尽量挂在大脑部位，找不到则不添加。
                        var brain = pawn.health.hediffSet.GetBrain();
                        if (brain != null && !pawn.health.hediffSet.HasHediff(entry.hediffToAdd))
                        {
                            pawn.health.AddHediff(entry.hediffToAdd, brain);
                        }

                        converted = true;
                        break;
                    }
                }
            }

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

            // 卸下时移除本头盔可能添加的所有目标 Hediff。
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

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nonBlockedTicks, "nonBlockedTicks", 0);
            Scribe_Values.Look(ref lastManualUseTick, "lastManualUseTick", -999999);
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
