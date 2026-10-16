namespace Mugirl.Features.Restraints;

// 束具的使用效果：让目标穿上这件束具（不替换已锁定的束具）。所有束具 Def 都通过 XML 挂载此 comp，
// 但只有 JobDriver_UseItemOn（JobDef GiveSlaveApparelGear，目前无人下达）会触发 DoEffect。
public class CompGetSlaveApparelGear : CompUseEffect
{
    public override float OrderPriority => -79;

    public override void DoEffect(Pawn usedBy)
    {
        base.DoEffect(usedBy);
        if (usedBy?.apparel == null || parent is not Apparel apparel)
        {
            return;
        }

        if (RestraintWearGuard.WouldReplaceLockedSlaveApparel(usedBy, apparel))
        {
            RestraintDropGuard.RejectDropMessage();
            return;
        }

        usedBy.apparel.Wear(apparel);
    }
}
