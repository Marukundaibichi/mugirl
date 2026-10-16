using System.Diagnostics.CodeAnalysis;
using LudeonTK;
using Mugirl.Features.Corporate;

namespace Mugirl.Features.Incidents;

internal static class StorylineDebugActions
{
    private const float OpeningCrashPoints = 10000f;
    private const float CourierRaidPoints = 200f;

    [DebugAction("Mugirl", "Trigger early: Mugirl pod crash", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static void TriggerOpeningCrash()
    {
        if (!TryGetContext(out Map? map, out GameComponent_Storyline? state, out _))
        {
            return;
        }

        if (state.OpeningCrash.started || HasQuest(IncidentsDefOf.Mugirl_SlaveOpeningPodCrash))
        {
            Report(DebugOutcome.AlreadyTriggered);
            return;
        }

        // 调试入口忽略天数和自动事件开关，但仍使用正式任务及其生成流程。
        if (QuestLauncher.TryLaunch<QuestPart_JoinOnRescue>(IncidentsDefOf.Mugirl_SlaveOpeningPodCrash, map, OpeningCrashPoints) == null)
        {
            Report(DebugOutcome.Failed);
            return;
        }

        state.MarkOpeningCrashStarted();
        state.WakeStoryService();
        Report(DebugOutcome.Triggered);
    }

    [DebugAction("Mugirl", "Trigger early: Courier contact", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static void TriggerCourier()
    {
        if (!TryGetContext(out Map? map, out GameComponent_Storyline? state, out _))
        {
            return;
        }

        if (state.Courier.triggered || state.Courier.questStarted || HasQuest(IncidentsDefOf.Mugirl_CourierContact))
        {
            Report(DebugOutcome.AlreadyTriggered);
            return;
        }

        if (QuestLauncher.TryLaunch<QuestPart_SpawnCourier>(IncidentsDefOf.Mugirl_CourierContact, map, CourierRaidPoints) == null)
        {
            Report(DebugOutcome.Failed);
            return;
        }

        state.MarkCourierRaidStarted();
        state.Courier.delayRemaining = 0;
        state.WakeStoryService();
        Report(DebugOutcome.Triggered);
    }

    [DebugAction("Mugirl", "Trigger early: Corporate representative", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static void TriggerRepresentative()
    {
        if (!TryGetContext(out Map? map, out _, out CorporateIntroduction? introduction))
        {
            return;
        }

        string? failure = introduction.DevTriggerRepresentative(map);
        Report(failure == null ? DebugOutcome.Triggered : ParseCorporateOutcome(failure));
    }

    [DebugAction("Mugirl", "Deliver pending bovine fusion reward", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static void DeliverPendingFusionInvestmentResult()
    {
        FusionInvestmentUtility.DevDeliverPendingResult();
    }

    private static bool TryGetContext(
        [NotNullWhen(true)] out Map? map,
        [NotNullWhen(true)] out GameComponent_Storyline? state,
        [NotNullWhen(true)] out CorporateIntroduction? introduction)
    {
        map = GameUtility.LoadedMaps?.Find(GameUtility.IsCurrentMap);
        state = GameUtility.GameComponent<GameComponent_Storyline>();
        introduction = CorporateIntroduction.Current;
        if (!Prefs.DevMode || map?.IsPlayerHome != true || state == null || introduction == null)
        {
            Report(DebugOutcome.RequiresHomeMap);
            return false;
        }

        Faction? faction = introduction.EnsureCorporateFactionAvailable();
        if (faction == null || faction.defeated)
        {
            Report(DebugOutcome.NoFaction);
            return false;
        }

        return true;
    }

    private static bool HasQuest(QuestScriptDef root)
    {
        if (!GameUtility.TryGetQuestsListForReading(out List<Quest>? quests))
        {
            return false;
        }

        for (int i = 0; i < quests.Count; i++)
        {
            if (quests[i].root == root)
            {
                return true;
            }
        }

        return false;
    }

    // CorporateIntroduction.DevTriggerRepresentative 以字符串返回失败原因。
    private static DebugOutcome ParseCorporateOutcome(string failure)
    {
        return failure switch
        {
            "RequiresHomeMap" => DebugOutcome.RequiresHomeMap,
            "AlreadyTriggered" => DebugOutcome.AlreadyTriggered,
            "NoFaction" => DebugOutcome.NoFaction,
            _ => DebugOutcome.Failed,
        };
    }

    // 翻译键写成字面量，离线校验才能确认它们在中英文语言文件中都存在。
    private static void Report(DebugOutcome outcome)
    {
        string key = outcome switch
        {
            DebugOutcome.Triggered => "Mugirl.StoryDebug.Triggered",
            DebugOutcome.AlreadyTriggered => "Mugirl.StoryDebug.AlreadyTriggered",
            DebugOutcome.RequiresHomeMap => "Mugirl.StoryDebug.RequiresHomeMap",
            DebugOutcome.NoFaction => "Mugirl.StoryDebug.NoFaction",
            _ => "Mugirl.StoryDebug.Failed",
        };
        Messages.Message(key.Translate(),
            outcome == DebugOutcome.Triggered ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, historical: false);
    }

    private enum DebugOutcome
    {
        Triggered,
        AlreadyTriggered,
        RequiresHomeMap,
        NoFaction,
        Failed,
    }
}
