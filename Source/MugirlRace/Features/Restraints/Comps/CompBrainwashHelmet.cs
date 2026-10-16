namespace Mugirl.Features.Restraints;

// 脑控头盔运行逻辑：未破解时自动周期转换，破解后提供主动控制。
// 转换计时、手动冷却与当前周期分别保存，不重新推算已运行的进度。
public class CompBrainwashHelmet : CompAdvancedRestraint
{
    private int mugirlHelmetNonBlockedTicks;
    private int mugirlHelmetTicksToChange;
    private int mugirlHelmetLastManualUseTick = -999999;

    public CompProperties_BrainwashHelmet? Props => props as CompProperties_BrainwashHelmet;

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

    // 周期为 35000–60000 tick 的随机值，按间隔 tick 累计（1.6 每 tick 扫描一次 Hediff 列表）；
    // 触发时刻最多晚一个间隔（几 tick），封锁 Hediff 也按间隔检查。
    public override void CompTickInterval(int delta)
    {
        base.CompTickInterval(delta);

        Pawn? pawn = Wearer;
        CompProperties_BrainwashHelmet? helmetProps = Props;
        if (helmetProps == null || pawn?.health?.hediffSet == null || pawn.Dead || !pawn.Spawned || IsParentCracked)
        {
            return;
        }

        if (helmetProps.blockHediff != null && pawn.health.hediffSet.HasHediff(helmetProps.blockHediff))
        {
            mugirlHelmetNonBlockedTicks = 0;
            return;
        }

        if (mugirlHelmetTicksToChange <= 0)
        {
            return;
        }

        mugirlHelmetNonBlockedTicks += delta;
        if (mugirlHelmetNonBlockedTicks >= mugirlHelmetTicksToChange)
        {
            mugirlHelmetNonBlockedTicks = 0;
            mugirlHelmetTicksToChange = Math.Max(1, helmetProps.ticksToChange.RandomInRange);
            ExecuteHediffLogic(pawn, helmetProps);
        }
    }

    public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
    {
        foreach (Gizmo gizmo in base.CompGetWornGizmosExtra())
        {
            yield return gizmo;
        }

        // 主动按钮只在装备破解后显示；点击时仍会重新检查状态和冷却。
        CompProperties_BrainwashHelmet? helmetProps = Props;
        if (helmetProps == null || !IsParentCracked || Wearer == null)
        {
            yield break;
        }

        int cdLeft = ManualCooldownTicksLeft(helmetProps);
        float cooldownPercent = ManualCooldown.Percent(cdLeft, helmetProps.useCooldownTicks);
        Texture2D activateIcon = helmetProps.ActivateIcon;

        yield return MakeCooldownCommand(
            MugirlText.Resolve(helmetProps.activateLabel),
            MugirlText.Resolve(helmetProps.activateDesc),
            activateIcon,
            disabled: cdLeft > 0,
            () => CurrentCooldownPercent(cooldownPercent),
            TryManualTrigger);

        // Ideology 激活时额外提供文化转换按钮。
        if (ModsConfig.IdeologyActive)
        {
            yield return new Command_Action
            {
                defaultLabel = "Mugirl.Restraints.BrainwashHelmet.IdeoConvertLabel".Translate(),
                defaultDesc = "Mugirl.Restraints.BrainwashHelmet.IdeoConvertDesc".Translate(),
                icon = activateIcon,
                action = TryIdeoConvert,
            };
        }
    }

