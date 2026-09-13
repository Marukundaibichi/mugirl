using HarmonyLib;
using System.Collections.Generic;
using Verse;

namespace Mugirl
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

            PawnRenderingRefreshUtility.NotifyPawnChanged(__instance.pawn);
        }
    }

    public class GameComponent_GhoulRenderingRefresh : GameComponent
    {
        public GameComponent_GhoulRenderingRefresh()
        {
            PawnRenderingRefreshUtility.ClearPendingRefreshes();
        }

        public GameComponent_GhoulRenderingRefresh(Game game)
        {
            PawnRenderingRefreshUtility.ClearPendingRefreshes();
        }

        public override void GameComponentTick()
        {
            PawnRenderingRefreshUtility.TickPendingRefreshes();
        }
    }

    public static class PawnRenderingRefreshUtility
    {
        private const int RefreshWindowTicks = 300;
        private const int EarlyRefreshDelayTicks = 30;

        // StaticCacheLifecycle: 每局游戏待刷新 Pawn 集合；构造、初始化、新建和读档时清空。
        private static readonly Dictionary<Pawn, PendingRefresh> pendingRefreshes = new Dictionary<Pawn, PendingRefresh>();
        // StaticCacheLifecycle: 每次刷新使用的临时列表；刷新前后都会清空。
        private static readonly List<Pawn> tmpPawnsToProcess = new List<Pawn>();
        // StaticCacheLifecycle: 下一次到期时间；与待刷新集合一起在每局游戏重置。
        private static int nextPendingRefreshTick = int.MaxValue;

        private struct PendingRefresh
        {
            internal int nextTick;
            internal int finalTick;

            internal PendingRefresh(int currentTick)
            {
                nextTick = currentTick + EarlyRefreshDelayTicks;
                finalTick = currentTick + RefreshWindowTicks;
            }
        }

        public static void NotifyPawnChanged(Pawn pawn)
        {
            if (!MountedPawnUtility.IsMugirl(pawn) || pawn.Destroyed)
            {
                return;
            }

            RefreshGraphics(pawn);
            if (!MugirlGameUtility.IsPlaying())
            {
                return;
            }

            if (MugirlTickUtility.TryGetCurrentGameTick(out int currentTick))
            {
                PendingRefresh refresh = new PendingRefresh(currentTick);
                pendingRefreshes[pawn] = refresh;
                if (refresh.nextTick < nextPendingRefreshTick)
                {
                    nextPendingRefreshTick = refresh.nextTick;
                }
            }
        }

        public static void TickPendingRefreshes()
        {
            if (pendingRefreshes.Count == 0)
            {
                return;
            }

            if (!MugirlTickUtility.TryGetCurrentGameTick(out int currentTick) || currentTick < nextPendingRefreshTick)
            {
                return;
            }

            tmpPawnsToProcess.Clear();
            nextPendingRefreshTick = int.MaxValue;
            foreach (KeyValuePair<Pawn, PendingRefresh> entry in pendingRefreshes)
            {
                Pawn pawn = entry.Key;
                if (!MountedPawnUtility.IsMugirl(pawn) || pawn.Destroyed || currentTick >= entry.Value.nextTick)
                {
                    tmpPawnsToProcess.Add(pawn);
                }
                else if (entry.Value.nextTick < nextPendingRefreshTick)
                {
                    nextPendingRefreshTick = entry.Value.nextTick;
                }
            }

            for (int i = 0; i < tmpPawnsToProcess.Count; i++)
            {
                Pawn pawn = tmpPawnsToProcess[i];
                if (!pendingRefreshes.TryGetValue(pawn, out PendingRefresh refresh))
                {
                    continue;
                }

                if (!MountedPawnUtility.IsMugirl(pawn) || pawn.Destroyed)
                {
                    pendingRefreshes.Remove(pawn);
                    continue;
                }

                // 只保留初始化后的首次延后刷新与 300 tick 最终兜底。
                // 先更新调度再触发渲染，允许外部刷新通知安全地重新排队。
                bool refreshDue = currentTick >= refresh.nextTick;
                if (refreshDue && currentTick >= refresh.finalTick)
                {
                    pendingRefreshes.Remove(pawn);
                }
                else
                {
                    if (refreshDue)
                    {
                        refresh.nextTick = refresh.finalTick;
                        pendingRefreshes[pawn] = refresh;
                    }
                    if (refresh.nextTick < nextPendingRefreshTick)
                    {
                        nextPendingRefreshTick = refresh.nextTick;
                    }
                }

                if (refreshDue)
                {
                    RefreshGraphics(pawn);
                }
            }

            tmpPawnsToProcess.Clear();
        }

        public static void ClearPendingRefreshes()
        {
            pendingRefreshes.Clear();
            tmpPawnsToProcess.Clear();
            nextPendingRefreshTick = int.MaxValue;
        }

        private static void RefreshGraphics(Pawn pawn)
        {
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }

    public static class GhoulRenderingRefreshUtility
    {
        public static void ClearPendingRefreshes()
        {
            PawnRenderingRefreshUtility.ClearPendingRefreshes();
        }
    }
}
