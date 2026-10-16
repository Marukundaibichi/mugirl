using System;
using System.Linq;
using System.Reflection;
using Verse;

namespace Mugirl
{
    internal static class CorporatePricingCompatibility
    {
        internal static bool HasOpaqueBehavior(ThingDef def) => OpaqueBehaviorReason(def) != null;

        // 按原版回调职责判断，不因私有辅助方法恰好以 Post/Pre 开头就限制供货。
        // 显示回调允许参与正常估价；生命周期、交互、产出和 tick 回调仍保守限制。
        // 不执行第三方方法来试探产出，也不把所有近战武器列为例外。
        internal static string OpaqueBehaviorReason(ThingDef def)
        {
            Assembly game = typeof(Thing).Assembly;
            if (def.thingClass != null && def.thingClass.Assembly != game) return def.thingClass.FullName;
            foreach (CompProperties props in def.comps ?? Enumerable.Empty<CompProperties>())
            {
                Type type = props?.compClass;
                if (type == null) return typeof(CompProperties).FullName + ".compClass=null";
                if (type.Assembly == game) continue;
                for (Type current = type; current != null && current.Assembly != game; current = current.BaseType)
                    foreach (MethodInfo method in current.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        MethodInfo root = method.GetBaseDefinition();
                        bool gameCallback = root.DeclaringType?.Assembly == game
                            && typeof(ThingComp).IsAssignableFrom(root.DeclaringType);
                        if (gameCallback && !IsPresentationCallback(root)
                            || method.Name == "DoEffect" || method.Name == "DoEffectOn")
                            return type.FullName + " -> " + current.FullName + "." + method.Name;
                    }
            }
            return null;
        }

        private static bool IsPresentationCallback(MethodInfo method)
        {
            if (method.DeclaringType != typeof(ThingComp)) return false;
            switch (method.Name)
            {
                case nameof(ThingComp.PostDraw):
                case nameof(ThingComp.PostDrawExtraSelectionOverlays):
                case nameof(ThingComp.DrawAt):
                case nameof(ThingComp.DrawGUIOverlay):
                case nameof(ThingComp.CompDrawWornExtras):
                case nameof(ThingComp.PostPrintOnto):
                case nameof(ThingComp.CompPrintForPowerGrid):
                case nameof(ThingComp.CompInspectStringExtra):
                case nameof(ThingComp.CompTipStringExtra):
                case nameof(ThingComp.GetDescriptionPart):
                case nameof(ThingComp.SpecialDisplayStats):
                case nameof(ThingComp.TransformLabel):
                case nameof(ThingComp.ForceColor):
                case nameof(ThingComp.DontDrawParent):
                case nameof(ThingComp.GetStatsExplanation):
                case nameof(ThingComp.Notify_ColorChanged):
                    return true;
                default:
                    return false;
            }
        }
    }
}
