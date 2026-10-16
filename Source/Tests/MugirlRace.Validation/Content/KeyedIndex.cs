using System.Formats.Tar;
using System.Xml;

namespace MugirlRace.Validation.Content;

/// <summary>
/// 每个启用内容包的 Keyed 翻译（<c>Languages/&lt;language&gt;/Keyed/*.xml</c>，或官方内容使用的 <c>Languages/&lt;language&gt;.tar</c>
/// 中的同样文件），英文与简体中文。
/// </summary>
internal sealed class KeyedIndex
{
    /// <summary>RimWorld 匹配语言目录的名称（完整目录名，然后是 " (" 之前的旧名）。</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Languages = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["English"] = ["English"],
        ["ChineseSimplified"] = ["ChineseSimplified (简体中文)", "ChineseSimplified"],
    };

    private readonly Dictionary<string, Dictionary<string, List<KeyedEntry>>> keys = new(StringComparer.Ordinal);
    private readonly List<string> problems = [];

    private KeyedIndex()
    {
    }

    /// <summary>无法读取的文件。</summary>
    public IReadOnlyList<string> Problems => problems;

    public static KeyedIndex Build(ModCatalog catalog)
    {
        var index = new KeyedIndex();
        foreach (string language in Languages.Keys)
        {
            index.keys[language] = new Dictionary<string, List<KeyedEntry>>(StringComparer.Ordinal);
        }

        foreach (ContentPack pack in catalog.LoadOrder)
        {
            IReadOnlyList<string> minimalFolders = catalog.Minimal.IsActive(pack.PackageId) ? catalog.FoldersFor(pack, catalog.Minimal) : [];

            // 游戏按语言目录内的相对路径去重：同一内容包里，高优先级目录的文件遮蔽低优先级目录中同路径的文件。
            var seen = Languages.Keys.ToDictionary(language => language, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
            foreach (string folder in catalog.FoldersFor(pack, catalog.Full))
            {
                bool inMinimal = minimalFolders.Contains(folder, StringComparer.OrdinalIgnoreCase);
                string languagesDir = Path.Combine(folder, "Languages");
                if (!Directory.Exists(languagesDir))
                {
                    continue;
                }

                foreach ((string language, string[] folderNames) in Languages)
                {
                    string? directory = LanguageDirectory(languagesDir, language);
                    if (directory is not null)
                    {
                        index.ReadDirectory(language, pack, inMinimal, Path.Combine(directory, "Keyed"), seen[language]);
                    }

                    string? tar = folderNames.Select(name => Path.Combine(languagesDir, name + ".tar")).FirstOrDefault(File.Exists);
                    if (tar is not null)
                    {
                        index.ReadTar(language, pack, inMinimal, tar, seen[language]);
                    }
                }
            }
        }

        return index;
    }

    /// <summary>
    /// <c>Languages</c> 目录下 <paramref name="language"/> 的目录：完整目录名，或完整名不存在时的旧名（同 <c>LoadedLanguage</c>）。
    /// 都不存在时为 null。
    /// </summary>
    public static string? LanguageDirectory(string languagesDir, string language)
        => Languages[language].Select(name => Path.Combine(languagesDir, name)).FirstOrDefault(Directory.Exists);

    /// <summary><paramref name="language"/> 中 <paramref name="key"/> 的每一处定义。</summary>
    public IReadOnlyList<KeyedEntry> Find(string language, string key)
        => keys[language].TryGetValue(key, out List<KeyedEntry>? list) ? list : [];

    /// <summary><paramref name="pack"/> 在 <paramref name="language"/> 中定义的键，以及定义每个键的全部文件。</summary>
    public IReadOnlyDictionary<string, List<KeyedEntry>> KeysOf(string language, ContentPack pack)
        => keys[language]
            .Select(pair => (pair.Key, Entries: pair.Value.Where(entry => entry.Pack == pack).ToList()))
            .Where(pair => pair.Entries.Count > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Entries, StringComparer.Ordinal);

    private void ReadDirectory(string language, ContentPack pack, bool inMinimal, string directory, HashSet<string> seen)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*.xml", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (!seen.Add(Path.GetRelativePath(directory, file).Replace('\\', '/')))
            {
                continue;
            }

            try
            {
                using FileStream stream = File.OpenRead(file);
                Read(language, pack, inMinimal, file, stream);
            }
            catch (XmlException exception)
            {
                problems.Add($"{file}: {exception.Message}");
            }
        }
    }

    private void ReadTar(string language, ContentPack pack, bool inMinimal, string tarFile, HashSet<string> seen)
    {
        if (!File.Exists(tarFile))
        {
            return;
        }

        using FileStream stream = File.OpenRead(tarFile);
        using var reader = new TarReader(stream);
        while (reader.GetNextEntry() is { } entry)
        {
            string name = entry.Name.Replace('\\', '/');
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null
                || !name.StartsWith("Keyed/", StringComparison.Ordinal) || !name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                || !seen.Add(name["Keyed/".Length..]))
            {
                continue;
            }

            try
            {
                Read(language, pack, inMinimal, tarFile + "!" + name, entry.DataStream);
            }
            catch (XmlException exception)
            {
                problems.Add($"{tarFile}!{name}: {exception.Message}");
            }
        }
    }

    private void Read(string language, ContentPack pack, bool inMinimal, string file, Stream stream)
    {
        var document = new XmlDocument();
        document.Load(stream);
        foreach (XmlElement element in document.DocumentElement?.ChildNodes.OfType<XmlElement>() ?? [])
        {
            if (!keys[language].TryGetValue(element.Name, out List<KeyedEntry>? list))
            {
                keys[language][element.Name] = list = [];
            }

            list.Add(new KeyedEntry(pack, file, inMinimal, element.InnerText));
        }
    }
}
