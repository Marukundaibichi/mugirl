namespace Mugirl.Features.Corporate;

// 展示人员和待结算资产；持有引用，不改变 Pawn 的资产归属。
internal sealed class CorporatePeopleDesk : IExposable
{
    internal List<CorporatePersonOffer> offers = new List<CorporatePersonOffer>();
    internal List<Pawn> awaitingWorld = new List<Pawn>();
    internal int pendingSilver;
    internal int nextCleanupTick;

    public void ExposeData()
    {
        Scribe_Collections.Look(ref offers, "offers", LookMode.Deep);
        Scribe_Collections.Look(ref awaitingWorld, "awaitingWorld", LookMode.Reference);
        Scribe_Values.Look(ref pendingSilver, "pendingSilver", 0);
        if (Scribe.mode != LoadSaveMode.PostLoadInit)
        {
            return;
        }

        offers ??= new List<CorporatePersonOffer>();
        awaitingWorld ??= new List<Pawn>();
        // 只做数据修复：失效人员的已付款转为待领白银。改衣与重新定价需要完整初始化的 Pawn，在完整加载后执行。
        for (int i = offers.Count - 1; i >= 0; i--)
        {
            CorporatePersonOffer offer = offers[i];
            if (offer?.pawn == null || offer.pawn.Destroyed || offer.pawn.Dead)
            {
                if (offer != null && offer.paid && !offer.delivered)
                {
                    pendingSilver += offer.paidAmount;
                }

                offers.RemoveAt(i);
            }
        }

        awaitingWorld.RemoveAll(p => p == null || p.Destroyed);
        nextCleanupTick = 0;
    }
}
