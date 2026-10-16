namespace Mugirl.Features.Corporate;

// 现货和订单的持久集合，货物所有权始终属于 Network 的 Vault。
internal sealed class CorporateTradeDesk : IExposable
{
    internal List<CorporateStockOffer> stock = new List<CorporateStockOffer>();
    internal List<CorporateOrder> orders = new List<CorporateOrder>();

    public void ExposeData()
    {
        Scribe_Collections.Look(ref stock, "stock", LookMode.Deep);
        Scribe_Collections.Look(ref orders, "orders", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            stock ??= new List<CorporateStockOffer>();
            orders ??= new List<CorporateOrder>();
        }
    }
}
