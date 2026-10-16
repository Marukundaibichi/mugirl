using System.Diagnostics.CodeAnalysis;
using Verse.AI;

namespace Mugirl.Core;

// 跨功能共用的小人状态判断（类人、能力、清醒）。
internal static class PawnPredicates
{
    internal static bool IsHumanlike([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn?.RaceProps?.Humanlike == true;
    }

    internal static bool HasCapacity([NotNullWhen(true)] Pawn? pawn, PawnCapacityDef capacity)
    {
        return pawn?.health?.capacities?.CapableOf(capacity) == true;
    }

    internal static bool IsAwake([NotNullWhen(true)] Pawn? pawn)
    {
        if (pawn?.health?.capacities?.CanBeAwake != true)
        {
            return false;
        }

        Pawn_JobTracker? jobs = pawn.jobs;
        if (jobs?.curJob is not null && jobs.curDriver is not null)
        {
            return !jobs.curDriver.asleep;
        }

        return true;
    }
}
