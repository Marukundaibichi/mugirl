using System.Xml;

namespace MugirlRace.Validation.Content;

/// <summary>RimWorld 眼中的一个 Mod 或官方内容目录：身份来自 About/About.xml，另有其 LoadFolders.xml。</summary>
internal sealed class ContentPack
{
    private ContentPack(string rootDir, string packageId, string name, bool isOfficial)
    {
        RootDir = rootDir;
        PackageId = packageId;
        Name = name;
        IsOfficial = isOfficial;
    }

    /// <summary>规范化的包 id（见 <see cref="ActiveMods.Normalize"/>）。</summary>
    public string PackageId { get; }

    /// <summary>显示名（About.xml 的 <c>name</c>），供 <c>PatchOperationFindMod</c> 使用。</summary>
    public string Name { get; }

    public string RootDir { get; }

    /// <summary>Core 或 DLC（Data/&lt;name&gt;）。</summary>
    public bool IsOfficial { get; }

    /// <summary>被验证的 Mod。</summary>
    public bool IsUnderTest { get; private init; }

    /// <summary>不是硬依赖、因 <c>--assume-active</c> 才加入加载顺序的可选整合 Mod。</summary>
    public bool IsAssumedActive { get; init; }

    /// <summary><c>modDependencies</c>（或游戏版本对应的 <c>modDependenciesByVersion</c>）中规范化的包 id。</summary>
    public IReadOnlyList<string> Dependencies { get; private init; } = [];

    /// <summary><c>loadAfter</c>（或 <c>loadAfterByVersion</c>）中规范化的包 id。</summary>
    public IReadOnlyList<string> LoadAfter { get; private init; } = [];

    /// <summary><c>supportedVersions</c> 的条目。</summary>
    public IReadOnlyList<string> SupportedVersions { get; private init; } = [];

    public ModLoadFolders? LoadFolders { get; private init; }

    public override string ToString() => PackageId;

    /// <summary>读取 <paramref name="rootDir"/>/About/About.xml；不是 Mod 目录时返回 <see langword="null"/>。</summary>
    public static ContentPack? TryRead(string rootDir, bool isOfficial, bool isUnderTest, string gameMajorMinor)
    {
        string about = Path.Combine(rootDir, "About", "About.xml");
        if (!File.Exists(about))
        {
            return null;
        }

        var document = new XmlDocument();
        document.Load(about);
        XmlElement? meta = document.DocumentElement;
        string? packageId = meta?["packageId"]?.InnerText;
        if (meta is null || string.IsNullOrWhiteSpace(packageId))
        {
            return null;
        }

        return new ContentPack(rootDir, ActiveMods.Normalize(packageId), meta["name"]?.InnerText.Trim() ?? packageId, isOfficial)
        {
            IsUnderTest = isUnderTest,
            Dependencies = VersionedList(meta, "modDependencies", gameMajorMinor, li => li["packageId"]?.InnerText),
            LoadAfter = VersionedList(meta, "loadAfter", gameMajorMinor, li => li.InnerText),
            SupportedVersions = meta["supportedVersions"]?.ChildNodes.OfType<XmlElement>().Select(li => li.InnerText.Trim()).ToList() ?? [],
            LoadFolders = ModLoadFolders.TryRead(rootDir),
        };
    }

    /// <summary>以 <see cref="IsAssumedActive"/> 标记的副本。</summary>
    public ContentPack AsAssumedActive() => new(RootDir, PackageId, Name, IsOfficial)
    {
        IsUnderTest = IsUnderTest,
        IsAssumedActive = true,
        Dependencies = Dependencies,
        LoadAfter = LoadAfter,
        SupportedVersions = SupportedVersions,
        LoadFolders = LoadFolders,
    };

    // 与 ModMetaData 相同：该游戏版本的 "<list>ByVersion/v1.7" 取代 "<list>"。
    private static List<string> VersionedList(XmlElement meta, string listName, string gameMajorMinor, Func<XmlElement, string?> id)
    {
        XmlElement? list = meta[listName + "ByVersion"]?["v" + gameMajorMinor] ?? meta[listName];
        if (list is null)
        {
            return [];
        }

        return list.ChildNodes.OfType<XmlElement>()
            .Select(id)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ActiveMods.Normalize(value!))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
