namespace Mugirl.Features.Restraints;

// 阻止直接脱下仍锁定的束具（Pawn_ApparelTracker.TryDrop 补丁与使用效果共用）。
internal static class RestraintDropGuard
{
    // 已死亡的佩戴者（剥尸体）不受限制。
    internal static bool ShouldBlockDirectDrop(Apparel? apparel)
    {
        return apparel is RestraintApparel slaveApparel
            && slaveApparel.IsEffectivelyLocked
            && slaveApparel.Wearer is { Dead: false };
    }

    internal static void RejectDropMessage()
    {
        Messages.Message("Mugirl.SlaveApparelCannotBeRemoved".Translate(), MessageTypeDefOf.RejectInput, historical: false);
    }
}
