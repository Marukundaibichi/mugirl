namespace Mugirl.Features.Restraints;

// 电击项圈运行逻辑：未破解时自动随机电击，破解后显示手动控制按钮。
// 检查倒计时与手动冷却独立保存，自动随机检查的次序由 CompTick 保持。
public class CompShockCollar : CompAdvancedRestraint
{
    private int mugirlShockCheckTicks;
    private int mugirlShockLastManualUseTick = -99999;

    public CompProperties_ShockCollar? Props => props as CompProperties_ShockCollar;

    public override void CompTick()
    {
        base.CompTick();

        // 热路径只递减计时器，完整检查延迟到间隔触发时执行。
        mugirlShockCheckTicks--;
        if (mugirlShockCheckTicks > 0)
        {
            return;
        }

        CompProperties_ShockCollar? shockProps = Props;
        if (shockProps == null)
        {
            return;
        }

        mugirlShockCheckTicks = Math.Max(1, shockProps.ticks);

        Pawn? wearer = Wearer;
        if (wearer == null || wearer.Dead || !wearer.Spawned || !CanBeShocked(wearer, shockProps))
        {
            return;
        }

        // 只有未破解时自动随机电击。Rand.Chance 本身把 <=0 视为不触发、>=1 视为必定触发，因此无需先钳制概率。
        if (IsParentCracked)
        {
            return;
        }

        if (Rand.Chance(shockProps.rand / 100f))
        {
            if (Rand.Chance(shockProps.powerShockChance / 100f))
            {
                ApplyHediffs(wearer, PowerHediffsFor(wearer, shockProps));
            }
            else
            {
                ApplyHediffs(wearer, shockProps.hediffDefs);
            }
        }
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref mugirlShockCheckTicks, "mugirlShockCheckTicks", 0);
        Scribe_Values.Look(ref mugirlShockLastManualUseTick, "mugirlShockLastManualUseTick", -99999);
    }

    public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
    {
        foreach (Gizmo gizmo in base.CompGetWornGizmosExtra())
        {
            yield return gizmo;
        }

        Pawn? wearer = Wearer;
        CompProperties_ShockCollar? shockProps = Props;

        // 手动控制只在已破解状态显示，自动电击与手动控制互斥。
        if (wearer == null || shockProps == null || !CanBeShocked(wearer, shockProps) || !IsParentCracked)
        {
            yield break;
        }

        int cdLeft = ManualCooldown.TicksLeft(mugirlShockLastManualUseTick, shockProps.useCooldownTicks, CurrentGameTickOrFallback(mugirlShockLastManualUseTick));
        bool canUse = cdLeft <= 0;
        float cooldownPercent = ManualCooldown.Percent(cdLeft, shockProps.useCooldownTicks);

        // 描述文本在生成 Gizmo 时计算一次；点击时仍会重新校验冷却和佩戴者。
        string powerDesc = canUse
            ? MugirlText.Resolve(shockProps.powerDesc)
            : MugirlText.Resolve("Mugirl.Restraints.ShockCollar.PowerCooldownTicksLeft", cdLeft);

        string commonDesc = canUse
            ? MugirlText.Resolve(shockProps.commonDesc)
            : MugirlText.Resolve("Mugirl.Restraints.ShockCollar.CommonCooldownTicksLeft", cdLeft);

        yield return MakeCooldownCommand(
            MugirlText.Resolve(shockProps.powerLabel),
            powerDesc,
            shockProps.PowerIcon,
            disabled: !canUse,
            () => CurrentCooldownPercent(cooldownPercent),
            () => TryManualShock(power: true));

        yield return MakeCooldownCommand(
            MugirlText.Resolve(shockProps.commonLabel),
            commonDesc,
            shockProps.CommonIcon,
            disabled: !canUse,
            () => CurrentCooldownPercent(cooldownPercent),
            () => TryManualShock(power: false));
    }

    // 默认只影响玩家可控单位，避免访客、盟友或敌对单位被装备 mod 意外波及。
    private static bool CanBeShocked(Pawn pawn, CompProperties_ShockCollar shockProps)
    {
        return !shockProps.playerColonistOnly || pawn.IsColonistPlayerControlled || pawn.IsPrisonerOfColony;
    }

    // 点击执行时重新检查佩戴者、破解状态和冷却，避免旧 Gizmo 操作过期对象。
    private void TryManualShock(bool power)
    {
        Pawn? wearer = Wearer;
        CompProperties_ShockCollar? shockProps = Props;
        if (wearer == null || shockProps == null || !CanBeShocked(wearer, shockProps) || !IsParentCracked)
        {
            return;
        }

        int currentTick = CurrentGameTickOrFallback(mugirlShockLastManualUseTick);
        if (ManualCooldown.TicksLeft(mugirlShockLastManualUseTick, shockProps.useCooldownTicks, currentTick) > 0)
        {
            return;
        }

        ApplyHediffs(wearer, power ? PowerHediffsFor(wearer, shockProps) : shockProps.hediffDefs);
        mugirlShockLastManualUseTick = currentTick;
    }

    private float CurrentCooldownPercent(float fallback)
    {
        CompProperties_ShockCollar? shockProps = Props;
        if (shockProps == null)
        {
            return UnityMathf.Clamp01(fallback);
        }

        int cdLeft = ManualCooldown.TicksLeft(mugirlShockLastManualUseTick, shockProps.useCooldownTicks, CurrentGameTickOrFallback(mugirlShockLastManualUseTick));
        return ManualCooldown.Percent(cdLeft, shockProps.useCooldownTicks);
    }

    private static void ApplyHediffs(Pawn pawn, List<HediffDef>? hediffs)
    {
        Pawn_HealthTracker? health = pawn.health;
        if (health?.hediffSet == null || hediffs == null)
        {
            return;
        }

        for (int i = 0; i < hediffs.Count; i++)
        {
            HediffDef? hediffDef = hediffs[i];
            if (hediffDef != null && !health.hediffSet.HasHediff(hediffDef))
            {
                health.AddHediff(hediffDef);
            }
        }
    }

    // 非雪牛娘佩戴者使用不含产奶效果的强力电击（配置为空时退回普通强力电击）。
    private static List<HediffDef>? PowerHediffsFor(Pawn pawn, CompProperties_ShockCollar shockProps)
    {
        if (!MugirlIdentity.IsMugirlPawn(pawn) && shockProps.nonMugirlPowerHediffDefs is { Count: > 0 } nonMugirl)
        {
            return nonMugirl;
        }

        return shockProps.powerhediffDefs;
    }
}