    public override void Notify_Unequipped(Pawn pawn)
    {
        base.Notify_Unequipped(pawn);

        HediffSet? hediffSet = pawn?.health?.hediffSet;
        CompProperties_BrainwashHelmet? helmetProps = Props;
        if (pawn == null || hediffSet == null || helmetProps == null)
        {
            return;
        }

        // 卸下时移除本头盔可能添加的所有目标 Hediff。
        if (helmetProps.convertList != null)
        {
            for (int e = 0; e < helmetProps.convertList.Count; e++)
            {
                HediffDef? hediffToAdd = helmetProps.convertList[e]?.hediffToAdd;
                if (hediffToAdd != null)
                {
                    RemoveAllOfDef(pawn, hediffSet, hediffToAdd);
                }
            }
        }

        if (helmetProps.defaultHediff != null)
        {
            RemoveAllOfDef(pawn, hediffSet, helmetProps.defaultHediff);
        }
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref mugirlHelmetNonBlockedTicks, "mugirlHelmetNonBlockedTicks", 0);
        Scribe_Values.Look(ref mugirlHelmetLastManualUseTick, "mugirlHelmetLastManualUseTick", -999999);
        Scribe_Values.Look(ref mugirlHelmetTicksToChange, "mugirlHelmetTicksToChange", 0);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            EnsureCurrentTicksToChangeInitialized();
        }
    }

    // 装备可能直接生成到穿戴栏，未必经过地图生成入口；只补非法/未初始化值，避免读档后覆盖保存的剩余周期。
    private void EnsureCurrentTicksToChangeInitialized()
    {
        CompProperties_BrainwashHelmet? helmetProps = Props;
        if (mugirlHelmetTicksToChange > 0 || helmetProps == null)
        {
            return;
        }

        mugirlHelmetTicksToChange = Math.Max(1, helmetProps.ticksToChange.RandomInRange);
    }

    private void TryManualTrigger()
    {
        CompProperties_BrainwashHelmet? helmetProps = Props;
        int currentTick = CurrentGameTickOrFallback(mugirlHelmetLastManualUseTick);
        if (helmetProps == null || !IsParentCracked
            || ManualCooldown.TicksLeft(mugirlHelmetLastManualUseTick, helmetProps.useCooldownTicks, currentTick) > 0)
        {
            return;
        }

        Pawn? wearer = Wearer;
        if (wearer?.health?.hediffSet == null)
        {
            return;
        }

        ExecuteHediffLogic(wearer, helmetProps);
        mugirlHelmetLastManualUseTick = currentTick;

        Messages.Message("Mugirl.Restraints.BrainwashHelmet.ManualTriggerMessage".Translate(wearer.LabelShortCap), MessageTypeDefOf.PositiveEvent);
    }

    private void TryIdeoConvert()
    {
        CompProperties_BrainwashHelmet? helmetProps = Props;
        Pawn? wearer = Wearer;
        if (helmetProps == null || !IsParentCracked || wearer == null)
        {
            return;
        }

        GameComponent_BrainwashPerformance.StartFor(wearer, GetBrainwashPerformanceHediffDefFor(wearer, helmetProps));
        PlayerIdeoUtility.AdoptPlayerPrimaryIdeo(wearer);
        Messages.Message("Mugirl.Restraints.BrainwashHelmet.IdeoConvertMessage".Translate(wearer.LabelShortCap), wearer, MessageTypeDefOf.PositiveEvent);
    }

    private int ManualCooldownTicksLeft(CompProperties_BrainwashHelmet helmetProps)
    {
        return ManualCooldown.TicksLeft(mugirlHelmetLastManualUseTick, helmetProps.useCooldownTicks, CurrentGameTickOrFallback(mugirlHelmetLastManualUseTick));
    }

    private float CurrentCooldownPercent(float fallback)
    {
        CompProperties_BrainwashHelmet? helmetProps = Props;
        if (helmetProps == null)
        {
            return UnityMathf.Clamp01(fallback);
        }

        return ManualCooldown.Percent(ManualCooldownTicksLeft(helmetProps), helmetProps.useCooldownTicks);
    }

    // 意识形态转换时播放的表演：按当前洗脑阶段取下一阶段的 Hediff，否则用默认 Hediff。
    private static HediffDef? GetBrainwashPerformanceHediffDefFor(Pawn pawn, CompProperties_BrainwashHelmet helmetProps)
    {
        HediffSet? hediffSet = pawn.health?.hediffSet;
        if (hediffSet != null && helmetProps.convertList != null)
        {
            for (int i = 0; i < helmetProps.convertList.Count; i++)
            {
                CompProperties_BrainwashHelmet.HediffConvertEntry? entry = helmetProps.convertList[i];
                if (entry?.hediffToCheck != null && entry.hediffToAdd != null && hediffSet.GetFirstHediffOfDef(entry.hediffToCheck) != null)
                {
                    return entry.hediffToAdd;
                }
            }
        }

        return helmetProps.defaultHediff;
    }

    // 核心转换逻辑：优先执行 convertList，未命中时添加默认 Hediff。洗脑类 Hediff 挂在大脑上，找不到大脑则不添加。
    private static void ExecuteHediffLogic(Pawn pawn, CompProperties_BrainwashHelmet helmetProps)
    {
        Pawn_HealthTracker? health = pawn.health;
        HediffSet? hediffSet = health?.hediffSet;
        if (health == null || hediffSet == null)
        {
            return;
        }

        if (helmetProps.convertList != null)
        {
            for (int i = 0; i < helmetProps.convertList.Count; i++)
            {
                CompProperties_BrainwashHelmet.HediffConvertEntry? entry = helmetProps.convertList[i];
                if (entry?.hediffToCheck == null || entry.hediffToAdd == null)
                {
                    continue;
                }

                Hediff? existing = hediffSet.GetFirstHediffOfDef(entry.hediffToCheck);
                if (existing == null)
                {
                    continue;
                }

                health.RemoveHediff(existing);
                BodyPartRecord? brain = hediffSet.GetBrain();
                if (brain != null && !hediffSet.HasHediff(entry.hediffToAdd))
                {
                    health.AddHediff(entry.hediffToAdd, brain);
                }

                return;
            }
        }

        if (helmetProps.defaultHediff != null && !hediffSet.HasHediff(helmetProps.defaultHediff))
        {
            BodyPartRecord? brain = hediffSet.GetBrain();
            if (brain != null)
            {
                health.AddHediff(helmetProps.defaultHediff, brain);
            }
        }
    }

    private static void RemoveAllOfDef(Pawn pawn, HediffSet hediffSet, HediffDef def)
    {
        List<Hediff> hediffs = hediffSet.hediffs;
        for (int i = hediffs.Count - 1; i >= 0; i--)
        {
            if (hediffs[i].def == def)
            {
                pawn.health.RemoveHediff(hediffs[i]);
            }
        }
    }
}
