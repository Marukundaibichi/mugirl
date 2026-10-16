namespace Mugirl.Features.Incidents;

// 坠舱、流浪者加入等信件共用的段落文本，写法沿用原版 QuestNode_Root_RefugeePodCrash.SendLetter_NewTemp。
internal static class JoinerLetterUtility
{
    /// <summary>未成年对象的年龄文本（原版以生物学整岁乘以每年 tick 数换算）。</summary>
    internal static string ChildAgeText(Pawn pawn)
    {
        return (pawn.ageTracker.AgeBiologicalYears * GenDate.TicksPerYear)
            .ToStringTicksToPeriod(true, false, true, true, false);
    }
}
