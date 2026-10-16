using Verse.Sound;

namespace Mugirl.Features.Restraints;

// 磁力镣铐运行逻辑；自动周期、当前绑定时长和下一次切换时刻分别保存。
public partial class CompMagneticShackles : CompAdvancedRestraint
{
    private int mugirlShacklesLastManualUseTick = -999999;
    private bool mugirlShacklesActive;

    private int mugirlShacklesCycleInterval;
    private int mugirlShacklesBindDurationTicks;

    // 自动循环下一次切换状态的游戏 tick；-1 表示尚未排程。
    private int mugirlShacklesNextStateTick = -1;

    public CompProperties_MagneticShackles? Props => props as CompProperties_MagneticShackles;

    public bool IsActive => mugirlShacklesActive;

    private int CurrentStateInterval
    {
        get
        {
            EnsureCycleTimingsInitialized();
            return mugirlShacklesActive ? mugirlShacklesBindDurationTicks : mugirlShacklesCycleInterval;
        }
    }

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
            mugirlShacklesNextStateTick = -1;
        }
    }

    public override void CompTick()
    {
        base.CompTick();
        CompProperties_MagneticShackles? shackleProps = Props;
        Pawn? pawn = Wearer;
        if (shackleProps == null || pawn == null || pawn.Dead || !pawn.Spawned)
        {
            return;
        }

        // 已破解时停止自动循环，改由手动按钮控制。
        if (IsParentCracked)
        {
            mugirlShacklesNextStateTick = -1;
            return;
        }

        EnsureCycleTimingsInitialized();
        int currentTick = CurrentGameTickOrFallback(mugirlShacklesNextStateTick);
        if (mugirlShacklesNextStateTick < 0)
        {
            mugirlShacklesNextStateTick = currentTick + CurrentStateInterval;
        }

        if (currentTick < mugirlShacklesNextStateTick)
        {
            return;
        }

        // 每次切换都重新随机下一段时长。
        if (mugirlShacklesActive)
        {
            DeactivateShackles();
            mugirlShacklesCycleInterval = Math.Max(1, shackleProps.cycleInterval.RandomInRange);
            mugirlShacklesNextStateTick = currentTick + mugirlShacklesCycleInterval;
        }
        else
        {
            ActivateShackles();
            mugirlShacklesBindDurationTicks = Math.Max(1, shackleProps.bindDurationTicks.RandomInRange);
            mugirlShacklesNextStateTick = currentTick + mugirlShacklesBindDurationTicks;
        }
    }

    public override void Notify_Equipped(Pawn pawn)
    {
        base.Notify_Equipped(pawn);
        mugirlShacklesLastManualUseTick = CurrentGameTickOrFallback(mugirlShacklesLastManualUseTick);
        ResetCycleTimings();
    }

    public override void Notify_Unequipped(Pawn pawn)
    {
        base.Notify_Unequipped(pawn);
        DeactivateShackles(pawn, false);
        mugirlShacklesNextStateTick = -1;
    }

    // 激活：按配置给佩戴者添加束缚 Hediff，播放音效并提示。
    public void ActivateShackles()
    {
        CompProperties_MagneticShackles? shackleProps = Props;
        Pawn? wearer = Wearer;
        if (mugirlShacklesActive || shackleProps == null || wearer?.health?.hediffSet == null)
        {
            return;
        }

        mugirlShacklesActive = true;
        mugirlShacklesLastManualUseTick = CurrentGameTickOrFallback(mugirlShacklesLastManualUseTick);

        if (shackleProps.bindHediffs != null)
        {
            for (int i = 0; i < shackleProps.bindHediffs.Count; i++)
            {
                CompProperties_MagneticShackles.BindHediffEntry? entry = shackleProps.bindHediffs[i];
                if (entry?.hediffDef != null)
                {
                    AddBindHediff(wearer, entry.hediffDef, entry.bodyPartDefs);
                }
            }
        }

        if (wearer.Spawned)
        {
            shackleProps.ActivateSound?.PlayOneShot(wearer);
        }

        if (wearer.Map != null)
        {
            MoteMaker.ThrowText(wearer.DrawPos, wearer.Map, MugirlText.Resolve(shackleProps.moteTextOn), UnityColor.Cyan);
        }

        Messages.Message(MugirlText.Resolve(shackleProps.messageOn, wearer.LabelShortCap), wearer, MessageTypeDefOf.NegativeEvent);
    }

    // 解除时只移除本配置添加的束缚 Hediff。
    public void DeactivateShackles()
    {
        DeactivateShackles(Wearer, true);
    }

    // 束缚的身体部位组会禁止依赖对应部位的 Verb（RestraintApparel.AllowVerbCast 调用基类后才会走到这里）。
    public override bool CompAllowVerbCast(Verb verb)
    {
        List<BodyPartGroupDef>? boundGroups = Props?.boundBodyPartGroupDefs;
        if (!mugirlShacklesActive || boundGroups == null || boundGroups.Count == 0 || verb?.tool == null)
        {
            return true;
        }

        return !boundGroups.Contains(verb.tool.linkedBodyPartsGroup);
    }

    public override void PostExposeData()
    {
        base.PostExposeData();

        Scribe_Values.Look(ref mugirlShacklesLastManualUseTick, "mugirlShacklesLastManualUseTick", -999999);
        Scribe_Values.Look(ref mugirlShacklesActive, "mugirlShacklesActive", false);
        Scribe_Values.Look(ref mugirlShacklesCycleInterval, "mugirlShacklesCycleInterval", 0);
        Scribe_Values.Look(ref mugirlShacklesBindDurationTicks, "mugirlShacklesBindDurationTicks", 0);
        Scribe_Values.Look(ref mugirlShacklesNextStateTick, "mugirlShacklesNextStateTick", -1, forceSave: true);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            EnsureCycleTimingsInitialized();
            if (mugirlShacklesNextStateTick < -1)
            {
                mugirlShacklesNextStateTick = -1;
            }
        }
    }

    private void EnsureCycleTimingsInitialized()
    {
        CompProperties_MagneticShackles? shackleProps = Props;
        if (shackleProps == null)
        {
            return;
        }

        if (mugirlShacklesCycleInterval <= 0)
        {
            mugirlShacklesCycleInterval = Math.Max(1, shackleProps.cycleInterval.RandomInRange);
        }

        if (mugirlShacklesBindDurationTicks <= 0)
        {
            mugirlShacklesBindDurationTicks = Math.Max(1, shackleProps.bindDurationTicks.RandomInRange);
        }
    }

    private void ResetCycleTimings()
    {
        CompProperties_MagneticShackles? shackleProps = Props;
        if (shackleProps == null)
        {
            mugirlShacklesCycleInterval = 0;
            mugirlShacklesBindDurationTicks = 0;
            mugirlShacklesNextStateTick = -1;
            return;
        }

        mugirlShacklesCycleInterval = Math.Max(1, shackleProps.cycleInterval.RandomInRange);
        mugirlShacklesBindDurationTicks = Math.Max(1, shackleProps.bindDurationTicks.RandomInRange);
        mugirlShacklesNextStateTick = -1;
    }

    // 配置了 BodyPartDef 时只给对应部位添加，未配置时作为全身效果；同部位已有时不重复添加。
    private static void AddBindHediff(Pawn wearer, HediffDef hediffDef, List<BodyPartDef>? bodyPartDefs)
    {
        List<BodyPartRecord>? parts = wearer.RaceProps?.body?.AllParts;
        if (bodyPartDefs != null && bodyPartDefs.Count > 0 && parts != null)
        {
            for (int d = 0; d < bodyPartDefs.Count; d++)
            {
                BodyPartDef? bodyPartDef = bodyPartDefs[d];
                if (bodyPartDef == null)
                {
                    continue;
                }

                for (int i = 0; i < parts.Count; i++)
                {
                    BodyPartRecord part = parts[i];
                    if (part.def == bodyPartDef && !HasHediffOnPart(wearer, hediffDef, part))
                    {
                        wearer.health.AddHediff(HediffMaker.MakeHediff(hediffDef, wearer, part));
                    }
                }
            }
        }
        else if (!HasHediffOnPart(wearer, hediffDef, null))
        {
            wearer.health.AddHediff(HediffMaker.MakeHediff(hediffDef, wearer, null));
        }
    }

    private void DeactivateShackles(Pawn? wearer, bool showFeedback)
    {
        if (!mugirlShacklesActive)
        {
            return;
        }

        mugirlShacklesActive = false;
        if (wearer == null)
        {
            return;
        }

        CompProperties_MagneticShackles? shackleProps = Props;
        if (shackleProps?.bindHediffs == null || wearer.health?.hediffSet == null)
        {
            return;
        }

        List<Hediff> hediffs = wearer.health.hediffSet.hediffs;
        for (int e = 0; e < shackleProps.bindHediffs.Count; e++)
        {
            CompProperties_MagneticShackles.BindHediffEntry? entry = shackleProps.bindHediffs[e];
            if (entry?.hediffDef == null)
            {
                continue;
            }

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
                MoteMaker.ThrowText(wearer.DrawPos, wearer.Map, MugirlText.Resolve(shackleProps.moteTextOff), UnityColor.Green);
            }

            Messages.Message(MugirlText.Resolve(shackleProps.messageOff, wearer.LabelShortCap), wearer, MessageTypeDefOf.PositiveEvent);
        }
    }

    private static bool HasHediffOnPart(Pawn pawn, HediffDef hediffDef, BodyPartRecord? part)
    {
        List<Hediff>? hediffs = pawn.health?.hediffSet?.hediffs;
        if (hediffs == null)
        {
            return false;
        }

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
        if (entry.hediffDef == null || hediff.def != entry.hediffDef)
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
            BodyPartDef? bodyPartDef = entry.bodyPartDefs[i];
            if (bodyPartDef != null && hediff.Part.def == bodyPartDef)
            {
                return true;
            }
        }

        return false;
    }
}
