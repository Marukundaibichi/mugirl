using System.Diagnostics.CodeAnalysis;
using RimWorld.Planet;

namespace Mugirl.Core;

// 对 Current.Game / Find.* 的判空封装。可空标注按原版实现给出：经 Current.Game?. 取得的服务在没有游戏时为 null，
// 经 Find.World / Current.Game. 直接取得的服务只在游戏进行中可用（没有游戏时原版会抛 NullReferenceException）。
// 不保存全局静态游戏对象引用。
internal static class GameUtility
{
    internal static List<Map>? LoadedMaps => Current.Game?.Maps;

    internal static FactionManager? Factions => Current.Game?.World?.factionManager;

    internal static WorldObjectsHolder WorldObjects => Find.WorldObjects;

    internal static WorldGrid WorldGrid => Find.WorldGrid;

    internal static WorldPawns WorldPawns => Find.WorldPawns;

    /// <summary>没有 UIRoot 时为 null（Find.WindowStack 是 UIRoot?.windows）；打开窗口优先用 <see cref="TryAddWindow"/>。</summary>
    internal static WindowStack? Windows => Find.WindowStack;

    internal static LetterStack Letters => Find.LetterStack;

    internal static QuestManager Quests => Find.QuestManager;

    internal static Storyteller? Storyteller => Current.Game?.storyteller;

    internal static ResearchManager? ResearchManager => Current.Game?.researchManager;

    internal static HistoryEventsManager HistoryEvents => Find.HistoryEventsManager;

    internal static MainTabsRoot MainTabs => Find.MainTabsRoot;

    internal static T? GameComponent<T>() where T : GameComponent => Current.Game?.GetComponent<T>();

    internal static bool IsCurrentGame(Game game) => ReferenceEquals(Current.Game, game);

    internal static bool TryShowMap(Map? map)
    {
        if (map is null || LoadedMaps?.Contains(map) != true)
        {
            return false;
        }

        Current.Game.CurrentMap = map;
        Find.World.renderer.wantedMode = WorldRenderMode.None;
        return true;
    }

    internal static bool TryAddWindow(Window? window)
    {
        if (window is null)
        {
            return false;
        }

        WindowStack? windowStack = Find.WindowStack;
        if (windowStack is null)
        {
            MugirlLog.WarningOnce(
                "GameUtility.WindowStackUnavailable",
                () => "Mugirl.GameUtility.WindowStackUnavailable".Translate().ToString());
            return false;
        }

        windowStack.Add(window);
        return true;
    }

    internal static bool TryBeginTargeting(TargetingParameters? parameters, Action<LocalTargetInfo>? action, Pawn? caster)
    {
        if (parameters is null || action is null || Find.Targeter is not { } targeter)
        {
            return false;
        }

        targeter.BeginTargeting(parameters, action, caster);
        return true;
    }

    internal static bool TryBeginTargeting(
        ITargetingSource? source,
        Action? actionWhenFinished,
        bool allowNonSelectedTargetingSource,
        bool requiresAvailableVerb)
    {
        if (source is null || Find.Targeter is not { } targeter)
        {
            return false;
        }

        targeter.BeginTargeting(
            source,
            parent: null,
            allowNonSelectedTargetingSource: allowNonSelectedTargetingSource,
            extraSourceGetter: null,
            actionWhenFinished: actionWhenFinished,
            requiresAvailableVerb: requiresAvailableVerb);
        return true;
    }

    internal static bool IsTargetingSource([NotNullWhen(true)] ITargetingSource? source)
    {
        return source is not null
            && Find.Targeter is { IsTargeting: true } targeter
            && targeter.targetingSource == source;
    }

    internal static bool TryStopTargeting(ITargetingSource? source)
    {
        if (!IsTargetingSource(source))
        {
            return false;
        }

        Find.Targeter.StopTargeting();
        return true;
    }

    internal static bool TryReceiveLetter(Letter? letter, string? debugInfo = null, int delayTicks = 0, bool playSound = true)
    {
        if (letter is null || !CanReceiveLetter())
        {
            return false;
        }

        Current.Game.letterStack.ReceiveLetter(letter, debugInfo, delayTicks, playSound);
        return true;
    }

    internal static bool TryReceiveLetter(
        TaggedString label,
        TaggedString text,
        LetterDef? letterDef,
        LookTargets? lookTargets,
        Faction? relatedFaction = null,
        Quest? quest = null,
        List<ThingDef>? hyperlinkThingDefs = null,
        string? debugInfo = null,
        int delayTicks = 0,
        bool playSound = true)
    {
        if (letterDef is null || !CanReceiveLetter())
        {
            return false;
        }

        Current.Game.letterStack.ReceiveLetter(label, text, letterDef, lookTargets, relatedFaction, quest, hyperlinkThingDefs, debugInfo, delayTicks, playSound);
        return true;
    }

    internal static bool TryRemoveLetter(Letter? letter)
    {
        if (letter is null)
        {
            return false;
        }

        LetterStack? letterStack = Current.Game?.letterStack;
        if (letterStack is null)
        {
            MugirlLog.WarningOnce(
                "GameUtility.LetterStackUnavailable",
                () => "Mugirl.GameUtility.LetterStackUnavailable".Translate().ToString());
            return false;
        }

        letterStack.RemoveLetter(letter);
        return true;
    }

