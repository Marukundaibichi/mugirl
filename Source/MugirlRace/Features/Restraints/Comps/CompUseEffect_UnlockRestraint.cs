namespace Mugirl.Features.Restraints;

// 钥匙的使用效果（对自己使用）：给第一件匹配钥匙的锁定束具开锁一次，成功后消耗钥匙。
public class CompUseEffect_UnlockRestraint : CompUseEffect
{
    public override float OrderPriority => -69;

    public override void DoEffect(Pawn usedBy)
    {
        if (usedBy == null || usedBy.MapHeld == null || usedBy.apparel == null)
        {
            return;
        }

        base.DoEffect(usedBy);

        RestraintApparel? lockedApparel = FindLockedApparelForKey(usedBy);
        if (lockedApparel == null)
        {
            return;
        }

        // 脱下失败会回滚锁定状态，钥匙保留。
        if (lockedApparel.UseKey(usedBy, usedBy.PositionHeld) != KeyUnlockResult.DropFailed && !parent.Destroyed)
        {
            parent.Destroy();
        }
    }

    private RestraintApparel? FindLockedApparelForKey(Pawn pawn)
    {
        List<Apparel> wornApparel = pawn.apparel.WornApparel;
        for (int i = 0; i < wornApparel.Count; i++)
        {
            if (wornApparel[i] is RestraintApparel apparel && pawn.IsWornLockedRestraint(apparel) && apparel.SatisfiesKey(parent))
            {
                return apparel;
            }
        }

        return null;
    }
}
