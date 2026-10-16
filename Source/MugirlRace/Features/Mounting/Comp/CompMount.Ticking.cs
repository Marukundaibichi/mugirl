namespace Mugirl.Features.Mounting;

// 骑乘期间的 tick：骑手不在地图上，原版不会 tick ThingComp 容器中的 Pawn，所以由载具代为推进。
public sealed partial class CompMount
{
    public override void CompTick()
    {
        base.CompTick();
        // 原版每 tick 推进一次装备 verb（Pawn.Tick → VerbsTick）。骑乘远程 verb 也必须逐 tick 推进：
        // 在 CompTickInterval 中按 delta 连续推进时，镜头外的载具会在同一 tick 打出多发连射。
        if (mugirlMountRiders.Count > 0 && Props is not null)
        {
            MountedCombatController.VerbTick(this);
        }
    }

    public override void CompTickInterval(int delta)
    {
        base.CompTickInterval(delta);
        Pawn? rider = MountedPawn;
        CompProperties_Mount? mountProps = Props;
        if (rider is null || mountProps is null)
        {
            return;
        }

        Schedule.Add(delta);
        MountedCombatController.TickAim(this, delta);

        int physiologicalInterval = Math.Max(1, mountProps.physiologicalTickInterval);
        int safetyInterval = Math.Max(1, mountProps.safetyCheckInterval);
        int turretInterval = Math.Max(1, mountProps.turretTickInterval);

        if (mountProps.tickPhysiology && Schedule.ConsumePhysiology(physiologicalInterval, out int physiologicalDelta))
        {
            RiderTickDriver.TickInterval(rider, physiologicalDelta);
            if (rider.Dead)
            {
                TryDismount(sendMessage: false);
                return;
            }
        }

        if (mountProps.autoDismount
            && Schedule.ConsumeSafety(safetyInterval, out _)
            && MountEligibilityUtility.ShouldAutoDismount(rider, MooPawn, out string? reasonKey))
        {
            string reason = reasonKey.NullOrEmpty() ? "Mugirl.Mount.ReasonInvalid".Translate().ToString() : reasonKey.Translate().ToString();
            if (TryDismount(sendMessage: false))
            {
                Messages.Message("Mugirl.Mount.AutoDismountMessage".Translate(rider.LabelShort, reason), MooPawn, MessageTypeDefOf.NeutralEvent);
            }

            return;
        }

        if (Schedule.ConsumeTurret(turretInterval, out int turretDelta))
        {
            MountedCombatController.Tick(this, turretDelta);
        }

        if (Schedule.ConsumeMelee(turretInterval, out _))
        {
            MountedMeleeSupport.Tick(this);
        }
    }
}
