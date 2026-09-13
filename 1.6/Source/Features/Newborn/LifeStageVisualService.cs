using HarmonyLib;
using RimWorld;
using System.Reflection;
using Verse;

namespace Mugirl
{
    internal static class LifeStageVisualService
    {
        private const string TeenagerLifeStageDefName = "Mugirl_Teenager";

        public static bool NormalizeBodyType(Pawn pawn, bool queueRenderRefresh = false)
        {
            if (!ModsConfig.BiotechActive || pawn?.story == null || !MugirlIdentity.HasMugirlBody(pawn))
            {
                return false;
            }

            BodyTypeDef expectedBodyType = null;
            if (pawn.DevelopmentalStage.Baby() || pawn.DevelopmentalStage.Newborn())
            {
                expectedBodyType = BodyTypeDefOf.Baby;
            }
            else if (IsTeenagerLifeStage(pawn))
            {
                expectedBodyType = BodyTypeDefOf.Female;
            }
            else if (pawn.DevelopmentalStage.Child())
            {
                expectedBodyType = BodyTypeDefOf.Child;
            }
            else if (pawn.DevelopmentalStage.Adult())
            {
                expectedBodyType = BodyTypeDefOf.Female;
            }

            if (expectedBodyType == null)
            {
                return false;
            }

            if (pawn.story.bodyType == expectedBodyType)
            {
                return false;
            }

            pawn.story.bodyType = expectedBodyType;
            RefreshVisuals(pawn, queueRenderRefresh);
            return true;
        }

        public static void RefreshVisuals(Pawn pawn, bool queueRenderRefresh = false)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            if (queueRenderRefresh && MountedPawnUtility.IsMugirl(pawn))
            {
                // NotifyPawnChanged 本身会立即刷新，避免同一调用重复标记渲染树。
                PawnRenderingRefreshUtility.NotifyPawnChanged(pawn);
            }
            else
            {
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }
            PortraitsCache.SetDirty(pawn);
        }

        public static int NormalizeLoadedPawns()
        {
            int changed = 0;
            var pawns = PawnsFinder.AllMapsWorldAndTemporary_Alive;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (NormalizeBodyType(pawns[i]))
                {
                    changed++;
                }
            }

            return changed;
        }

        private static bool IsTeenagerLifeStage(Pawn pawn)
        {
            return pawn?.ageTracker?.CurLifeStage?.defName == TeenagerLifeStageDefName;
        }
    }

    [HarmonyPatch(typeof(Pawn_AgeTracker), "RecalculateLifeStageIndex")]
    internal static class Harmony_PawnAgeTracker_RecalculateLifeStageIndex_MugirlBodyType
    {
        // StaticCacheLifecycle: 进程级 Pawn_AgeTracker.pawn 反射缓存；不持有游戏对象。
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_AgeTracker), "pawn");

        public static void Postfix(Pawn_AgeTracker __instance)
        {
            Pawn pawn = PawnField?.GetValue(__instance) as Pawn;
            LifeStageVisualService.NormalizeBodyType(pawn);
        }
    }
}
