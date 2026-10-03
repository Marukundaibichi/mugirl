using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl
{
    // 固色服装不进入造型台的服装染色列表。原版 Dialog_StylingStation 只按 CompColorable 过滤，
    // 列表在构造函数与 Reset 两个入口填充，两处填充后统一剔除固色成员；
    // 染色 Tab 不再显示这些服装，ApplyApparelColors 也不会再给它们写 DesiredColor。
    [HarmonyPatch(typeof(Dialog_StylingStation), MethodType.Constructor, new Type[] { typeof(Pawn), typeof(Thing) })]
    public static class Harmony_StylingStationFixedColor_Ctor
    {
        public static void Postfix(Dialog_StylingStation __instance)
        {
            MugirlFixedColorUtility.RemoveFixedFromStylingStation(__instance);
        }
    }

    [HarmonyPatch(typeof(Dialog_StylingStation), "Reset")]
    public static class Harmony_StylingStationFixedColor_Reset
    {
        public static void Postfix(Dialog_StylingStation __instance)
        {
            MugirlFixedColorUtility.RemoveFixedFromStylingStation(__instance);
        }
    }
}
