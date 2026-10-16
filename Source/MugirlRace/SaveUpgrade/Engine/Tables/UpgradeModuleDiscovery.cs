using System.Linq;
using System.Reflection;

namespace Mugirl.SaveUpgrade;

/// <summary>在本程序集中发现标注 <see cref="SaveUpgradeModuleAttribute"/> 的模块，按 (顺序, 名称) 排序；与反射顺序无关。</summary>
internal static class UpgradeModuleDiscovery
{
    internal static IReadOnlyList<UpgradeModuleRegistration> Discover(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        List<UpgradeModuleRegistration> modules = [];
        List<string> problems = [];
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            // 依赖可选程序集（HAR 等）的类型在离线验证与单元测试中可能加载失败；升级模块不依赖它们，照常发现其余类型。
            types = exception.Types;
        }

        foreach (Type? type in types)
        {
            if (type?.GetCustomAttribute<SaveUpgradeModuleAttribute>(inherit: false) is not { } attribute)
            {
                continue;
            }

            if (type.IsAbstract || !typeof(ISaveUpgradeModule).IsAssignableFrom(type))
            {
                problems.Add(type.FullName + " carries [SaveUpgradeModule] but is not a concrete ISaveUpgradeModule.");
                continue;
            }

            if (type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) is not { } constructor)
            {
                problems.Add(type.FullName + " needs a parameterless constructor.");
                continue;
            }

            modules.Add(new UpgradeModuleRegistration(attribute.Name, attribute.Order, (ISaveUpgradeModule)constructor.Invoke(null)));
        }

        if (problems.Count > 0)
        {
            throw new UpgradeTableException(problems);
        }

        return [.. modules.OrderBy(m => m.Order).ThenBy(m => m.Name, StringComparer.Ordinal)];
    }
}
