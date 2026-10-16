using Mugirl.Features.Corporate;

namespace Mugirl.SaveUpgrade;

// 保留原 LoadedGame 的每个报价先整装、再重算基础价的次序。
internal sealed class CorporateOfferPriceCompletion : ICorporateOfferContinuation
{
    public CorporateOfferPriceCompletion()
    {
    }

    public void Complete(CorporateNetwork network, CorporatePersonOffer offer)
    {
        if (offer.paid && offer.paidAmount < 0)
        {
            offer.paidAmount = offer.price;
        }

        offer.price = network.PeopleBasePrice(offer.pawn);
    }

    public void ExposeData()
    {
    }
}