    internal static bool TrySignalForceNormalSpeedShort()
    {
        if (Current.Game?.tickManager?.slower is not { } slower)
        {
            return false;
        }

        slower.SignalForceNormalSpeedShort();
        return true;
    }

    internal static bool IsPlaying()
    {
        return Current.ProgramState == ProgramState.Playing;
    }

    internal static bool IsResearchFinished([NotNullWhen(true)] ResearchProjectDef? project)
    {
        return project is not null
            && Current.Game?.researchManager is not null
            && project.IsFinished;
    }

    internal static bool TryGetGameComponent<T>([MaybeNullWhen(false)] out T component) where T : GameComponent
    {
        component = Current.Game?.GetComponent<T>();
        return component is not null;
    }

    internal static bool ChildrenAllowedByCurrentDifficulty()
    {
        return Current.Game?.storyteller?.difficulty?.ChildrenAllowed == true;
    }

    internal static bool IsCurrentMap([NotNullWhen(true)] Map? map)
    {
        return map is not null && Current.Game?.CurrentMap == map;
    }

    internal static bool TryShakeCamera(Map? map, float magnitude, int durationTicks)
    {
        CameraDriver? cameraDriver = Find.CameraDriver;
        if (!IsCurrentMap(map) || magnitude <= 0f || durationTicks <= 0 || cameraDriver?.shaker is not { } shaker)
        {
            return false;
        }

        shaker.DoShake(magnitude, durationTicks);
        return true;
    }

    internal static bool TryDeinitAndRemoveMap(Map? map, bool notifyPlayer)
    {
        if (map is null || Current.Game is not { } game)
        {
            return false;
        }

        game.DeinitAndRemoveMap(map, notifyPlayer);
        return true;
    }

    internal static bool TryMarkColonistsDirty()
    {
        if (!IsPlaying())
        {
            return false;
        }

        ColonistBar? colonistBar = Find.ColonistBar;
        if (colonistBar is null)
        {
            return false;
        }

        colonistBar.MarkColonistsDirty();
        return true;
    }

    internal static bool TryGetQuestsListForReading([MaybeNullWhen(false)] out List<Quest> quests)
    {
        quests = Current.Game?.questManager?.QuestsListForReading;
        return quests is not null;
    }

    internal static bool TryGetFirstFactionOfDef(FactionDef? factionDef, [MaybeNullWhen(false)] out Faction faction)
    {
        faction = null;
        if (factionDef is null)
        {
            return false;
        }

        FactionManager? factionManager = Current.Game?.World?.factionManager;
        if (factionManager is null)
        {
            return false;
        }

        faction = factionManager.FirstFactionOfDef(factionDef);
        return faction is not null;
    }

    internal static bool TryGetRandomNonHostileFaction([MaybeNullWhen(false)] out Faction faction, bool allowHidden, TechLevel minTechLevel)
    {
        faction = null;
        FactionManager? factionManager = Find.FactionManager;
        if (factionManager is null)
        {
            return false;
        }

        faction = factionManager.RandomNonHostileFaction(
            allowHidden: allowHidden,
            allowDefeated: false,
            allowNonHumanlike: true,
            minTechLevel: minTechLevel);
        return faction is not null;
    }

    internal static bool TryResolvePlayerEventMap([MaybeNullWhen(false)] out Map map)
    {
        map = Current.Game?.AnyPlayerHomeMap;
        if (map is not null)
        {
            return true;
        }

        List<Map>? maps = Current.Game?.Maps;
        if (maps is null)
        {
            return false;
        }

        for (int i = 0; i < maps.Count; i++)
        {
            map = maps[i];
            if (map is not null)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryPassToWorld(Pawn pawn)
    {
        if (!TryGetWorldPawns(out WorldPawns? worldPawns))
        {
            return false;
        }

        worldPawns.PassToWorld(pawn);
        return true;
    }

    internal static bool TryPassToWorldForDiscard(Pawn pawn)
    {
        if (!TryGetWorldPawns(out WorldPawns? worldPawns))
        {
            return false;
        }

        worldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
        return true;
    }

    internal static bool TryRemoveWorldPawn(Pawn pawn)
    {
        if (!TryGetWorldPawns(out WorldPawns? worldPawns))
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

    internal static bool TrySendSignal(string? signalTag)
    {
        if (signalTag.NullOrEmpty())
        {
            return false;
        }

        SignalManager? signalManager = Current.Game?.signalManager;
        if (signalManager is null)
        {
            return false;
        }

        signalManager.SendSignal(new Signal(signalTag));
        return true;
    }

    private static bool CanReceiveLetter()
    {
        if (Current.Game?.letterStack is null || Current.Game.tickManager is null || Current.Game.history?.archive is null)
        {
            MugirlLog.WarningOnce(
                "GameUtility.LetterStackUnavailable",
                () => "Mugirl.GameUtility.LetterStackUnavailable".Translate().ToString());
            return false;
        }

        return true;
    }

    private static bool TryGetWorldPawns([NotNullWhen(true)] out WorldPawns? worldPawns)
    {
        worldPawns = Current.Game?.World?.worldPawns;
        if (worldPawns is null)
        {
            MugirlLog.WarningOnce(
                "GameUtility.WorldPawnsUnavailable",
                () => "Mugirl.GameUtility.WorldPawnsUnavailable".Translate().ToString());
            return false;
        }

        return true;
    }
}
