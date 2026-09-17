using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace Mugirl
{
    internal static class MugirlStoryDebugActions
    {
        [DebugAction("Mugirl", "Trigger early: Mugirl pod crash", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TriggerOpeningCrash()
        {
            if (!TryGetContext(out Map map, out MugirlStoryState state)) return;
            if (state.openingCrashStarted || HasQuest(Mugirl_DefOf.Mugirl_SlaveOpeningPodCrash))
            {
                Report("AlreadyTriggered");
                return;
            }

            // 调试入口忽略天数和自动事件开关，但仍使用正式任务及其生成流程。
            if (!GenerateQuest(Mugirl_DefOf.Mugirl_SlaveOpeningPodCrash, map, 10000f)) return;
            state.openingCrashStarted = true;
            state.openingCrashCheckTimer = -1;
            state.WakeStoryService();
            Report("Triggered", true);
        }

        [DebugAction("Mugirl", "Trigger early: Courier contact", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TriggerCourier()
        {
            if (!TryGetContext(out Map map, out MugirlStoryState state)) return;
            if (state.courierRaidTriggered || state.courierRaidQuestStarted || HasQuest(MugirlContentDefOf.Mugirl_CourierRaid))
            {
                Report("AlreadyTriggered");
                return;
            }

            if (!GenerateQuest(MugirlContentDefOf.Mugirl_CourierRaid, map, 200f)) return;
            state.courierRaidTriggered = true;
            state.courierRaidQuestStarted = true;
            state.courierRaidTimer = 0;
            state.WakeStoryService();
            Report("Triggered", true);
        }

        [DebugAction("Mugirl", "Trigger early: Corporate representative", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TriggerRepresentative()
        {
            if (!TryGetContext(out Map map, out _)) return;
            string failure = CorporateIntroduction.Current.DevTriggerRepresentative(map);
            Report(failure ?? "Triggered", failure == null);
        }

        private static bool TryGetContext(out Map map, out MugirlStoryState state)
        {
            map = MugirlGameUtility.LoadedMaps?.FirstOrDefault(MugirlGameUtility.IsCurrentMap);
            state = MugirlGameUtility.GameComponent<MugirlStoryState>();
            if (!Prefs.DevMode || map?.IsPlayerHome != true || state == null || CorporateIntroduction.Current == null)
            {
                Report("RequiresHomeMap");
                return false;
            }
            Faction faction = CorporateIntroduction.Current.EnsureCorporateFactionAvailable();
            if (faction == null || faction.defeated)
            {
                Report("NoFaction");
                return false;
            }
            return true;
        }

        private static bool HasQuest(QuestScriptDef root)
        {
            return MugirlGameUtility.TryGetQuestsListForReading(out var quests) && quests.Any(q => q.root == root);
        }

        private static bool GenerateQuest(QuestScriptDef root, Map map, float points)
        {
            Slate slate = new Slate();
            slate.Set("map", map);
            slate.Set("points", points);
            Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(root, slate);
            if (quest == null || quest.PartsListForReading.Count == 0)
            {
                Report("Failed");
                return false;
            }
            return true;
        }

        private static void Report(string suffix, bool success = false)
        {
            Messages.Message(("Mugirl.StoryDebug." + suffix).Translate(),
                success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, historical: false);
        }
    }
}
