using HarmonyLib;
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

    [HarmonyPatch(typeof(Hediff), nameof(Hediff.Tick))]
    public static class Harmony_GhoulRenderingRefresh_Tick
    {
        public static void Postfix(Hediff __instance)
        {
            if (__instance?.def?.defName != "Ghoul")
            {
                return;
            }

            GhoulRenderingRefreshUtility.RefreshIfNeeded(__instance);
        }
    }

    public static class GhoulRenderingRefreshUtility
    {
        public static void NotifyGhoulChanged(Pawn pawn)
        {
            if (!MountedPawnUtility.IsMooGirl(pawn))
            {
                return;
            }

            RefreshGraphics(pawn);
        }

        public static void RefreshIfNeeded(Hediff hediff)
        {
            Pawn pawn = hediff?.pawn;
            if (!MountedPawnUtility.IsMooGirl(pawn) || hediff.ageTicks > 300 || !pawn.IsHashIntervalTick(30))
            {
                return;
            }

            RefreshGraphics(pawn);
        }

        private static void RefreshGraphics(Pawn pawn)
        {
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }
}
