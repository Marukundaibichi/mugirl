using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Core;

// 玩家派系判断；没有游戏或玩家派系时安全返回（Faction.OfPlayerSilentFail）。
internal static class PlayerFactionUtility
{
    internal static Faction? PlayerFaction => Faction.OfPlayerSilentFail;

    internal static bool IsPlayerFaction([NotNullWhen(true)] Faction? faction)
    {
        Faction? playerFaction = PlayerFaction;
        return playerFaction != null && faction == playerFaction;
    }

    internal static bool IsHostileToPlayer([NotNullWhen(true)] Faction? faction)
    {
        Faction? playerFaction = PlayerFaction;
        return faction != null && playerFaction != null && faction.HostileTo(playerFaction);
    }

    internal static bool IsHostileToPlayer([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn != null && IsHostileToPlayer(pawn.Faction);
    }

    internal static bool TrySetPlayerFaction(Pawn? pawn)
    {
        Faction? playerFaction = PlayerFaction;
        if (pawn == null || pawn.Destroyed || playerFaction == null)
        {
            return false;
        }

        pawn.SetFaction(playerFaction);
        return IsPlayerFaction(pawn.Faction);
    }
}
