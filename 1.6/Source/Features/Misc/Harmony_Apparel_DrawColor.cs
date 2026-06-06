using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MooGirl
{
    [HarmonyPatch(typeof(Apparel), nameof(Apparel.DrawColor), MethodType.Getter)]
    public static class Harmony_Apparel_DrawColor
    {
        public static bool Prefix(Apparel __instance, ref Color __result)
        {
            if (__instance?.def?.defName != "MooGirl_Spacesuit")
            {
                return true;
            }

            Color color = __instance.def.graphicData?.color ?? Color.white;
            if (__instance.WornByCorpse)
            {
                color = PawnRenderUtility.GetRottenColor(color);
            }

            Pawn wearer = __instance.Wearer;
            if (wearer?.Drawer?.renderer?.StatueColor is Color statueColor)
            {
                color = statueColor;
            }

            __result = color;
            return false;
        }
    }
}
