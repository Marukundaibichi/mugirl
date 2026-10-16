using System.Xml;

namespace MugirlRace.Validation.Content;

/// <summary>
/// 解析后的 LoadFolders.xml（<c>Verse.ModLoadFolders.LoadDataFromXmlCustom</c> 的复刻）：版本键为元素名小写并去掉开头的
/// <c>v</c>，目录分隔符规范化，<c>/</c> 表示 Mod 根目录。
/// </summary>
internal sealed class ModLoadFolders
{
    private readonly Dictionary<string, List<LoadFolder>> foldersForVersion = new(StringComparer.Ordinal);

    private ModLoadFolders(string file) => File = file;

    /// <summary>LoadFolders.xml 文件。</summary>
    public string File { get; }

    /// <summary>按文件顺序的版本键（例如 <c>1.6</c>、<c>1.7</c>）。</summary>
    public IReadOnlyList<string> DefinedVersions => foldersForVersion.Keys.ToList();

    public IReadOnlyList<LoadFolder>? FoldersForVersion(string version) => foldersForVersion.GetValueOrDefault(version);

    /// <summary>
    /// 从 <paramref name="modRoot"/> 读取 LoadFolders.xml。游戏按不区分大小写解析文件名（<c>GenFile.ResolveCaseInsensitiveFilePath</c>），
    /// 因此 <c>loadFolders.xml</c> 也算。
    /// </summary>
    public static ModLoadFolders? TryRead(string modRoot)
    {
        if (!Directory.Exists(modRoot))
        {
            return null;
        }

        string? file = Directory.EnumerateFiles(modRoot)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), "LoadFolders.xml", StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            return null;
        }

        var document = new XmlDocument();
        document.Load(file);
        var result = new ModLoadFolders(file);
        XmlElement? root = document.DocumentElement;
        if (root is null)
        {
            return result;
        }

        foreach (XmlElement versionNode in root.ChildNodes.OfType<XmlElement>())
        {
            string version = versionNode.Name.ToLowerInvariant();
            if (version.StartsWith('v'))
            {
                version = version[1..];
            }

            if (!result.foldersForVersion.TryGetValue(version, out List<LoadFolder>? folders))
            {
                folders = [];
                result.foldersForVersion.Add(version, folders);
            }

            foreach (XmlElement item in versionNode.ChildNodes.OfType<XmlElement>())
            {
                string text = item.InnerText;
                string folder = text is "/" or "\\" ? "" : text.Replace('/', Path.DirectorySeparatorChar);
                folders.Add(new LoadFolder(folder, Ids(item, "IfModActive"), Ids(item, "IfModActiveAll"), Ids(item, "IfModNotActive")));
            }
        }

        return result;
    }

    private static List<string>? Ids(XmlElement item, string attribute)
        => item.Attributes[attribute] is { } value ? value.Value.Split(',').Select(id => id.Trim()).ToList() : null;
}
