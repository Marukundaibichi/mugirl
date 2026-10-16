using System.Collections.Concurrent;

namespace Mugirl.SaveUpgrade;

/// <summary>Def 类型到其 DefDatabase 根类型（如 <c>ThingDef</c>）的规范化。</summary>
/// <remarks>
/// 规则与 <c>GenDefDatabase.AllDefTypesWithDatabases</c> 相同：数据库建在继承链上最靠近 <see cref="Def"/> 的非抽象类型上。
/// 例如 <c>ThingDef_AlienRace</c> → <c>ThingDef</c>（<c>BuildableDef</c> 是抽象类，不是根）。<see cref="Def"/> 本身、
/// 抽象类型和不派生自 <see cref="Def"/> 的类型没有根，返回 null，调用方据此拒绝查表。
/// </remarks>
internal static class DefRoots
{
    // StaticCacheLifecycle: 进程级；类型继承关系在进程内不变，缓存不随存档清理。读档工作线程与主线程都可能写入。
    private static readonly ConcurrentDictionary<Type, Type?> Cache = new();

    /// <summary><paramref name="defType"/> 的 DefDatabase 根类型；没有根时为 null。</summary>
    internal static Type? DatabaseRoot(Type defType)
    {
        ArgumentNullException.ThrowIfNull(defType);
        return Cache.GetOrAdd(defType, static type => FindRoot(type));
    }

    private static Type? FindRoot(Type defType)
    {
        Type? root = null;
        for (Type? current = defType; current is not null; current = current.BaseType)
        {
            if (current == typeof(Def))
            {
                return root;
            }

            if (!current.IsAbstract)
            {
                root = current;
            }
        }

        return null;
    }
}
