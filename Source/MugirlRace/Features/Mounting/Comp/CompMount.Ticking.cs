namespace Mugirl.Features.Mounting;

// 骑乘期间的 tick：骑手放在本组件的 ThingOwner 中，不属于载具的 Pawn.GetChildHolders，原版不会推进它，所以由载具代为推进。
public sealed partial class CompMount
{
    // RiderTickDriver.Tick 遍历骑手子容器用的临时列表；每个载具一份，不进存档。
    private readonly List<IThingHolder> riderHolders = [];

    public override void CompTick()
    {
        base.CompTick();
        Pawn? rider = MountedPawn;
        CompProperties_Mount? mountProps = Props;
        if (rider is null || mountProps is null)
        {
            return;
        }

        // 骑乘远程 verb 必须逐 tick 推进（原版 Pawn.Tick 的频率）：在 CompTickInterval 中按 delta 连续推进时，
        // 镜头外的载具会在同一 tick 打出多发连射。推进骑手时它随骑手的装备推进，否则单独推进。
        if (!mountProps.tickPhysiology)
        {
            MountedCombatController.VerbTick(this);
            return;
        }

        RiderTickDriver.Tick(this, rider, riderHolders);
        if (rider.Dead)
        {
            TryDismount(sendMessage: false);
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
