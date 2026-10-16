using System.IO.Compression;

namespace MugirlRace.Validation.Content;

/// <summary>
/// <c>ContentFinder&lt;AudioClip&gt;</c> 能找到的音频路径：每个启用内容包的 <c>Sounds/</c> 目录与已烘焙的 <c>Assets.zip</c>（区分
/// 大小写），然后是根 Assets.zip 中的本体资源（<c>Resources/Sounds/</c>，先精确匹配再忽略大小写）。结构与 <see cref="TextureIndex"/>
/// 相同。
/// </summary>
internal sealed class SoundIndex
{
    private static readonly string[] SoundExtensions = [".wav", ".mp3", ".ogg", ".xm", ".it", ".mod", ".s3m"];

    // 音频路径 -> 最小配置中是否可用。
    private readonly Dictionary<string, bool> content = new(StringComparer.Ordinal);
    private readonly HashSet<string> contentIgnoreCase = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> resources = new(StringComparer.Ordinal);
    private readonly HashSet<string> resourcesIgnoreCase = new(StringComparer.OrdinalIgnoreCase);

    private SoundIndex()
    {
    }

    public int Count => content.Count + resources.Count;

    public static SoundIndex Build(ModCatalog catalog, string gameDir)
    {
        var index = new SoundIndex();
        foreach (ContentPack pack in catalog.LoadOrder)
        {
            IReadOnlyList<string> minimalFolders = catalog.Minimal.IsActive(pack.PackageId) ? catalog.FoldersFor(pack, catalog.Minimal) : [];
            foreach (string folder in catalog.FoldersFor(pack, catalog.Full))
            {
                bool inMinimal = minimalFolders.Contains(folder, StringComparer.OrdinalIgnoreCase);
                string sounds = Path.Combine(folder, "Sounds");
                if (Directory.Exists(sounds))
                {
                    foreach (string file in Directory.EnumerateFiles(sounds, "*", SearchOption.AllDirectories))
                    {
                        if (SoundExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                        {
                            index.AddContent(Path.ChangeExtension(Path.GetRelativePath(sounds, file), null).Replace('\\', '/'), inMinimal);
                        }
                    }
                }

                string assetPack = Path.Combine(folder, "Assets.zip");
                if (File.Exists(assetPack))
                {
                    foreach (string entry in ZipEntries(assetPack, "Sounds/"))
                    {
                        index.AddContent(entry, inMinimal);
                    }
                }
            }
        }

        string baseResources = Path.Combine(gameDir, "Assets.zip");
        if (File.Exists(baseResources))
        {
            foreach (string entry in ZipEntries(baseResources, "Resources/Sounds/"))
            {
                index.resources.Add(entry);
                index.resourcesIgnoreCase.Add(entry);
            }
        }

        return index;
    }

    /// <summary>查找一个音频文件（<c>clipPath</c>）。</summary>
    public TextureMatch FindClip(string path)
    {
        if (content.TryGetValue(path, out bool inMinimal))
        {
            return inMinimal ? TextureMatch.Found : TextureMatch.OptionalContentOnly;
        }

        if (resources.Contains(path))
        {
            return TextureMatch.Found;
        }

        if (resourcesIgnoreCase.Contains(path))
        {
            return TextureMatch.CaseMismatch;
        }

        return contentIgnoreCase.Contains(path) ? TextureMatch.ContentCaseMismatch : TextureMatch.Missing;
    }

    /// <summary>查找音频目录（<c>clipFolderPath</c>）：其下至少有一个文件。</summary>
    public TextureMatch FindFolder(string folder)
    {
        string prefix = folder.TrimEnd('/') + "/";
        if (content.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal)) || resources.Any(key => key.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return TextureMatch.Found;
        }

        if (resources.Any(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return TextureMatch.CaseMismatch;
        }

        return content.Keys.Any(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ? TextureMatch.ContentCaseMismatch : TextureMatch.Missing;
    }

    private void AddContent(string path, bool inMinimal)
    {
        content[path] = content.GetValueOrDefault(path) || inMinimal;
        contentIgnoreCase.Add(path);
    }

    private static IEnumerable<string> ZipEntries(string zipFile, string prefix)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipFile);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.EndsWith('/'))
            {
                continue;
            }

            string path = name[prefix.Length..];
            while (SoundExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                path = path[..^Path.GetExtension(path).Length];
            }

            yield return path;
        }
    }
}
