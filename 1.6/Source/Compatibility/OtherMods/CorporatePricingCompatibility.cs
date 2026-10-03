using System;
using System.Linq;
using System.Reflection;
using Verse;

namespace Mugirl
{
    internal static class CorporatePricingCompatibility
    {
        // 不执行第三方回调来试开箱。只有纯显示组件可以直接沿用标准估价；
        // 第三方使用、生成、销毁、交互或 tick 行为不能证明产出上限，禁止自动复制。
        internal static bool HasOpaqueBehavior(ThingDef def)
        {
            Assembly game = typeof(Thing).Assembly;
            if (def.thingClass != null && def.thingClass.Assembly != game) return true;
            foreach (CompProperties props in def.comps ?? Enumerable.Empty<CompProperties>())
            {
                Type type = props.compClass;
                if (type == null) return true;
                if (type.Assembly == game) continue;
                for (Type current = type; current != null && current.Assembly != game; current = current.BaseType)
                    foreach (MethodInfo method in current.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        string name = method.Name;
                        if (name.StartsWith("CompTick", StringComparison.Ordinal)
                            || name.StartsWith("Post", StringComparison.Ordinal)
                            || name.StartsWith("Pre", StringComparison.Ordinal)
                            || name.StartsWith("Notify_", StringComparison.Ordinal)
                            || name == "CompGetGizmosExtra" || name == "CompFloatMenuOptions"
                            || name == "DoEffect" || name == "DoEffectOn" || name == "Initialize") return true;
                    }
            }
            return false;
        }
    }
}
