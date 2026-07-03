using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Mugirl
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

        public string activateLabel = "Mugirl.Restraints.MagneticShackles.ActivateLabel";
        public string activateDesc = "Mugirl.Restraints.MagneticShackles.ActivateDesc";
        public string activateIconPath = "UI/Commands/DesirePower";

        public string deactivateLabel = "Mugirl.Restraints.MagneticShackles.DeactivateLabel";
        public string deactivateDesc = "Mugirl.Restraints.MagneticShackles.DeactivateDesc";
        public string deactivateIconPath = "UI/Commands/DesirePower";

        public string labelWhenActive = "Mugirl.Restraints.MagneticShackles.LabelActive";
        public string labelWhenInactive = "Mugirl.Restraints.MagneticShackles.LabelInactive";

        public string moteTextOn = "Mugirl.Restraints.MagneticShackles.MoteOn";
        public string moteTextOff = "Mugirl.Restraints.MagneticShackles.MoteOff";
        public string messageOn = "Mugirl.Restraints.MagneticShackles.MessageOn";
        public string messageOff = "Mugirl.Restraints.MagneticShackles.MessageOff";

        public List<BodyPartGroupDef> boundBodyPartGroupDefs = new List<BodyPartGroupDef>();

        public string activateSoundDefName = null;
    }

    public partial class Comp_MagneticShackles : Comp_AdvancedSlaveApparel
    {
        private int tickCounter = 0;
        private int lastManualUseTick = -999999;
        private bool isActive = false;

        private int currentCycleInterval;
        private int currentBindDurationTicks;
        private int nextStateTick = -1; // 自动循环下一次状态时间

        public Pawn Wearer => parent.ParentHolder is Pawn_ApparelTracker tracker ? tracker.pawn : null;
        public CompProperties_MagneticShackles Props => props as CompProperties_MagneticShackles;
        public bool IsActive => isActive;

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureCycleTimingsInitialized();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureCycleTimingsInitialized();
            if (!respawningAfterLoad)
            {
                nextStateTick = -1;
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            CompProperties_MagneticShackles shackleProps = Props;
            Pawn pawn = Wearer;
            if (shackleProps == null || pawn == null || pawn.Dead || !pawn.Spawned)
                return;

            tickCounter++;

            // 如果装备已破解，清理自动循环状态并直接返回
            if (parent is AdvancedSlaveApparel slave && slave.IsCracked())
            {
                nextStateTick = -1; // 重置计时器，确保不会自动激活
                return;
            }

            // 未破解时执行自动循环
            EnsureCycleTimingsInitialized();
            int currentTick = CurrentGameTickOrFallback(nextStateTick);
            if (nextStateTick < 0)
            {
                nextStateTick = currentTick + CurrentStateInterval;
            }

            if (currentTick >= nextStateTick)
            {
                if (isActive)
                {
                    DeactivateShackles();
                    currentCycleInterval = Mathf.Max(1, shackleProps.cycleInterval.RandomInRange); // 每次重新随机
                    nextStateTick = currentTick + currentCycleInterval;
                }
                else
                {
                    ActivateShackles();
                    currentBindDurationTicks = Mathf.Max(1, shackleProps.bindDurationTicks.RandomInRange); // 每次重新随机
                    nextStateTick = currentTick + currentBindDurationTicks;
                }
            }
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            lastManualUseTick = CurrentGameTickOrFallback(lastManualUseTick);
            tickCounter = 0;
            ResetCycleTimings();
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            DeactivateShackles(pawn, false);
            nextStateTick = -1;
        }

        private int CurrentStateInterval
        {
            get
            {
                EnsureCycleTimingsInitialized();
                return isActive ? currentBindDurationTicks : currentCycleInterval;
            }
        }

        private void EnsureCycleTimingsInitialized()
        {
            CompProperties_MagneticShackles shackleProps = Props;
            if (shackleProps == null)
            {
                return;
            }

            if (currentCycleInterval <= 0)
            {
                currentCycleInterval = Mathf.Max(1, shackleProps.cycleInterval.RandomInRange);
            }
            if (currentBindDurationTicks <= 0)
            {
                currentBindDurationTicks = Mathf.Max(1, shackleProps.bindDurationTicks.RandomInRange);
            }
        }

        private void ResetCycleTimings()
        {
            CompProperties_MagneticShackles shackleProps = Props;
            if (shackleProps == null)
            {
                currentCycleInterval = 0;
                currentBindDurationTicks = 0;
                nextStateTick = -1;
                return;
            }

            currentCycleInterval = Mathf.Max(1, shackleProps.cycleInterval.RandomInRange);
            currentBindDurationTicks = Mathf.Max(1, shackleProps.bindDurationTicks.RandomInRange);
            nextStateTick = -1;
        }

        // 激活逻辑
        public void ActivateShackles()
        {
            CompProperties_MagneticShackles shackleProps = Props;
            Pawn wearer = Wearer;
            if (isActive || shackleProps == null || wearer?.health?.hediffSet == null) return;

            isActive = true;
            lastManualUseTick = CurrentGameTickOrFallback(lastManualUseTick);

            if (shackleProps.bindHediffs != null)
            {
                foreach (var entry in shackleProps.bindHediffs)
                {
                    if (entry == null || entry.hediffDef == null) continue;

                    // 配置了 BodyPartDef 时只给对应部位添加束缚 Hediff。
                    if (entry.bodyPartDefs != null && entry.bodyPartDefs.Count > 0 && wearer.RaceProps?.body?.AllParts != null)
                    {
                        foreach (var bodyPartDef in entry.bodyPartDefs)
                        {
                            if (bodyPartDef == null) continue;

                            List<BodyPartRecord> parts = wearer.RaceProps.body.AllParts;
                            for (int i = 0; i < parts.Count; i++)
                            {
                                BodyPartRecord part = parts[i];
                                if (part.def != bodyPartDef)
                                {
                                    continue;
                                }

                                if (!HasHediffOnPart(wearer, entry.hediffDef, part))
                                {
                                    Hediff hd = HediffMaker.MakeHediff(entry.hediffDef, wearer, part);
                                    wearer.health.AddHediff(hd);
                                }
                            }
                        }
                    }
                    else
                    {
                        // 未配置部位时作为全身效果添加。
                        if (!HasHediffOnPart(wearer, entry.hediffDef, null))
                        {
                            Hediff hd = HediffMaker.MakeHediff(entry.hediffDef, wearer, null);
                            wearer.health.AddHediff(hd);
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(shackleProps.activateSoundDefName) && wearer.Spawned)
            {
                var soundDef = DefDatabase<SoundDef>.GetNamedSilentFail(shackleProps.activateSoundDefName);
                if (soundDef != null)
                    soundDef.PlayOneShot(wearer);
            }

            if (wearer.Map != null)
            {
                MoteMaker.ThrowText(wearer.DrawPos, wearer.Map, MugirlText.Resolve(shackleProps.moteTextOn), Color.cyan);
            }
            Messages.Message(MugirlText.Resolve(shackleProps.messageOn, wearer.LabelShortCap), wearer, MessageTypeDefOf.NegativeEvent);
        }

        // 解除时只移除本配置添加的束缚 Hediff。
        public void DeactivateShackles()
        {
            DeactivateShackles(Wearer, true);
        }

        private void DeactivateShackles(Pawn wearer, bool showFeedback)
        {
            if (!isActive) return;

            if (wearer == null)
            {
                isActive = false;
                return;
            }

            isActive = false;

            CompProperties_MagneticShackles shackleProps = Props;
            if (shackleProps == null || wearer.health?.hediffSet == null)
            {
                return;
            }

            if (shackleProps.bindHediffs == null)
            {
                return;
            }

            foreach (var entry in shackleProps.bindHediffs)
            {
                if (entry == null || entry.hediffDef == null) continue;

                List<Hediff> hediffs = wearer.health.hediffSet.hediffs;
                for (int i = hediffs.Count - 1; i >= 0; i--)
                {
                    Hediff hediff = hediffs[i];
                    if (ShouldRemoveBoundHediff(entry, hediff))
                    {
                        wearer.health.RemoveHediff(hediff);
                    }
                }
            }

            if (showFeedback)
            {
                if (wearer.Spawned && wearer.Map != null)
                {
                    MoteMaker.ThrowText(wearer.DrawPos, wearer.Map, MugirlText.Resolve(shackleProps.moteTextOff), Color.green);
                }
                Messages.Message(MugirlText.Resolve(shackleProps.messageOff, wearer.LabelShortCap), wearer, MessageTypeDefOf.PositiveEvent);
            }
        }

        // 束缚的身体部位组会禁止依赖对应部位的 Verb。
        public override bool CompAllowVerbCast(Verb verb)
        {
            CompProperties_MagneticShackles shackleProps = Props;
            if (!IsActive || shackleProps?.boundBodyPartGroupDefs == null || shackleProps.boundBodyPartGroupDefs.Count == 0)
                return true;

            if (verb?.tool == null)
                return true;

            return !shackleProps.boundBodyPartGroupDefs.Contains(verb.tool.linkedBodyPartsGroup);
        }

        private static bool HasHediffOnPart(Pawn pawn, HediffDef hediffDef, BodyPartRecord part)
        {
            if (pawn?.health?.hediffSet == null || hediffDef == null)
            {
                return false;
            }

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff.def == hediffDef && hediff.Part == part)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldRemoveBoundHediff(CompProperties_MagneticShackles.BindHediffEntry entry, Hediff hediff)
        {
            if (entry?.hediffDef == null || hediff?.def != entry.hediffDef)
            {
                return false;
            }

            if (entry.bodyPartDefs == null || entry.bodyPartDefs.Count == 0)
            {
                return hediff.Part == null;
            }

            if (hediff.Part == null)
            {
                return false;
            }

            for (int i = 0; i < entry.bodyPartDefs.Count; i++)
            {
                BodyPartDef bodyPartDef = entry.bodyPartDefs[i];
                if (bodyPartDef != null && hediff.Part.def == bodyPartDef)
                {
                    return true;
                }
            }

            return false;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref tickCounter, "tickCounter", 0);
            Scribe_Values.Look(ref lastManualUseTick, "lastManualUseTick", -999999);
            Scribe_Values.Look(ref isActive, "isActive", false);
            Scribe_Values.Look(ref currentCycleInterval, "currentCycleInterval", 0);
            Scribe_Values.Look(ref currentBindDurationTicks, "currentBindDurationTicks", 0);
            Scribe_Values.Look(ref nextStateTick, "nextStateTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureCycleTimingsInitialized();
                if (nextStateTick < -1)
                {
                    nextStateTick = -1;
                }
            }
        }

        private static int CurrentGameTickOrFallback(int fallback)
        {
            return MugirlTickUtility.CurrentGameTickOrFallback(fallback);
        }

    }
}
