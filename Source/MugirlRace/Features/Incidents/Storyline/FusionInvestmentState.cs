namespace Mugirl.Features.Incidents;

// 剧情事实与剩余倒计时分别保存，不推导或合并独立状态。
internal sealed class FusionInvestmentState : IExposable
{
    internal bool accepted;
    internal bool pending;
    internal int amount;
    internal int resultRemaining;
    internal int nextOfferTick;
    internal bool investorActive;
    internal Pawn? investor;

    public void ExposeData()
    {
        Scribe_Values.Look(ref accepted, "accepted", false);
        Scribe_Values.Look(ref pending, "pending", false);
        Scribe_Values.Look(ref amount, "amount", 0);
        Scribe_Values.Look(ref resultRemaining, "resultRemaining", 0);
        Scribe_Values.Look(ref nextOfferTick, "nextOfferTick", 0);
        Scribe_Values.Look(ref investorActive, "investorActive", false);
        Scribe_References.Look(ref investor, "investor");
    }
}
