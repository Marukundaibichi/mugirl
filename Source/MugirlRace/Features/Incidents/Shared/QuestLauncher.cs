using RimWorld.QuestGen;

namespace Mugirl.Features.Incidents;

// 生成并发布本模组的任务。原版 QuestUtility.GenerateQuestAndMakeAvailable 无论任务根节点是否中途放弃都会把任务加入
// QuestManager（已在 1.7 反编译核对），失败的生成会留下永不结束的空任务；这里只发布含有标志性 QuestPart 的任务。
internal static class QuestLauncher
{
    internal const string MapSlateKey = "map";
    internal const string PointsSlateKey = "points";

    /// <summary>生成任务；任务含 <typeparamref name="TPart"/> 时加入 QuestManager 并返回，否则丢弃并返回 null。</summary>
    internal static Quest? TryLaunch<TPart>(QuestScriptDef root, Map map, float points)
        where TPart : QuestPart
    {
        Slate slate = new Slate();
        slate.Set(PointsSlateKey, points);
        slate.Set(MapSlateKey, map);

        // Generate 在异常时返回 null（原版已记录错误）。
        Quest? quest = QuestGen.Generate(root, slate);
        if (quest == null || !HasPart<TPart>(quest))
        {
            return null;
        }

        GameUtility.Quests.Add(quest);
        return quest;
    }

    internal static bool HasPart<TPart>(Quest quest)
        where TPart : QuestPart
    {
        List<QuestPart> parts = quest.PartsListForReading;
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i] is TPart)
            {
                return true;
            }
        }

        return false;
    }

    // 任务根节点的目标地图：优先 slate 中的 map，否则用玩家事件地图。
    internal static Map? ResolveTargetMap(Slate? slate)
    {
        if (slate != null && slate.TryGet(MapSlateKey, out Map? mapFromSlate) && mapFromSlate != null)
        {
            return mapFromSlate;
        }

        return GameUtility.TryResolvePlayerEventMap(out Map? map) ? map : null;
    }
}
