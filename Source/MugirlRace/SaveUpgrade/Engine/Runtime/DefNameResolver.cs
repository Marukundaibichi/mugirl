namespace Mugirl.SaveUpgrade;

/// <summary>
/// <c>BackCompatibility.BackCompatibleDefName</c> 与 <c>BackCompatibility.WasDefRemoved</c> 后缀的判定：所有 Scribe_Defs 读取的引用、
/// 玩家的异种/思想体系/剧本文件与第三方 DefInjected 键都经过这里。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>按传入的 defName 参数查表，而不是原版或其他 Mod 转换后的结果；</item>
/// <item>defType 规范化为 DefDatabase 根类型（声明类型可能是子类，<c>TaleData_Def</c> 传运行时类型），根是 <see cref="Def"/> 时拒绝；</item>
/// <item>只有名称在该根数据库中找不到时才介入：第三方用同名重新定义、或其他转换器已给出有效名称时，以它们为准；</item>
/// <item>不受读档会话门控：异种、思想体系、剧本文件和 DefInjected 不经过存档 DOM。</item>
/// </list>
/// </remarks>
internal static class DefNameResolver
{
    /// <summary>应替换当前结果的新名称；不替换时为 null。</summary>
    /// <param name="defName">原方法的 defName 参数（存档或文件中的原文）。</param>
    /// <param name="current">原方法（及更早的后缀）给出的结果。</param>
    /// <param name="exists">(根类型, 名称) 在 DefDatabase 中是否存在。</param>
    internal static string? Resolve(UpgradeTables tables, Type defType, string defName, string? current, Func<Type, string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(exists);
        if (defType is null || defName is null || !tables.TryGetDefRenames(defName, out IReadOnlyDictionary<string, string>? renamesByRoot))
        {
            return null;
        }

        if (DefRoots.DatabaseRoot(defType) is not { } root || !renamesByRoot.TryGetValue(root.Name, out string? renamed))
        {
            return null;
        }

        if (current is not null && exists(root, current))
        {
            return null;
        }

        return renamed;
    }

    /// <summary>
    /// <paramref name="defName"/> 是否是本 Mod 删除的 Def（含隐含 Def）：是时原版 <c>DefFromNode</c> 把残留引用读成 null 而不报错。
    /// 名称在当前根数据库中存在（第三方重新定义）时返回 false。
    /// </summary>
    /// <param name="defType"><c>WasDefRemoved</c> 收到的类型（Scribe_Defs 的声明类型，可能是子类）。</param>
    /// <param name="exists">(根类型, 名称) 在 DefDatabase 中是否存在。</param>
    internal static bool WasDropped(UpgradeTables tables, Type defType, string defName, Func<Type, string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(exists);
        if (defType is null || defName is null || DefRoots.DatabaseRoot(defType) is not { } root)
        {
            return false;
        }

        return tables.IsDroppedDef(root.Name, defName) && !exists(root, defName);
    }
}
