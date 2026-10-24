using System.Collections.Concurrent;
using System.Reflection;

namespace Mugirl.Features.Corporate;

/// <summary>
/// 巨企供货：识别通用估价无法覆盖的明确产出入口。
/// </summary>
/// <remarks>
/// 存档、初始化、tick、显示、战斗和通用使用回调本身不是产出证据，不能据此禁购整批被注入组件的物品。
/// 不执行第三方代码来试探效果，也不声称能静态证明任意脚本没有额外收益。Mugirl 自己的组件适用相同规则。
/// </remarks>
internal static class CorporateForeignBehavior
{
    private const BindingFlags DeclaredMethods = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly Assembly GameAssembly = typeof(Thing).Assembly;

    // StaticCacheLifecycle: 进程级；类型与方法表在进程内不变，按 compClass 缓存结果（null 表示无限制行为），
    // 不持有 Def 或游戏对象，无需清理。同一组件类被许多 ThingDef 共用时不再重复反射。
    private static readonly ConcurrentDictionary<Type, string?> compReasons = new();

    // StaticCacheLifecycle: 进程级；只缓存类型的产出入口，不持有 Def 或游戏对象。
    private static readonly ConcurrentDictionary<Type, string?> thingReasons = new();

    /// <summary>限制原因（类型或方法的全名）；没有需要限制的行为时为 null。</summary>
    internal static string? OpaqueBehaviorReason(ThingDef def)
    {
        if (def.thingClass is not null && thingReasons.GetOrAdd(def.thingClass, ComputeThingReason) is { } thingReason)
        {
            return thingReason;
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

            if (OpaqueCompReason(compClass) is { } reason)
            {
                return reason;
            }
        }

        return null;
    }

    /// <summary>组件类的明确产出限制原因；结果按类型缓存。</summary>
    internal static string? OpaqueCompReason(Type compClass) => compReasons.GetOrAdd(compClass, ComputeCompReason);

    private static string? ComputeCompReason(Type compClass)
    {
        // 已知生产器和它们的模组子类都保留限制；不依据命名空间或模组名单放行。
        if (typeof(CompSpawner).IsAssignableFrom(compClass)
            || typeof(CompSpawnerItems).IsAssignableFrom(compClass)
            || typeof(CompHatcher).IsAssignableFrom(compClass)
            || typeof(CompUseEffect_GiveQuest).IsAssignableFrom(compClass))
        {
            return compClass.FullName;
        }

        return ForeignProductionReason(compClass, typeof(ThingComp));
    }

    private static string? ComputeThingReason(Type thingClass)
        => ForeignProductionReason(thingClass, typeof(Thing));

    private static string? ForeignProductionReason(Type type, Type callbackOwner)
    {
        if (type.Assembly == GameAssembly)
        {
            return null;
        }

        try
        {
            for (Type? current = type; current is not null && current.Assembly != GameAssembly; current = current.BaseType)
            {
                foreach (MethodInfo method in current.GetMethods(DeclaredMethods))
                {
                    bool production = callbackOwner == typeof(ThingComp)
                        ? method.Name is nameof(ThingComp.GetAdditionalLeavings) or nameof(ThingComp.GetAdditionalHarvestYield)
                        : method.Name is nameof(Thing.ButcherProducts) or nameof(Thing.SmeltProducts);
                    // 只解析明确产出入口的覆写关系；同名辅助方法不算，隐藏方法也不能掩盖基类的真实覆写。
                    if (production && method.GetBaseDefinition().DeclaringType == callbackOwner)
                    {
                        return type.FullName + " -> " + current.FullName + "." + method.Name;
                    }
                }
            }

            return null;
        }
        catch (Exception)
        {
            // 反射失败只是无法确认，不能当作物品会产出资源的证据。
            // 真正无法生成的货物仍由订单工厂拒绝，且不会扣款。
            return null;
        }
    }
}
