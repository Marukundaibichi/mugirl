using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MooGirl
{
    public class CompProperties_MagneticShackles : CompProperties
    {
        public CompProperties_MagneticShackles()
        {
            compClass = typeof(Comp_MagneticShackles);
        }

        public IntRange cycleInterval = new IntRange(15000, 55000);
        public IntRange bindDurationTicks = new IntRange(10000, 20000);

        public List<BindHediffEntry> bindHediffs = new List<BindHediffEntry>();

        public class BindHediffEntry
        {
            public HediffDef hediffDef;
            public List<BodyPartDef> bodyPartDefs;
        }

        public int useCooldownTicks = 600;

        public string activateLabel = "激活镣铐";
        public string activateDesc = "启动磁力镣铐，束缚目标。";
        public string activateIconPath = "UI/Commands/DesirePower";

        public string deactivateLabel = "解除镣铐";
        public string deactivateDesc = "解除磁力镣铐。";
        public string deactivateIconPath = "UI/Commands/DesirePower";

        public string labelWhenActive = "磁力镣铐（激活）";
        public string labelWhenInactive = "磁力镣铐（未激活）";

        public string moteTextOn = "磁力手铐启动!";
        public string moteTextOff = "磁力手铐解除!";
        public string messageOn = "{0} 的磁力手铐突然启动了";
        public string messageOff = "{0} 的磁力手铐被解除";

        public List<BodyPartGroupDef> boundBodyPartGroupDefs = new List<BodyPartGroupDef>();

        public string activateSoundDefName = null;
    }

    public class Comp_MagneticShackles : Comp_AdvancedSlaveApparel
    {
        private int tickCounter = 0;
        private int lastManualUseTick = -999999;
        private bool isActive = false;

        private int currentCycleInterval;
        private int currentBindDurationTicks;
        private int nextStateTick = -1; // 自动循环下一次状态时间

        public Pawn Wearer => parent.ParentHolder is Pawn_ApparelTracker tracker ? tracker.pawn : null;
        public CompProperties_MagneticShackles Props => (CompProperties_MagneticShackles)props;
        public bool IsActive => isActive;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            currentCycleInterval = Props.cycleInterval.RandomInRange;
            currentBindDurationTicks = Props.bindDurationTicks.RandomInRange;
            nextStateTick = -1;
        }

        public override void CompTick()
        {
            base.CompTick();
            Pawn pawn = Wearer;
            if (pawn == null || pawn.Dead || !pawn.Spawned)
                return;

            tickCounter++;

            // 如果装备已破解，清理自动循环状态并直接返回
            if (parent is AdvancedSlaveApparel slave && slave.IsCracked())
            {
                nextStateTick = -1; // 重置计时器，确保不会自动激活
                return;
            }

            // 未破解时执行自动循环
            int currentTick = Find.TickManager.TicksGame;
            if (nextStateTick < 0)
            {
                nextStateTick = currentTick + currentCycleInterval;
            }

            if (currentTick >= nextStateTick)
            {
                if (isActive)
                {
                    DeactivateShackles();
                    currentCycleInterval = Props.cycleInterval.RandomInRange; // 每次重新随机
                    nextStateTick = currentTick + currentCycleInterval;
                }
                else
                {
                    ActivateShackles();
                    currentBindDurationTicks = Props.bindDurationTicks.RandomInRange; // 每次重新随机
                    nextStateTick = currentTick + currentBindDurationTicks;
                }
            }
        }


        public void OnEquipped()
        {
            lastManualUseTick = Find.TickManager.TicksGame;
            tickCounter = 0;
            currentCycleInterval = Props.cycleInterval.RandomInRange;
            currentBindDurationTicks = Props.bindDurationTicks.RandomInRange;
            nextStateTick = -1;
        }

        public void OnUnequipped()
        {
            DeactivateShackles();
        }

        // 激活逻辑
        public void ActivateShackles()
        {
            if (isActive || Wearer == null) return;

            isActive = true;
            lastManualUseTick = Find.TickManager.TicksGame;

            foreach (var entry in Props.bindHediffs)
            {
                if (entry == null || entry.hediffDef == null) continue;

                // BodyPartDef 不为空则绑定指定部位
                if (entry.bodyPartDefs != null && entry.bodyPartDefs.Count > 0)
                {
                    foreach (var bodyPartDef in entry.bodyPartDefs)
                    {
                        if (bodyPartDef == null) continue;

                        var parts = Wearer.RaceProps.body.AllParts.Where(p => p.def == bodyPartDef);
                        foreach (var part in parts)
                        {
                            if (!Wearer.health.hediffSet.hediffs.Any(h => h.def == entry.hediffDef && h.Part == part))
                            {
                                var hd = HediffMaker.MakeHediff(entry.hediffDef, Wearer, part);
                                Wearer.health.AddHediff(hd);
                            }
                        }
                    }
                }
                else
                {
                    // 绑定全身/无部位
                    if (!Wearer.health.hediffSet.hediffs.Any(h => h.def == entry.hediffDef && h.Part == null))
                    {
                        var hd = HediffMaker.MakeHediff(entry.hediffDef, Wearer, null);
                        Wearer.health.AddHediff(hd);
                    }
                }
            }

            if (!string.IsNullOrEmpty(Props.activateSoundDefName))
            {
                var soundDef = DefDatabase<SoundDef>.GetNamedSilentFail(Props.activateSoundDefName);
                if (soundDef != null)
                    soundDef.PlayOneShot(Wearer);
            }

            MoteMaker.ThrowText(Wearer.DrawPos, Wearer.Map, Props.moteTextOn, Color.cyan);
            Messages.Message(string.Format(Props.messageOn, Wearer.LabelShortCap), Wearer, MessageTypeDefOf.NegativeEvent);
        }

        // 解除逻辑（彻底移除 Hediff）
        public void DeactivateShackles()
        {
            if (!isActive || Wearer == null) return;

            isActive = false;

            foreach (var entry in Props.bindHediffs)
            {
                if (entry == null || entry.hediffDef == null) continue;

                // 收集要移除的 Hediff（无论部位是否为空）
                List<Hediff> hediffsToRemove = new List<Hediff>();

                if (entry.bodyPartDefs == null || entry.bodyPartDefs.Count == 0)
                {
                    // 全身 / 无部位
                    hediffsToRemove.AddRange(Wearer.health.hediffSet.hediffs.Where(h => h.def == entry.hediffDef && h.Part == null));
                }
                else
                {
                    foreach (var bodyPartDef in entry.bodyPartDefs)
                    {
                        if (bodyPartDef == null) continue;
                        hediffsToRemove.AddRange(
                            Wearer.health.hediffSet.hediffs
                                .Where(h => h.def == entry.hediffDef && h.Part != null && h.Part.def == bodyPartDef)
                        );
                    }
                }

                // 移除 Hediff
                foreach (var h in hediffsToRemove)
                    Wearer.health.RemoveHediff(h);
            }

            MoteMaker.ThrowText(Wearer.DrawPos, Wearer.Map, Props.moteTextOff, Color.green);
            Messages.Message(string.Format(Props.messageOff, Wearer.LabelShortCap), Wearer, MessageTypeDefOf.PositiveEvent);
        }

        // Verb 限制
        public override bool CompAllowVerbCast(Verb verb)
        {
            if (!IsActive || Props.boundBodyPartGroupDefs == null || Props.boundBodyPartGroupDefs.Count == 0)
                return true;

            if (verb?.tool == null)
                return true;

            return !Props.boundBodyPartGroupDefs.Contains(verb.tool.linkedBodyPartsGroup);
        }

        // 手动按钮
        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (var g in base.CompGetWornGizmosExtra())
                yield return g;

            if (parent is AdvancedSlaveApparel slave && slave.IsCracked() && Wearer != null)
            {
                int currentTick = Find.TickManager.TicksGame;
                int cdLeft = (lastManualUseTick + Props.useCooldownTicks) - currentTick;
                bool canUse = cdLeft <= 0;
                float cooldownPercent = Mathf.InverseLerp(Props.useCooldownTicks, 0f, cdLeft);

                if (!isActive)
                {
                    yield return new Command_ActionWithCooldown
                    {
                        defaultLabel = Props.activateLabel,
                        defaultDesc = canUse ? Props.activateDesc : ((string)"MooGirl.MagneticShackles.CooldownTicksLeft".Translate(cdLeft)),
                        icon = ContentFinder<Texture2D>.Get(Props.activateIconPath),
                        action = () =>
                        {
                            if (!canUse) return;
                            ActivateShackles();
                            lastManualUseTick = Find.TickManager.TicksGame;
                        },
                        Disabled = !canUse,
                        cooldownPercentGetter = () => Mathf.Clamp01(cooldownPercent)
                    };
                }
                else
                {
                    yield return new Command_ActionWithCooldown
                    {
                        defaultLabel = Props.deactivateLabel,
                        defaultDesc = Props.deactivateDesc,
                        icon = ContentFinder<Texture2D>.Get(Props.deactivateIconPath),
                        action = () =>
                        {
                            if (!canUse) return;
                            DeactivateShackles();
                            lastManualUseTick = Find.TickManager.TicksGame;
                        },
                        Disabled = !canUse,
                        cooldownPercentGetter = () => Mathf.Clamp01(cooldownPercent)
                    };
                }
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref tickCounter, "tickCounter", 0);
            Scribe_Values.Look(ref lastManualUseTick, "lastManualUseTick", -999999);
            Scribe_Values.Look(ref isActive, "isActive", false);
            Scribe_Values.Look(ref currentCycleInterval, "currentCycleInterval");
            Scribe_Values.Look(ref currentBindDurationTicks, "currentBindDurationTicks");
            Scribe_Values.Look(ref nextStateTick, "nextStateTick");
        }
    }
}
