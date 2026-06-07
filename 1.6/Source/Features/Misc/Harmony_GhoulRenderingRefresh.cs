using HarmonyLib;
using System.Collections.Generic;
using Verse;

namespace MooGirl
{
    [HarmonyPatch(typeof(Hediff), nameof(Hediff.PostAdd))]
    public static class Harmony_GhoulRenderingRefresh_PostAdd
    {
        public static void Postfix(Hediff __instance)
        {
            if (__instance?.def?.defName != "Ghoul")
            {
                return;
            }

            GhoulRenderingRefreshUtility.NotifyGhoulChanged(__instance.pawn);
        }
    }

    public class GameComponent_GhoulRenderingRefresh : GameComponent
    {
        public GameComponent_GhoulRenderingRefresh()
        {
            GhoulRenderingRefreshUtility.ClearPendingRefreshes();
        }

        public GameComponent_GhoulRenderingRefresh(Game game)
        {
            GhoulRenderingRefreshUtility.ClearPendingRefreshes();
        }

        public override void GameComponentTick()
        {
            GhoulRenderingRefreshUtility.TickPendingRefreshes();
        }
    }

    public static class GhoulRenderingRefreshUtility
    {
        private const int RefreshWindowTicks = 300;
        private const int RefreshIntervalTicks = 30;

        // StaticCacheLifecycle: 每局游戏待刷新 Pawn 集合；构造、初始化、新建和读档时清空。
        private static readonly Dictionary<Pawn, int> pendingRefreshUntilTick = new Dictionary<Pawn, int>();
        // StaticCacheLifecycle: 每次刷新使用的临时列表；刷新前后都会清空。
        private static readonly List<Pawn> tmpPawnsToRemove = new List<Pawn>();

        public static void NotifyGhoulChanged(Pawn pawn)
        {
            if (!MountedPawnUtility.IsMooGirl(pawn) || pawn.Destroyed)
            {
                return;
            }

            RefreshGraphics(pawn);
            if (!MooGirlGameUtility.IsPlaying())
            {
                return;
            }

            if (MooGirlTickUtility.TryGetCurrentGameTick(out int currentTick))
            {
                pendingRefreshUntilTick[pawn] = currentTick + RefreshWindowTicks;
            }
        }

        public static void TickPendingRefreshes()
        {
            if (pendingRefreshUntilTick.Count == 0)
            {
                return;
            }

            if (!MooGirlTickUtility.TryGetCurrentGameTick(out int currentTick))
            {
                return;
            }

            tmpPawnsToRemove.Clear();
            if (currentTick % RefreshIntervalTicks != 0)
            {
                return;
            }

            foreach (KeyValuePair<Pawn, int> entry in pendingRefreshUntilTick)
            {
                Pawn pawn = entry.Key;
                if (!MountedPawnUtility.IsMooGirl(pawn) || pawn.Destroyed || currentTick > entry.Value)
                {
                    tmpPawnsToRemove.Add(pawn);
                    continue;
                }

                RefreshGraphics(pawn);
            }

            for (int i = 0; i < tmpPawnsToRemove.Count; i++)
            {
                pendingRefreshUntilTick.Remove(tmpPawnsToRemove[i]);
            }

            tmpPawnsToRemove.Clear();
        }

        public static void ClearPendingRefreshes()
        {
            pendingRefreshUntilTick.Clear();
            tmpPawnsToRemove.Clear();
        }

        private static void RefreshGraphics(Pawn pawn)
        {
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }
}
