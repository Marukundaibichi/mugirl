namespace Mugirl.Features.Corporate;

// 每周现货的一行：样品存放在 Vault 中，只用于展示与估价；成交时按样品规格重新生产货物。
public sealed class CorporateStockOffer : IExposable
{
    public int id;
    // 读档时样品引用可能失效（null）；使用处判空。
    public Thing? sample;
    public int count;
    public int unitPrice;

    public void ExposeData()
    {
        Scribe_Values.Look(ref id, "id", 0);
        Scribe_References.Look(ref sample, "sample");
        Scribe_Values.Look(ref count, "count", 0);
        Scribe_Values.Look(ref unitPrice, "unitPrice", 0);
    }
}
