namespace Mugirl.Features.Corporate;

// 订购、即时购买、赠礼与回收的交付收据；货物在交付前一直由 Vault 持有。
public sealed class CorporateOrder : IExposable
{
    public int id;
    // 卸载提供该 Def 的模组后为 null，订单转为待退款。
    public ThingDef? def;
    public string? savedLabel;
    public int quantity;
    public int deliveredQuantity;
    public int creditedTurnover;
    public int paid;
    public int createdTick;
    public int readyTick;
    public CorporateOrderSpeed speed;
    public int expediteFee;
    public CorporateOrderState state;
    // 读档时失效的货物引用保留为 null，由 TradeTick 转为待退款。
    public List<Thing?> goods = new List<Thing?>();

    public void ExposeData()
    {
        Scribe_Values.Look(ref id, "id", 0);
        Scribe_Defs.Look(ref def, "def");
        Scribe_Values.Look(ref savedLabel, "savedLabel", null);
        Scribe_Values.Look(ref quantity, "quantity", 0);
        Scribe_Values.Look(ref deliveredQuantity, "deliveredQuantity", 0);
        Scribe_Values.Look(ref creditedTurnover, "creditedTurnover", 0);
        Scribe_Values.Look(ref paid, "paid", 0);
        Scribe_Values.Look(ref createdTick, "createdTick", 0);
        Scribe_Values.Look(ref readyTick, "readyTick", 0);
        Scribe_Values.Look(ref speed, "speed", CorporateOrderSpeed.Standard);
        Scribe_Values.Look(ref expediteFee, "expediteFee", 0);
        Scribe_Values.Look(ref state, "state", CorporateOrderState.Preparing);
        Scribe_Collections.Look(ref goods, "goods", LookMode.Reference);
        if (Scribe.mode != LoadSaveMode.PostLoadInit)
        {
            return;
        }

        goods ??= new List<Thing?>();

    }

    /// <summary>货物中有失效或已销毁的条目。</summary>
    internal bool HasLostGoods
    {
        get
        {
            foreach (Thing? thing in goods)
            {
                if (thing == null || thing.Destroyed)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
