using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(Apparel), nameof(Apparel.DrawColor), MethodType.Getter)]
    public static class Harmony_Apparel_DrawColor
    {
        public static bool Prefix(Apparel __instance, ref Color __result)
        {
            string defName = __instance?.def?.defName;
            if (defName != "Mugirl_Spacesuit" && defName != "Mugirl_ImmortalFairyAzure")
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
