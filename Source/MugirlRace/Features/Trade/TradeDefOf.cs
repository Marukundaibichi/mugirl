namespace Mugirl.Features.Trade;

// 奴隶交易的 Def。
[DefOf]
internal static class TradeDefOf
{
    public static PawnKindDef Mugirl_Slave = null!;

    static TradeDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(TradeDefOf));
}
