using RimWorld.Planet;

namespace Mugirl.Core;

internal static class GeneratedPawnUtility
{

    public static bool TryPassToWorld(Pawn? pawn, bool keepFactionless = false)
    {
        if (pawn == null || pawn.Destroyed)
        {
            return false;
        }

        if (pawn.IsWorldPawn())
        {
            return true;
        }

        if (pawn.Spawned)
        {
            MugirlLog.WarningOnce(
                "GeneratedPawnPassToWorldSpawned",
                "Tried to pass a spawned generated pawn to world storage; leaving it on its current map.");
            return false;
        }

        bool wasFactionless = pawn.Faction == null;
        bool registered = GameUtility.TryPassToWorld(pawn);
        // 任务尚未登记时，原版把人物视作自由世界人物并随机分配派系。
        // 救援/申请加入要保留无派系状态；巨企人员离图等调用仍遵循原版行为。
        if (registered && keepFactionless && wasFactionless && pawn.Faction != null)
        {
            pawn.SetFaction(null);
        }

        return registered;
    }

    public static void Discard(Pawn? pawn)
    {
        if (pawn == null || pawn.Destroyed)
        {
            return;
        }

        if (pawn.Spawned)
        {
            MugirlLog.WarningOnce(
                "GeneratedPawnDiscardSpawned",
                "Tried to discard a spawned generated pawn; leaving it on its current map.");
            return;
        }

        GameUtility.TryRemoveWorldPawn(pawn);

        GameUtility.TryPassToWorldForDiscard(pawn);
    }
}
