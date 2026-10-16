using Verse.AI;

namespace Mugirl.Features.Restraints;

// 判断穿上新服装是否会替换掉仍锁定的束具（穿戴菜单、穿戴 Job 与原版 WouldReplaceLockedApparel 补丁共用）。
internal static class RestraintWearGuard
{
    internal static Apparel? JobTargetApparel(Job? job)
    {
        if (job == null)
        {
            return null;
        }

        LocalTargetInfo target = job.GetTarget(TargetIndex.A);
        return target.HasThing ? target.Thing as Apparel : null;
    }

    internal static bool WouldReplaceLockedSlaveApparel(Pawn? pawn, Apparel? newApparel)
    {
        BodyDef? body = pawn?.RaceProps?.body;
        List<Apparel>? wornApparel = pawn?.apparel?.WornApparel;
        if (wornApparel == null || newApparel?.def == null || body == null)
        {
            return false;
        }

        for (int i = 0; i < wornApparel.Count; i++)
        {
            Apparel worn = wornApparel[i];
            if (worn == null || worn == newApparel)
            {
                continue;
            }

            if (worn is RestraintApparel { IsEffectivelyLocked: true }
                && !ApparelUtility.CanWearTogether(newApparel.def, worn.def, body))
            {
                return true;
            }
        }

        return false;
    }
}
