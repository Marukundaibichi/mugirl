namespace MugirlRace.Validation.Content;

/// <summary>
/// 一组启用的 Mod，键的规则同 RimWorld 的 <c>ModLister.*NoSuffix</c>：包 id 不区分大小写，并忽略本地副本的
/// <c>_steam</c>/<c>_copy</c> 后缀。
/// </summary>
internal sealed class ActiveMods
{
    private readonly HashSet<string> packageIds;
    private readonly HashSet<string> names;

    /// <param name="label">配置名，出现在消息中。</param>
    /// <param name="packs">启用的内容包。</param>
    /// <param name="assumedIds">额外视为启用、但本机未安装的包 id（<c>--assume-active</c>）。</param>
    public ActiveMods(string label, IEnumerable<ContentPack> packs, IEnumerable<string>? assumedIds = null)
    {
        Label = label;
        List<ContentPack> list = packs.ToList();
        packageIds = new HashSet<string>(list.Select(pack => pack.PackageId), StringComparer.Ordinal);
        packageIds.UnionWith((assumedIds ?? []).Select(Normalize));
        names = new HashSet<string>(list.Select(pack => pack.Name), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>配置名（<c>full</c> 或 <c>minimal</c>）。</summary>
    public string Label { get; }

    public bool IsActive(string packageId) => packageIds.Contains(Normalize(packageId));

    /// <summary><c>ModLister.HasActiveModWithName</c>，供 <c>PatchOperationFindMod</c> 使用。</summary>
    public bool HasModNamed(string name) => names.Contains(name.Trim());

    /// <summary>小写、去掉 <c>_steam</c> 或 <c>_copy</c> 后缀的包 id。</summary>
    public static string Normalize(string packageId)
    {
        string id = packageId.Trim().ToLowerInvariant();
        foreach (string suffix in new[] { "_steam", "_copy" })
        {
            if (id.EndsWith(suffix, StringComparison.Ordinal))
            {
                return id[..^suffix.Length];
            }
        }

        return id;
    }
}
