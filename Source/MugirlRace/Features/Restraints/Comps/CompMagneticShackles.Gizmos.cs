namespace Mugirl.Features.Restraints;

// 磁力镣铐破解后的手动按钮：未激活时显示“激活”，激活时显示“解除”，共用冷却。
public partial class CompMagneticShackles
{
    public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
    {
        foreach (Gizmo gizmo in base.CompGetWornGizmosExtra())
        {
            yield return gizmo;
        }

        CompProperties_MagneticShackles? shackleProps = Props;
        if (shackleProps == null || !IsParentCracked || Wearer == null)
        {
            yield break;
        }

        int cdLeft = ManualCooldownTicksLeft(shackleProps);
        bool canUse = cdLeft <= 0;
        float cooldownPercent = ManualCooldown.Percent(cdLeft, shackleProps.useCooldownTicks);

        if (!mugirlShacklesActive)
        {
            yield return MakeCooldownCommand(
                MugirlText.Resolve(shackleProps.activateLabel),
                canUse ? MugirlText.Resolve(shackleProps.activateDesc) : MugirlText.Resolve("Mugirl.Restraints.MagneticShackles.CooldownTicksLeft", cdLeft),
                shackleProps.ActivateIcon,
                disabled: !canUse,
                () => CurrentCooldownPercent(cooldownPercent),
                () => TryManualToggle(activate: true));
        }
        else
        {
            yield return MakeCooldownCommand(
                MugirlText.Resolve(shackleProps.deactivateLabel),
                MugirlText.Resolve(shackleProps.deactivateDesc),
                shackleProps.DeactivateIcon,
                disabled: !canUse,
                () => CurrentCooldownPercent(cooldownPercent),
                () => TryManualToggle(activate: false));
        }
    }

    // 点击时重新检查破解状态、佩戴者与冷却。
    private void TryManualToggle(bool activate)
    {
        CompProperties_MagneticShackles? shackleProps = Props;
        int currentTick = CurrentGameTickOrFallback(mugirlShacklesLastManualUseTick);
        if (shackleProps == null || !IsParentCracked || Wearer == null
            || ManualCooldown.TicksLeft(mugirlShacklesLastManualUseTick, shackleProps.useCooldownTicks, currentTick) > 0)
        {
            return;
        }

        if (activate)
        {
            ActivateShackles();
        }
        else
        {
            DeactivateShackles();
        }

        mugirlShacklesLastManualUseTick = currentTick;
    }

    private int ManualCooldownTicksLeft(CompProperties_MagneticShackles shackleProps)
    {
        return ManualCooldown.TicksLeft(mugirlShacklesLastManualUseTick, shackleProps.useCooldownTicks, CurrentGameTickOrFallback(mugirlShacklesLastManualUseTick));
    }

    private float CurrentCooldownPercent(float fallback)
    {
        CompProperties_MagneticShackles? shackleProps = Props;
        if (shackleProps == null)
        {
            return UnityMathf.Clamp01(fallback);
        }

        return ManualCooldown.Percent(ManualCooldownTicksLeft(shackleProps), shackleProps.useCooldownTicks);
    }
}
