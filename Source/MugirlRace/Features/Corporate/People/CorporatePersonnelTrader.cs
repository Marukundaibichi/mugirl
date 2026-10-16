using System.Linq;

namespace Mugirl.Features.Corporate;

// 只供原版 PreTraded 钩子与故事记录使用；转移本身由终端交易完成并校验。
internal sealed class CorporatePersonnelTrader(CorporateNetwork network) : ITrader
{
    public TraderKindDef TraderKind => CorporateDefOf.Mugirl_CorporatePersonnelTrader;
    public IEnumerable<Thing> Goods => network.Vault;
    public int RandomPriceFactorSeed => 0;
    public string TraderName => "Mugirl.CorporatePeople.TraderName".Translate();
    public bool CanTradeNow => true;
    public float TradePriceImprovementOffsetForPlayer => 0f;
    public Faction? Faction => network.CorporateFaction;
    public TradeCurrency TradeCurrency => TradeCurrency.Silver;

    public IEnumerable<Thing> ColonyThingsWillingToBuy(Pawn playerNegotiator) => Enumerable.Empty<Thing>();

    public void GiveSoldThingToTrader(Thing toGive, int countToGive, Pawn playerNegotiator)
    {
        throw new InvalidOperationException("Corporate personnel transfers must use the validated terminal transaction.");
    }

    public void GiveSoldThingToPlayer(Thing toGive, int countToGive, Pawn playerNegotiator)
    {
        throw new InvalidOperationException("Corporate personnel transfers must use the validated terminal transaction.");
    }
}
