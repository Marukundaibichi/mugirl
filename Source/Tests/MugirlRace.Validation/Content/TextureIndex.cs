using System.IO.Compression;

namespace MugirlRace.Validation.Content;

/// <summary>
/// <c>ContentFinder&lt;Texture2D&gt;</c> 能找到的贴图路径：每个启用内容包的 <c>Textures/</c> 目录与已烘焙的 <c>Assets.zip</c>
/// （区分大小写，同 <c>ModContentHolder</c>），然后是根 Assets.zip 中的本体资源（<c>Resources/Textures/</c>，先精确匹配再忽略
/// 大小写，同 Waylith 的 <c>ResourceFiles</c>）。
/// </summary>
internal sealed class TextureIndex
{
    private static readonly string[] TextureExtensions = [".png", ".jpg", ".jpeg", ".psd", ".dds"];

    // 贴图路径 -> 最小配置中是否可用。
    private readonly Dictionary<string, bool> content = new(StringComparer.Ordinal);
    private readonly HashSet<string> contentIgnoreCase = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> resources = new(StringComparer.Ordinal);
    private readonly HashSet<string> resourcesIgnoreCase = new(StringComparer.OrdinalIgnoreCase);

    private TextureIndex()
    {
    }

    public int Count => content.Count + resources.Count;

    public static TextureIndex Build(ModCatalog catalog, string gameDir)
    {
        var index = new TextureIndex();
        foreach (ContentPack pack in catalog.LoadOrder)
        {
            IReadOnlyList<string> minimalFolders = catalog.Minimal.IsActive(pack.PackageId) ? catalog.FoldersFor(pack, catalog.Minimal) : [];
            foreach (string folder in catalog.FoldersFor(pack, catalog.Full))
            {
                bool inMinimal = minimalFolders.Contains(folder, StringComparer.OrdinalIgnoreCase);
                string textures = Path.Combine(folder, "Textures");
                if (Directory.Exists(textures))
                {
                    foreach (string file in Directory.EnumerateFiles(textures, "*", SearchOption.AllDirectories))
                    {
                        if (TextureExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                        {
                            index.AddContent(Path.ChangeExtension(Path.GetRelativePath(textures, file), null).Replace('\\', '/'), inMinimal);
                        }
                    }
                }

                string assetPack = Path.Combine(folder, "Assets.zip");
                if (File.Exists(assetPack))
                {
                    foreach (string entry in ZipEntries(assetPack, "Textures/"))
                    {
                        index.AddContent(entry, inMinimal);
                    }
                }
            }
        }

        string baseResources = Path.Combine(gameDir, "Assets.zip");
        if (File.Exists(baseResources))
        {
            foreach (string entry in ZipEntries(baseResources, "Resources/Textures/"))
            {
                index.resources.Add(entry);
                index.resourcesIgnoreCase.Add(entry);
            }
        }

        return index;
    }

    /// <summary>查找一个贴图路径。</summary>
    public TextureMatch Find(string path)
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

        // 1.7 按区分大小写查找 Mod 贴图（Windows 文件系统不区分，所以这类错误在资源管理器里看不出来）。
        return contentIgnoreCase.Contains(path) ? TextureMatch.ContentCaseMismatch : TextureMatch.Missing;
    }

    /// <summary><paramref name="folder"/> 下至少有一张贴图时为 true（<c>GetAllInFolder</c>）。</summary>
    public bool FolderHasTextures(string folder)
    {
        string prefix = folder.TrimEnd('/') + "/";
        return content.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal))
            || resources.Any(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private void AddContent(string path, bool inMinimal)
    {
        content[path] = content.GetValueOrDefault(path) || inMinimal;
        contentIgnoreCase.Add(path);
    }

    // 形如 "Textures/Things/Foo_north.psd.dds" 的条目：去掉 ".dds" 与原扩展名。
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
            while (TextureExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                path = path[..^Path.GetExtension(path).Length];
            }

            yield return path;
        }
    }
}
