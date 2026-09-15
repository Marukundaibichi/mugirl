using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace Mugirl
{
    internal static class MugirlGameUtility
    {
        // 巨企跨地图合同在同一局游戏中查询这些服务；不保存全局静态游戏对象引用。
        internal static List<Map> LoadedMaps => Current.Game?.Maps;
        internal static FactionManager Factions => Current.Game?.World?.factionManager;
        internal static WorldObjectsHolder WorldObjects => Find.WorldObjects;
        internal static WorldPawns WorldPawns => Find.WorldPawns;
        internal static WindowStack Windows => Find.WindowStack;
        internal static LetterStack Letters => Find.LetterStack;
        internal static QuestManager Quests => Find.QuestManager;
        internal static Storyteller Storyteller => Current.Game?.storyteller;
        internal static HistoryEventsManager HistoryEvents => Find.HistoryEventsManager;
        internal static MainTabsRoot MainTabs => Find.MainTabsRoot;
        internal static T GameComponent<T>() where T : GameComponent => Current.Game?.GetComponent<T>();

        internal static bool TryAddWindow(Window window)
        {
            if (window == null)
            {
                return false;
            }

            WindowStack windowStack = Find.WindowStack;
            if (windowStack == null)
            {
                MugirlLog.WarningOnce(
                    "GameUtility.WindowStackUnavailable",
                    "Mugirl.GameUtility.WindowStackUnavailable".Translate().ToString());
                return false;
            }

            windowStack.Add(window);
            return true;
        }

        internal static bool TryBeginTargeting(TargetingParameters parameters, Action<LocalTargetInfo> action, Pawn caster)
        {
            if (parameters == null || action == null || Find.Targeter == null)
            {
                return false;
            }

            Find.Targeter.BeginTargeting(parameters, action, caster);
            return true;
        }

        internal static bool TryBeginTargeting(
            ITargetingSource source,
            Action actionWhenFinished,
            bool allowNonSelectedTargetingSource,
            bool requiresAvailableVerb)
        {
            if (source == null || Find.Targeter == null)
            {
                return false;
            }

            Find.Targeter.BeginTargeting(
                source,
                parent: null,
                allowNonSelectedTargetingSource: allowNonSelectedTargetingSource,
                extraSourceGetter: null,
                actionWhenFinished: actionWhenFinished,
                requiresAvailableVerb: requiresAvailableVerb);
            return true;
        }

        internal static bool IsTargetingSource(ITargetingSource source)
        {
            return source != null
                && Find.Targeter != null
                && Find.Targeter.IsTargeting
                && Find.Targeter.targetingSource == source;
        }

        internal static bool TryStopTargeting(ITargetingSource source)
        {
            if (!IsTargetingSource(source))
            {
                return false;
            }

            Find.Targeter.StopTargeting();
            return true;
        }

        internal static bool TryReceiveLetter(Letter letter, string debugInfo = null, int delayTicks = 0, bool playSound = true)
        {
            if (letter == null || !CanReceiveLetter())
            {
                return false;
            }

            Current.Game.letterStack.ReceiveLetter(letter, debugInfo, delayTicks, playSound);
            return true;
        }

        internal static bool TryReceiveLetter(
            TaggedString label,
            TaggedString text,
            LetterDef letterDef,
            LookTargets lookTargets,
            Faction relatedFaction = null,
            Quest quest = null,
            List<ThingDef> hyperlinkThingDefs = null,
            string debugInfo = null,
            int delayTicks = 0,
            bool playSound = true)
        {
            if (letterDef == null || !CanReceiveLetter())
            {
                return false;
            }

            Current.Game.letterStack.ReceiveLetter(label, text, letterDef, lookTargets, relatedFaction, quest, hyperlinkThingDefs, debugInfo, delayTicks, playSound);
            return true;
        }

        internal static bool TryRemoveLetter(Letter letter)
        {
            if (letter == null)
            {
                return false;
            }

            LetterStack letterStack = Find.LetterStack;
            if (letterStack == null)
            {
                MugirlLog.WarningOnce(
                    "GameUtility.LetterStackUnavailable",
                    "Mugirl.GameUtility.LetterStackUnavailable".Translate().ToString());
                return false;
            }

            letterStack.RemoveLetter(letter);
            return true;
        }

        internal static bool TrySignalForceNormalSpeedShort()
        {
            if (Current.Game?.tickManager?.slower == null)
            {
                return false;
            }

            Current.Game.tickManager.slower.SignalForceNormalSpeedShort();
            return true;
        }

        internal static bool IsPlaying()
        {
            return Current.ProgramState == ProgramState.Playing;
        }

        internal static bool IsResearchFinished(ResearchProjectDef project)
        {
            return project != null
                && Current.Game?.researchManager != null
                && project.IsFinished;
        }

        internal static bool TryGetGameComponent<T>(out T component) where T : GameComponent
        {
            component = Current.Game?.GetComponent<T>();
            return component != null;
        }

        internal static bool ChildrenAllowedByCurrentDifficulty()
        {
            return Current.Game?.storyteller?.difficulty?.ChildrenAllowed == true;
        }

        internal static bool IsCurrentMap(Map map)
        {
            return map != null && Current.Game?.CurrentMap == map;
        }

        internal static bool TryDeinitAndRemoveMap(Map map, bool notifyPlayer)
        {
            if (map == null || Current.Game == null)
            {
                return false;
            }

            Current.Game.DeinitAndRemoveMap(map, notifyPlayer);
            return true;
        }

        internal static bool TryMarkColonistsDirty()
        {
            if (!IsPlaying())
            {
                return false;
            }

            ColonistBar colonistBar = Find.ColonistBar;
            if (colonistBar == null)
            {
                return false;
            }

            colonistBar.MarkColonistsDirty();
            return true;
        }

        internal static bool TryGetQuestsListForReading(out List<Quest> quests)
        {
            quests = Current.Game?.questManager?.QuestsListForReading;
            return quests != null;
        }

        internal static bool TryGetFirstFactionOfDef(FactionDef factionDef, out Faction faction)
        {
            faction = null;
            if (factionDef == null)
            {
                return false;
            }

            FactionManager factionManager = Current.Game?.World?.factionManager;
            if (factionManager == null)
            {
                return false;
            }

            faction = factionManager.FirstFactionOfDef(factionDef);
            return faction != null;
        }

        internal static bool TryGetRandomNonHostileFaction(out Faction faction, bool allowHidden, TechLevel minTechLevel)
        {
            faction = null;
            FactionManager factionManager = Find.FactionManager;
            if (factionManager == null)
            {
                return false;
            }

            faction = factionManager.RandomNonHostileFaction(
                allowHidden: allowHidden,
                allowDefeated: false,
                allowNonHumanlike: true,
                minTechLevel: minTechLevel);
            return faction != null;
        }

        internal static bool TryResolvePlayerEventMap(out Map map)
        {
            map = Current.Game?.AnyPlayerHomeMap;
            if (map != null)
            {
                return true;
            }

            List<Map> maps = Current.Game?.Maps;
            if (maps == null)
            {
                return false;
            }

            for (int i = 0; i < maps.Count; i++)
            {
                map = maps[i];
                if (map != null)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool TryPassToWorld(Pawn pawn)
        {
            if (!TryGetWorldPawns(out WorldPawns worldPawns))
            {
                return false;
            }

            worldPawns.PassToWorld(pawn);
            return true;
        }

        internal static bool TryPassToWorldForDiscard(Pawn pawn)
        {
            if (!TryGetWorldPawns(out WorldPawns worldPawns))
            {
                return false;
            }

            worldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
            return true;
        }

        internal static bool TryRemoveWorldPawn(Pawn pawn)
        {
            if (!TryGetWorldPawns(out WorldPawns worldPawns))
            {
                return false;
            }

            if (worldPawns.Contains(pawn))
            {
                worldPawns.RemovePawn(pawn);
                return true;
            }

            return false;
        }

        internal static bool WorldPawnsContains(Pawn pawn)
        {
            return TryGetWorldPawns(out WorldPawns worldPawns) && worldPawns.Contains(pawn);
        }

        internal static bool TrySendSignal(string signalTag)
        {
            if (signalTag.NullOrEmpty())
            {
                return false;
            }

            SignalManager signalManager = Find.SignalManager;
            if (signalManager == null)
            {
                return false;
            }

            signalManager.SendSignal(new Signal(signalTag));
            return true;
        }

        private static bool CanReceiveLetter()
        {
            if (Current.Game?.letterStack == null || Current.Game.tickManager == null || Current.Game.history?.archive == null)
            {
                MugirlLog.WarningOnce(
                    "GameUtility.LetterStackUnavailable",
                    "Mugirl.GameUtility.LetterStackUnavailable".Translate().ToString());
                return false;
            }

            return true;
        }

        private static bool TryGetWorldPawns(out WorldPawns worldPawns)
        {
            worldPawns = Current.Game?.World?.worldPawns;
            if (worldPawns == null)
            {
                MugirlLog.WarningOnce(
                    "GameUtility.WorldPawnsUnavailable",
                    "Mugirl.GameUtility.WorldPawnsUnavailable".Translate().ToString());
                return false;
            }

            return true;
        }
    }
}
