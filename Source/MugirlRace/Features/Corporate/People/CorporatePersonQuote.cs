namespace Mugirl.Features.Corporate;

// 人员买卖报价明细；total 为玩家实际支付或收到的白银。
public struct CorporatePersonQuote
{
    public int basePrice;
    public int colonistPremium;
    public int handlingFee;
    public int shippingFee;
    public int total;
}
