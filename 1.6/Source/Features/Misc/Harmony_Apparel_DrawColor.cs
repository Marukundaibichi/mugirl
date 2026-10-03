using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    // 打上 MugirlFixedColor 标签（apparel.tags）的服装固定使用 def 自带颜色（未配置 graphicData.color 时为白色），
    // 材质色、造型台染色与 DesiredColor 都不再影响穿着与掉落渲染；雕像色与尸体腐烂色仍按原样叠加。
    // 造型台染色入口的剔除见 Harmony_StylingStationFixedColor。
    [HarmonyPatch(typeof(Apparel), nameof(Apparel.DrawColor), MethodType.Getter)]
    public static class Harmony_Apparel_DrawColor
    {
        public static bool Prefix(Apparel __instance, ref Color __result)
        {
            if (!MugirlFixedColorUtility.IsFixedColor(__instance))
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
