namespace Mugirl.Features.Restraints;

// 束具状态查询（纯读取，无副作用）；钥匙开锁的菜单与 Job 见 Interaction/KeyUnlockFloatMenu。
public static class RestraintApparelUtility
{
    public static bool IsRestraint(this Apparel? apparel)
    {
        return apparel is RestraintApparel;
    }

    public static bool IsLockedRestraint(this Apparel? apparel)
    {
        return apparel is RestraintApparel { IsEffectivelyLocked: true };
    }

    public static bool IsWornLockedRestraint(this Pawn? pawn, Apparel? apparel)
    {
        return pawn?.apparel != null
            && apparel != null
            && pawn.apparel.WornApparel.Contains(apparel)
            && apparel.IsLockedRestraint();
    }

    public static bool SatisfiesKey(this Apparel? apparel, Thing? key)
    {
        return key != null && apparel is RestraintApparel slaveApparel && slaveApparel.Restraint is { } configuration
            && (configuration.key == null || configuration.key == key.def);
    }

    public static bool IsAdvancedRestraint(this Apparel? apparel)
    {
        return apparel is AdvancedRestraintApparel;
    }

    // 高级束具已破解时才为 true。
    public static bool IsCrackedAdvancedRestraint(this Apparel? apparel)
    {
        return apparel is AdvancedRestraintApparel advanced && advanced.IsCracked();
    }

    public static bool IsWearingRestraint(this Pawn? pawn)
    {
        List<Apparel>? wornApparel = pawn?.apparel?.WornApparel;
        if (wornApparel == null)
        {
            return false;
        }

        for (int i = 0; i < wornApparel.Count; i++)
        {
            if (wornApparel[i] is RestraintApparel)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsWearingUncrackedBrainwashApparel(this Pawn? pawn)
    {
        return IsWearingBrainwashApparel(pawn, cracked: false);
    }

    public static bool IsWearingCrackedBrainwashApparel(this Pawn? pawn)
    {
        return IsWearingBrainwashApparel(pawn, cracked: true);
    }

    public static bool IsHandsBlocked(this Pawn? pawn)
    {
        List<Apparel>? wornApparel = pawn?.apparel?.WornApparel;
        if (wornApparel == null)
        {
            return false;
        }

        for (int i = 0; i < wornApparel.Count; i++)
        {
            if (wornApparel[i] is RestraintApparel { Restraint.blocksHands: true })
            {
                return true;
            }
        }

        return false;
    }

    // 生成、读档后补齐原版穿戴锁：仍锁定的束具必须在原版 lockedApparel 列表中。
    public static void EnsureWornRestraintLocks(this Pawn? pawn)
    {
        Pawn_ApparelTracker? apparelTracker = pawn?.apparel;
        List<Apparel>? wornApparel = apparelTracker?.WornApparel;
        if (apparelTracker == null || wornApparel == null)
        {
            return;
        }

        for (int i = 0; i < wornApparel.Count; i++)
        {
            Apparel apparel = wornApparel[i];
            if (apparel.IsLockedRestraint() && !apparelTracker.IsLocked(apparel))
            {
                apparelTracker.Lock(apparel);
            }
        }
    }

    private static bool IsWearingBrainwashApparel(Pawn? pawn, bool cracked)
    {
        List<Apparel>? wornApparel = pawn?.apparel?.WornApparel;
        if (wornApparel == null)
        {
            return false;
        }

        for (int i = 0; i < wornApparel.Count; i++)
        {
            if (wornApparel[i] is BrainwashHelmetApparel brainwash && brainwash.IsCracked() == cracked)
            {
                return true;
            }
        }

        return false;
    }
}
