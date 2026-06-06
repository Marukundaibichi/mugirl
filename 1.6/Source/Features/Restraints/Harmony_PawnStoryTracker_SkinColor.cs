using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MooGirl
{
    [HarmonyPatch(typeof(Pawn_StoryTracker), nameof(Pawn_StoryTracker.SkinColor), MethodType.Getter)]
    public static class Patch_PawnStoryTracker_SkinColor
    {
        private static readonly Color MooGirlSkinColor =
            new Color32(255, 241, 231, 255);

        public static void Postfix(
            Pawn_StoryTracker __instance,
            Pawn ___pawn,
            ref Color __result)
        {
            if (MooGirlIdentity.IsMooGirlPawn(___pawn))
            {
                __result = MooGirlSkinColor;
            }
        }
    }
}
