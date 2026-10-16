namespace Mugirl.Features.Mounting;

// 一次瞄准与射击的持久状态；目标、已用/剩余时长和最近命中事实分别保存。
internal sealed class MountTurretState : IExposable
{
    internal int burstCooldownTicksLeft;
    internal LocalTargetInfo aimTarget = LocalTargetInfo.Invalid;
    internal int aimTicksLeft;
    internal int aimTicksTotal;
    internal int castStartTick = -1;
    internal LocalTargetInfo lastAttackedTarget = LocalTargetInfo.Invalid;
    internal int lastAttackTargetTick;

    internal void ClearAim()
    {
        aimTarget = LocalTargetInfo.Invalid;
        aimTicksLeft = 0;
        aimTicksTotal = 0;
        castStartTick = -1;
    }

    internal void AdvanceAim(int delta) => aimTicksLeft = Math.Max(0, aimTicksLeft - Math.Max(1, delta));
    internal void AdvanceCooldown(int delta) => burstCooldownTicksLeft = Math.Max(0, burstCooldownTicksLeft - delta);

    public void ExposeData()
    {
        Scribe_Values.Look(ref burstCooldownTicksLeft, "burstCooldownTicksLeft", 0);
        Scribe_TargetInfo.Look(ref aimTarget, "aimTarget");
        Scribe_Values.Look(ref aimTicksLeft, "aimTicksLeft", 0);
        Scribe_Values.Look(ref aimTicksTotal, "aimTicksTotal", 0);
        Scribe_Values.Look(ref castStartTick, "castStartTick", -1);
        Scribe_TargetInfo.Look(ref lastAttackedTarget, "lastAttackedTarget");
        Scribe_Values.Look(ref lastAttackTargetTick, "lastAttackTargetTick", 0);
    }
}
