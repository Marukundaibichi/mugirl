using System.Diagnostics.CodeAnalysis;
using Mugirl.Features.Roping;

namespace Mugirl.Features.Mounting;

// 上骑与下骑：把骑手移入/移出 ThingOwner，并同步炮塔状态、选择状态与提示消息。
public sealed partial class CompMount
{
    private const int DismountWaitTicks = 60;

    public bool CanMount([NotNullWhen(true)] Pawn? rider, [NotNullWhen(false)] out string? reasonKey)
    {
        return MountEligibilityUtility.CanMount(this, rider, out reasonKey);
    }

    public bool TryMount(Pawn? rider)
    {
        if (!CanMount(rider, out string? reasonKey))
        {
            Messages.Message(
                "Mugirl.Mount.LabelWithReason".Translate("Mugirl.Mount.CannotMount".Translate(), reasonKey.Translate()).CapitalizeFirst(),
                rider ?? MooPawn,
                MessageTypeDefOf.RejectInput,
                historical: false);
            return false;
        }

        // CanMount 已保证载具存在且已生成；这里只为 nullable 分析取出非空引用。
        if (MooPawn is not { } carrier)
        {
            return false;
        }

        bool wasSelected = SelectionUtility.IsSelectedInPlaying(rider);
        RiderTickDriver.PrepareForContainer(rider);
        RopingService.BreakAllRopesAndNotify(rider);
        RopingService.BreakAllRopesAndNotify(carrier);
        rider.DeSpawnOrDeselect();

        if (!mugirlMountRiders.TryAdd(rider))
        {
            GenSpawn.Spawn(rider, carrier.Position, carrier.Map);
            if (wasSelected)
            {
                SelectionUtility.SelectInPlaying(rider);
            }

            Messages.Message(
                "Mugirl.Mount.LabelWithReason".Translate("Mugirl.Mount.CannotMount".Translate(), "Mugirl.Mount.ReasonInvalid".Translate()).CapitalizeFirst(),
                carrier,
                MessageTypeDefOf.RejectInput,
                historical: false);
            return false;
        }

        RiderTickDriver.ClearHiddenJobs(rider);
        MountedCombatController.NotifyMounted(this);
        if (wasSelected)
        {
            SelectionUtility.SelectInPlaying(rider, playSound: false, forceDesignatorDeselect: false);
        }

        Messages.Message("Mugirl.Mount.MountedMessage".Translate(rider.LabelShort, carrier.LabelShort), carrier, MessageTypeDefOf.PositiveEvent);
        return true;
    }

    public bool TryDismount(IntVec3? preferredCell = null, bool sendMessage = true)
    {
        Pawn? rider = MountedPawn;
        Pawn? carrier = MooPawn;
        Map? map = carrier?.Map;
        if (rider is null || carrier is null || map is null)
        {
            return false;
        }

        if (!DismountCellFinder.TryFindCell(carrier, rider, preferredCell, out IntVec3 cell)
            || !TryDropRider(rider, carrier, cell, map, playDropSound: true))
        {
            if (sendMessage)
            {
                Messages.Message("Mugirl.Mount.NoDismountCell".Translate(carrier.LabelShort), carrier, MessageTypeDefOf.RejectInput, historical: false);
            }

            return false;
        }

        if (sendMessage)
        {
            Messages.Message("Mugirl.Mount.DismountedMessage".Translate(rider.LabelShort, carrier.LabelShort), carrier, MessageTypeDefOf.NeutralEvent);
        }

        return true;
    }

    public bool TryEmergencyDismountNear(IntVec3 cell, Map? map)
    {
        Pawn? rider = MountedPawn;
        if (rider is null || map is null)
        {
            return false;
        }

        Pawn? carrier = MooPawn;
        IntVec3 dropCell = cell;
        if (!DismountCellFinder.IsValidCell(dropCell, carrier, rider, map)
            && (carrier is null || !DismountCellFinder.TryFindCell(carrier, rider, null, out dropCell)))
        {
            return false;
        }

        return TryDropRider(rider, carrier, dropCell, map, playDropSound: false);
    }

    // 下骑的公共部分：放下骑手，重置炮塔状态，让骑手短暂停顿，并在原先选中时改选落地的骑手。
    private bool TryDropRider(Pawn rider, Pawn? carrier, IntVec3 cell, Map map, bool playDropSound)
    {
        bool wasSelected = SelectionUtility.IsSelectedInPlaying(rider);
        if (!mugirlMountRiders.TryDrop(
                rider,
                cell,
                map,
                ThingPlaceMode.Near,
                out Thing dropped,
                null,
                c => DismountCellFinder.IsValidCell(c, carrier, rider, map),
                playDropSound: playDropSound))
        {
            return false;
        }

        MountedCombatController.NotifyDismounting(this, rider);
        if (dropped is Pawn droppedPawn)
        {
            if (!droppedPawn.Dead && droppedPawn.jobs is not null)
            {
                PawnUtility.ForceWait(droppedPawn, DismountWaitTicks, carrier);
            }

            if (wasSelected)
            {
                SelectionUtility.SelectInPlaying(droppedPawn, playSound: false, forceDesignatorDeselect: false);
            }
        }

        return true;
    }
}
