using System.Linq;
using System.Threading;

namespace Mugirl.SaveUpgrade;

/// <summary>游戏内的 <see cref="IDefCatalog"/>：读档时全部 Def 已加载完毕，直接查 DefDatabase。</summary>
internal sealed class DefDatabaseCatalog : IDefCatalog
{
    private readonly Lazy<Dictionary<string, Type>> roots = new(BuildRoots, LazyThreadSafetyMode.ExecutionAndPublication);

    private DefDatabaseCatalog()
    {
    }

    // StaticCacheLifecycle: 进程级无状态访问器；根类型表在首次使用时建立，Def 加载完成后不再变化。
    internal static DefDatabaseCatalog Instance { get; } = new();

    public bool Contains(string root, string defName) =>
        roots.Value.TryGetValue(root, out Type? type) && GenDefDatabase.GetDefSilentFail(type, defName, specialCaseForSoundDefs: false) is not null;

    public int? IntegerField(string root, string defName, string field)
    {
        if (!roots.Value.TryGetValue(root, out Type? type)
            || GenDefDatabase.GetDefSilentFail(type, defName, specialCaseForSoundDefs: false) is not { } definition)
        {
            return null;
        }

        return definition.GetType().GetField(field)?.GetValue(definition) is int value ? value : null;
    }

    public IReadOnlyCollection<string>? CompClasses(string root, string defName)
    {
        switch (root)
        {
            case "ThingDef":
                return DefDatabase<ThingDef>.GetNamedSilentFail(defName) is { } thingDef
                    ? [.. thingDef.comps.Where(c => c?.compClass is not null).Select(c => SaveTypeNames.Of(c.compClass))]
                    : null;
            case "HediffDef":
                return DefDatabase<HediffDef>.GetNamedSilentFail(defName) is { } hediffDef
                    ? [.. (hediffDef.comps ?? []).Where(c => c?.compClass is not null).Select(c => SaveTypeNames.Of(c.compClass))]
                    : null;
            default:
                return null;
        }
    }

    private static Dictionary<string, Type> BuildRoots()
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (Type type in GenDefDatabase.AllDefTypesWithDatabases())
        {
            map.TryAdd(type.Name, type);
        }

        return map;
    }
}
