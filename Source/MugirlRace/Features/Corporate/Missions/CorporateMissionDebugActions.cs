using LudeonTK;

namespace Mugirl.Features.Corporate;

internal static class CorporateMissionDebugActions
{
    // 在当前地图中心直接生成核聚变研究站，用于检查蓝图、整地与通道。
    [DebugAction("Mugirl", "Test map: fusion research site", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static void SpawnFusionResearchSiteAtMapCenter()
    {
        Map? map = GameUtility.LoadedMaps?.Find(GameUtility.IsCurrentMap);
        if (!Prefs.DevMode || map == null)
        {
            Messages.Message("Mugirl.CQ.DebugMapUnavailable".Translate(), MessageTypeDefOf.RejectInput, historical: false);
            return;
        }

        try
        {
            CorporateNetwork.DevSpawnFusionResearchSite(map);
            Messages.Message("Mugirl.CQ.DebugMapSpawned".Translate(), MessageTypeDefOf.PositiveEvent, historical: false);
        }
        catch (Exception e)
        {
            MugirlLog.WarningOnce("CorporateQuestSite.Debug", "Corporate fusion research map debug spawn failed: " + e.Message);
            Messages.Message("Mugirl.CQ.DebugMapFailed".Translate(e.Message), MessageTypeDefOf.RejectInput, historical: false);
        }
    }
}
