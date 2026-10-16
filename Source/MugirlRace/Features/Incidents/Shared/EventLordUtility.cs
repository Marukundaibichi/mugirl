using Verse.AI;
using Verse.AI.Group;

namespace Mugirl.Features.Incidents;

internal static class EventLordUtility
{
    // 运货员与投资人谈完后都以慢跑离图，遇袭可以自卫；先退出原 Lord，避免两个 Lord 同时持有该 Pawn。
    internal static void ExitMapJogging(Pawn? pawn)
    {
        if (pawn == null || !pawn.Spawned || pawn.Dead)
        {
            return;
        }

        pawn.GetLord()?.RemovePawn(pawn);
        LordMaker.MakeNewLord(
            pawn.Faction,
            new LordJob_ExitMapBest(LocomotionUrgency.Jog, canDig: false, canDefendSelf: true),
            pawn.Map,
            Gen.YieldSingle(pawn));
    }
}
