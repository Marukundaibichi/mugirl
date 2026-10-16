namespace Mugirl.Features.Corporate;

// 一次 TradeDeal 中与巨企成交的金额：人员按实际 Pawn 记账以便去重，其余货物合计。
internal sealed class CorporateTurnoverCapture
{
    internal double otherAmount;
    internal readonly List<CorporatePersonTradeAmount> personAmounts = new List<CorporatePersonTradeAmount>();
}
