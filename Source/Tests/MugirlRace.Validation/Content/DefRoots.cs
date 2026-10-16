using Verse;

namespace MugirlRace.Validation.Content;

/// <summary>Def 类所属的 DefDatabase（游戏 <c>GenDefDatabase.AllDefTypesWithDatabases</c> 的规则）。</summary>
internal static class DefRoots
{
    /// <summary>
    /// <paramref name="type"/> 所在 DefDatabase 的类型：<c>Verse.Def</c> 之下最上层的非抽象祖先（游戏
    /// <c>GenDefDatabase.AllDefTypesWithDatabases</c> 的规则；ThingDef_AlienRace → ThingDef）。存档语料按它记录 Def。
    /// </summary>
    public static Type Database(Type type)
    {
        Type root = type;
        for (Type? current = type; current is not null && current != typeof(Def); current = current.BaseType)
        {
            if (!current.IsAbstract)
            {
                root = current;
            }
        }

        return root;
    }
}
