namespace Mugirl.Features.Corporate;

// 人员业务参数，XML 见 1.7/Defs/Misc/CorporatePeople.xml。
public sealed class CorporatePeopleConfigDef : Def
{
    public int weeklyCount = 4;
    public float basePriceMultiplier = 1.5f;
    public float colonistPriceMultiplier = 1.5f;
    public float handlingFeeFactor = 0.05f;
    public float shippingFeeFactor = 0.05f;
}
