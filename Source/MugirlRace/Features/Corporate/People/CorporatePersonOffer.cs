namespace Mugirl.Features.Corporate;

// 每周展示人员的一份报价；Pawn 由 Vault 持有，付款后保留签约时的价格与身份选择。
public sealed class CorporatePersonOffer : IExposable
{
    public int id;
    // 读档时引用失效的报价在 PostLoadInit 中移除；使用处仍判空。
    public Pawn? pawn;
    public int price;
    public bool paid;
    public bool delivered;
    public int paidAmount;
    public bool buyAsColonist;
    public bool transferApplied;
    internal ICorporateOfferContinuation? continuation;

    internal void Complete(CorporateNetwork network)
    {
        continuation?.Complete(network, this);
        continuation = null;
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref id, "id", 0);
        Scribe_References.Look(ref pawn, "pawn");
        Scribe_Values.Look(ref price, "price", 0);
        Scribe_Values.Look(ref paid, "paid", false);
        Scribe_Values.Look(ref delivered, "delivered", false);
        Scribe_Values.Look(ref paidAmount, "paidAmount", 0);
        Scribe_Values.Look(ref buyAsColonist, "buyAsColonist", false);
        Scribe_Values.Look(ref transferApplied, "transferApplied", false);
        Scribe_Deep.Look(ref continuation, "continuation");
    }
}
