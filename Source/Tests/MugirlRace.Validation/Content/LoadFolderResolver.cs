namespace MugirlRace.Validation.Content;

/// <summary>
/// RimWorld 1.7 目录与文件选择的复刻（<c>ModContentPack.InitLoadFolders</c> 与 <c>ModContentPack.GetAllFilesForModPreserveOrder</c>），
/// 保证检查的正是游戏会加载的内容。
/// </summary>
internal static class LoadFolderResolver
{
    /// <summary>
    /// 游戏为 <paramref name="pack"/> 加载的目录，优先级高的在前（<c>foldersToLoadDescendingOrder</c>）。
    /// </summary>
    /// <param name="pack">Mod。</param>
    /// <param name="gameVersion">完整游戏版本，例如 <c>1.7.4977</c>。</param>
    /// <param name="active">启用的 Mod，用于 <c>IfModActive</c> 一类条件。</param>
    public static IReadOnlyList<string> Resolve(ContentPack pack, Version gameVersion, ActiveMods active)
    {
        var folders = new List<string>();
        ModLoadFolders? loadFolders = pack.LoadFolders;
        if (loadFolders is not null && loadFolders.DefinedVersions.Count > 0)
        {
            // 精确查找用 "Major.Minor.Build"，永远匹配不到 "1.7" 键；回退选择不高于游戏版本的最高已定义版本，按字符串排序（同游戏）。
            IReadOnlyList<LoadFolder>? list = loadFolders.FoldersForVersion(gameVersion.ToString(3));
            if (list is not { Count: > 0 })
            {
                string? best = loadFolders.DefinedVersions
                    .Where(key => key != "default" && key.Contains('.', StringComparison.Ordinal) && TryParse(key, out Version? v) && v <= gameVersion)
                    .OrderByDescending(key => key, StringComparer.Ordinal)
                    .FirstOrDefault();
                list = (best is null ? null : loadFolders.FoldersForVersion(best)) ?? loadFolders.FoldersForVersion("default");
            }

            if (list is not null)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].ShouldLoad(active))
                    {
                        folders.Add(Path.Combine(pack.RootDir, list[i].FolderName));
                    }
                }

                return folders;
            }
        }

        string exact = Path.Combine(pack.RootDir, gameVersion.ToString(2));
        if (Directory.Exists(exact))
        {
            folders.Add(exact);
        }
        else
        {
            var chosen = new Version(0, 0);
            var candidates = new List<Version>();
            foreach (string directory in Directory.EnumerateDirectories(pack.RootDir))
            {
                if (TryParse(Path.GetFileName(directory), out Version? v))
                {
                    candidates.Add(v);
                }
            }

            candidates.Sort();
            foreach (Version item in candidates)
            {
                if ((item > chosen || chosen > gameVersion) && (item <= gameVersion || chosen.Major == 0))
                {
                    chosen = item;
                }
            }

            if (chosen.Major > 0)
            {
                folders.Add(Path.Combine(pack.RootDir, chosen.ToString()));
            }
        }

        string common = Path.Combine(pack.RootDir, "Common");
        if (Directory.Exists(common))
        {
            folders.Add(common);
        }

        folders.Add(pack.RootDir);
        return folders;
    }

    /// <summary>
    /// 每个加载目录下 <c>&lt;folder&gt;/<paramref name="contentPath"/></c> 的文件，按相对加载目录的路径去重：高优先级目录的文件
    /// 遮蔽低优先级目录中相同相对路径的文件（区分大小写，同游戏的 <c>HashSet&lt;string&gt;</c>）。
    /// </summary>
    public static IReadOnlyList<LoadedFile> Files(IReadOnlyList<string> foldersDescending, string contentPath, Func<string, bool>? acceptExtension = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<LoadedFile>();
        foreach (string folder in foldersDescending)
        {
            string directory = Path.Combine(folder, contentPath);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            // DirectXmlLoader/GetAllFilesForModPreserveOrder 保留文件系统枚举顺序，不按名字排序。
            foreach (string file in new DirectoryInfo(directory).GetFiles("*", SearchOption.AllDirectories).Select(f => f.FullName))
            {
                if (acceptExtension is not null && !acceptExtension(Path.GetExtension(file)))
                {
                    continue;
                }

                string relative = Path.GetRelativePath(folder, file);
                if (seen.Add(relative))
                {
                    result.Add(new LoadedFile(folder, relative, file));
                }
            }
        }

        return result;
    }

    /// <summary><c>VersionControl.TryParseVersionString</c>："major.minor[.build]"。</summary>
    public static bool TryParse(string text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Version? version)
    {
        version = null;
        string[] parts = text.Split('.');
        if (parts.Length is < 2 or > 3 || !parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit)))
        {
            return false;
        }

        return Version.TryParse(text, out version);
    }
}
