using System.Collections.Concurrent;
using System.Reflection;

namespace Mugirl.Features.Corporate;

/// <summary>
/// 巨企供货：判断物品是否带有来自其他程序集、且会参与生命周期、交互、产出或 tick 的行为，这类物品不能自动估价供货。
/// </summary>
/// <remarks>
/// 按原版回调职责判断，不因私有辅助方法恰好以 Post/Pre 开头就限制供货；显示回调允许参与正常估价。
/// 不执行第三方方法来试探产出，也不把所有近战武器列为例外。Mugirl 自己的组件同样按“其他程序集”处理。
/// </remarks>
internal static class CorporateForeignBehavior
{
    private const BindingFlags DeclaredMethods = BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly Assembly GameAssembly = typeof(Thing).Assembly;

    // StaticCacheLifecycle: 进程级；类型与方法表在进程内不变，按 compClass 缓存结果（null 表示无限制行为），
    // 不持有 Def 或游戏对象，无需清理。同一组件类被许多 ThingDef 共用时不再重复反射。
    private static readonly ConcurrentDictionary<Type, string?> compReasons = new();

    /// <summary>限制原因（类型或方法的全名）；没有需要限制的行为时为 null。</summary>
    internal static string? OpaqueBehaviorReason(ThingDef def)
    {
        if (def.thingClass is not null && def.thingClass.Assembly != GameAssembly)
        {
            return def.thingClass.FullName;
        }

        List<CompProperties>? comps = def.comps;
        if (comps is null)
        {
            return null;
        }

        for (int i = 0; i < comps.Count; i++)
        {
            Type? compClass = comps[i]?.compClass;
            if (compClass is null)
            {
                return typeof(CompProperties).FullName + ".compClass=null";
            }

            if (compClass.Assembly == GameAssembly)
            {
                continue;
            }

            if (OpaqueCompReason(compClass) is { } reason)
            {
                return reason;
            }
        }

        return null;
    }

    /// <summary>一个来自其他程序集的组件类的限制原因；结果按类型缓存。</summary>
    internal static string? OpaqueCompReason(Type compClass) => compReasons.GetOrAdd(compClass, ComputeCompReason);

    private static string? ComputeCompReason(Type compClass)
    {
        try
        {
            for (Type? current = compClass; current is not null && current.Assembly != GameAssembly; current = current.BaseType)
            {
                foreach (MethodInfo method in current.GetMethods(DeclaredMethods))
                {
                    MethodInfo root = method.GetBaseDefinition();
                    bool gameCallback = root.DeclaringType?.Assembly == GameAssembly
                        && typeof(ThingComp).IsAssignableFrom(root.DeclaringType);
                    if ((gameCallback && !IsPresentationCallback(root))
                        || method.Name is "DoEffect" or "DoEffectOn")
                    {
                        return compClass.FullName + " -> " + current.FullName + "." + method.Name;
                    }
                }
            }

            return null;
        }
        catch (Exception exception)
        {
            // 1.7 通过版本回退加载的 1.6 程序集里，签名引用已删除类型的方法在反射时会抛异常。
            // 无法检查的类型按“有不透明行为”处理（保守限制供货），不让异常中断整个供货分析。
            return compClass.FullName + " -> " + exception.GetType().Name;
        }
    }

    private static bool IsPresentationCallback(MethodInfo method)
    {
        if (method.DeclaringType != typeof(ThingComp))
        {
            return false;
        }

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
