namespace Mugirl.Features.Corporate;

// 报价可能有必须等 Pawn 完整初始化后才能完成的持久动作。
internal interface ICorporateOfferContinuation : IExposable
{
    void Complete(CorporateNetwork network, CorporatePersonOffer offer);
}
