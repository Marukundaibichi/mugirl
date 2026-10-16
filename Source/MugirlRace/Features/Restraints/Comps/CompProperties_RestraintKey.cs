namespace Mugirl.Features.Restraints;

// 钥匙不可堆叠：每把钥匙单独计数与消耗。
public class CompProperties_RestraintKey : CompProperties
{
    public CompProperties_RestraintKey()
    {
        compClass = typeof(CompRestraintKey);
    }
}
