namespace Mugirl.Features.Restraints;

// 开发者模式下给穿着的束具显示“立即解锁”按钮。XML 基础束具 Def 引用此 comp。
public class CompProperties_DevUnlockRestraint : CompProperties
{
    public CompProperties_DevUnlockRestraint()
    {
        compClass = typeof(CompDevUnlockRestraint);
    }
}
