using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    // 固色服装（apparel.tags 含 MugirlFixedColor）的公共判定与造型台列表清理。
    // 颜色锁定在 Harmony_Apparel_DrawColor；造型台染色入口剔除在 Harmony_StylingStationFixedColor。
    internal static class MugirlFixedColorUtility
    {
        internal const string Tag = "MugirlFixedColor";

        internal static bool IsFixedColor(Apparel apparel)
        {
            List<string> tags = apparel?.def?.apparel?.tags;
            return tags != null && tags.Contains(Tag);
        }

        // StaticCacheLifecycle: 进程级只读反射字段（原版私有字段句柄），启动后不再变化，不引用任何 Thing/Pawn。
        private static readonly FieldInfo StylingStationApparelColorsField =
            AccessTools.Field(typeof(Dialog_StylingStation), "apparelColors");

        // 从造型台的服装染色字典中移除固色成员；原版在构造函数与 Reset 中只按 CompColorable 过滤，
        // 字段名变动时此处安全返回，染色条目重新出现但颜色仍被 DrawColor 补丁锁定。
        internal static void RemoveFixedFromStylingStation(Dialog_StylingStation dialog)
        {
            if (!(StylingStationApparelColorsField?.GetValue(dialog) is Dictionary<Apparel, Color> colors))
            {
                return;
            }

            List<Apparel> removal = null;
            foreach (KeyValuePair<Apparel, Color> pair in colors)
            {
                if (IsFixedColor(pair.Key))
                {
                    if (removal == null)
                    {
                        removal = new List<Apparel>();
                    }
                    removal.Add(pair.Key);
                }
            }
            if (removal == null)
            {
                return;
            }
            for (int i = 0; i < removal.Count; i++)
            {
                colors.Remove(removal[i]);
            }
        }
    }
}
