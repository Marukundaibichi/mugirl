namespace Mugirl.Features.Mounting;

// 载具的 Gizmo 与检查面板文本。
public sealed partial class CompMount
{
    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        foreach (Gizmo gizmo in base.CompGetGizmosExtra())
        {
            yield return gizmo;
        }

        if (MountedPawn is null)
        {
            yield break;
        }

        yield return new Command_Action
        {
            defaultLabel = "Mugirl.Mount.SelectRider".Translate(),
            defaultDesc = "Mugirl.Mount.SelectRiderDesc".Translate(),
            icon = TexCommand.SelectCarriedPawn,
            action = () => SelectionUtility.SelectInPlaying(MountedPawn),
        };

        yield return new Command_Action
        {
            defaultLabel = "Mugirl.Mount.DismountRider".Translate(),
            defaultDesc = "Mugirl.Mount.DismountRiderDesc".Translate(),
            icon = TexCommand.DropCarriedPawn,
            action = () => TryDismount(),
        };

        yield return new Command_Toggle
        {
            defaultLabel = mugirlMountFireAtWill ? "Mugirl.Mount.FireAtWill".Translate() : "Mugirl.Mount.HoldFire".Translate(),
            defaultDesc = mugirlMountFireAtWill ? "Mugirl.Mount.FireAtWillDesc".Translate() : "Mugirl.Mount.HoldFireDesc".Translate(),
            icon = mugirlMountFireAtWill ? TexCommand.FireAtWill : TexCommand.CannotShoot,
            isActive = () => mugirlMountFireAtWill,
            toggleAction = () => mugirlMountFireAtWill = !mugirlMountFireAtWill,
        };
    }

    public override string? CompInspectStringExtra()
    {
        Pawn? rider = MountedPawn;
        return rider is null ? null : "Mugirl.Mount.InspectRider".Translate(rider.LabelShort);
    }
}
