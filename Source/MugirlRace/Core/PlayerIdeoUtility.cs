namespace Mugirl.Core;

internal static class PlayerIdeoUtility
{
    // 加入玩家的救援对象与洗脑对象改信玩家的主要文化。
    internal static void AdoptPlayerPrimaryIdeo(Pawn? pawn)
    {
        if (pawn == null || !ModsConfig.IdeologyActive)
        {
            return;
        }

        Faction? playerFaction = Faction.OfPlayerSilentFail;
        Ideo? primaryIdeo = playerFaction?.ideos?.PrimaryIdeo;
        if (primaryIdeo == null || pawn.ideo == null || pawn.Ideo == primaryIdeo)
        {
            return;
        }

        pawn.ideo.SetIdeo(primaryIdeo);
    }
}
