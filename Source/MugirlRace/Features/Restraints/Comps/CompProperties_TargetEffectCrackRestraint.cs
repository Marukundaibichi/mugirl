namespace Mugirl.Features.Restraints;

// 解码器对地面上的高级束具执行破解（由 JobDriver_CrackRestraint 调用）。
public class CompProperties_TargetEffectCrackRestraint : CompProperties
{
    public CompProperties_TargetEffectCrackRestraint()
    {
        compClass = typeof(CompTargetEffect_CrackRestraint);
    }
}
