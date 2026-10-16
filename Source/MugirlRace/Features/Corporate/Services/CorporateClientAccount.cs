using System.Linq;

namespace Mugirl.Features.Corporate;

// 成交额和客户服务权益；持久列表保持首次登记顺序。
internal sealed class CorporateClientAccount : IExposable
{
    internal long turnover;
    private List<int> creditedPawnIds = new List<int>();
    private HashSet<int> creditedPawnIndex = [];
    internal int giftWeek = -1;
    internal int freePersonWeek = -1;
    internal int supportWeek = -1;
    internal int supportCallsUsed;

    internal bool TryCreditPerson(int pawnId)
    {
        if (!creditedPawnIndex.Add(pawnId))
        {
            return false;
        }

        creditedPawnIds.Add(pawnId);
        return true;
    }

    internal void AddTurnover(long amount) => turnover += Math.Min(amount, long.MaxValue - turnover);

    public void ExposeData()
    {
        Scribe_Values.Look(ref turnover, "turnover", 0L);
        Scribe_Collections.Look(ref creditedPawnIds, "creditedPawnIds", LookMode.Value);
        Scribe_Values.Look(ref giftWeek, "giftWeek", -1);
        Scribe_Values.Look(ref freePersonWeek, "freePersonWeek", -1);
        Scribe_Values.Look(ref supportWeek, "supportWeek", -1);
        Scribe_Values.Look(ref supportCallsUsed, "supportCallsUsed", 0);
        if (Scribe.mode != LoadSaveMode.PostLoadInit)
        {
            return;
        }

        turnover = Math.Max(0L, turnover);
        creditedPawnIds = (creditedPawnIds ?? new List<int>()).Where(id => id > 0).Distinct().ToList();
        creditedPawnIndex = new HashSet<int>(creditedPawnIds);
    }
}
