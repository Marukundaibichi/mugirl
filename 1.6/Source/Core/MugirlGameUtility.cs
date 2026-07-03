using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace Mugirl
{
    internal static class MugirlGameUtility
    {
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
