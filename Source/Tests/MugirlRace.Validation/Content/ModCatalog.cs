using System.Reflection;
using MugirlRace.Validation.Infrastructure;

namespace MugirlRace.Validation.Content;

/// <summary>
/// RimWorld 1.7 与被测 Mod 一起加载的内容：Core 与已安装的 DLC、Mod 的依赖闭包（本地 Mods 目录或 Steam 创意工坊）、
/// <c>--assume-active</c> 指定的可选整合 Mod 及其依赖，以及 Mod 本身，按加载顺序排列。
/// </summary>
/// <remarks>
/// 评估两种启用配置。<see cref="Full"/> 启用所有已安装 DLC 与假定启用的整合 Mod，全部内容都按它加载。<see cref="Minimal"/>
/// 只启用 Mod 的硬依赖（Core、它依赖的 DLC 与依赖 Mod）；没有 <c>MayRequire</c> 保护就使用的 Def 必须在这个配置里存在。
/// </remarks>
internal sealed class ModCatalog
{
    // 官方内容包的标准顺序；RimWorld 按发售顺序把 DLC 排在 Core 之后。
    private static readonly string[] OfficialOrder = ["Core", "Royalty", "Ideology", "Biotech", "Anomaly", "Odyssey"];

    private readonly Dictionary<(ContentPack, ActiveMods), IReadOnlyList<string>> folderCache = [];

    private ModCatalog(Version gameVersion, ContentPack underTest, IReadOnlyList<ContentPack> loadOrder, IReadOnlyList<string> problems,
        IReadOnlyList<string> optionalProblems, IReadOnlyList<string> assumedNotInstalled)
    {
        GameVersion = gameVersion;
        UnderTest = underTest;
        LoadOrder = loadOrder;
        Problems = problems;
        OptionalProblems = optionalProblems;
        AssumedNotInstalled = assumedNotInstalled;
        Full = new ActiveMods("full", loadOrder, assumedNotInstalled);
        HardDependencies = Closure(underTest, loadOrder);
        Minimal = new ActiveMods("minimal", loadOrder.Where(pack => pack.PackageId == "ludeon.rimworld" || pack == underTest || HardDependencies.Contains(pack)));
    }

    /// <summary>Version.txt 中的游戏版本（例如 1.7.4977）。</summary>
    public Version GameVersion { get; }

    public ContentPack UnderTest { get; }

    /// <summary>完整配置下按加载顺序排列的全部内容包（官方内容在前，被测 Mod 最后）。</summary>
    public IReadOnlyList<ContentPack> LoadOrder { get; }

    /// <summary>被测 Mod 直接或间接需要的内容包（不含自身）。</summary>
    public IReadOnlySet<ContentPack> HardDependencies { get; }

    public ActiveMods Full { get; }

    public ActiveMods Minimal { get; }

    /// <summary>找不到的依赖 Mod 一类发现问题。</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>假定启用的整合 Mod 缺少的依赖（只影响可选内容，作警告报告）。</summary>
    public IReadOnlyList<string> OptionalProblems { get; }

    /// <summary>假定启用但本机未安装的包 id：只用于条件判断，其内容无法加载。</summary>
    public IReadOnlyList<string> AssumedNotInstalled { get; }

    public static ModCatalog Discover(ValidationOptions options)
    {
        if (!File.Exists(Path.Combine(options.GameDir, "Assembly-CSharp.dll")))
        {
            throw new EnvironmentException($"No RimWorld 1.7 installation at {options.GameDir} (Assembly-CSharp.dll missing).");
        }

        Version gameVersion = ReadGameVersion(options.GameDir);
        string majorMinor = gameVersion.ToString(2);
        ContentPack underTest = ContentPack.TryRead(options.ModRoot, isOfficial: false, isUnderTest: true, majorMinor)
            ?? throw new EnvironmentException($"{options.ModRoot} is not a mod folder (About/About.xml with a packageId is missing).");

        var official = new List<ContentPack>();
        string data = Path.Combine(options.GameDir, "Data");
        foreach (string directory in Directory.EnumerateDirectories(data)
                     .OrderBy(d => Array.IndexOf(OfficialOrder, Path.GetFileName(d)) is int i and >= 0 ? i : int.MaxValue)
                     .ThenBy(d => d, StringComparer.Ordinal))
        {
            if (ContentPack.TryRead(directory, isOfficial: true, isUnderTest: false, majorMinor) is { } pack)
            {
                official.Add(pack);
            }
        }

        var problems = new List<string>();
        var optionalProblems = new List<string>();
        Dictionary<string, ContentPack> installed = FindInstalledMods(options, underTest, majorMinor);
        var closure = new Dictionary<string, ContentPack>(StringComparer.Ordinal);
        AddClosure(underTest.Dependencies, official, installed, closure, problems, assumed: false);

        // 假定启用的整合 Mod：已安装的连同依赖加入加载顺序，未安装的只作为“启用”的包 id 参与条件判断。
        var notInstalled = new List<string>();
        foreach (string id in AssumedIds(options, underTest))
        {
            if (official.Any(pack => pack.PackageId == id) || closure.ContainsKey(id))
            {
                continue;
            }

            if (installed.ContainsKey(id))
            {
                AddClosure([id], official, installed, closure, optionalProblems, assumed: true);
            }
            else
            {
                notInstalled.Add(id);
            }
        }

        var loadOrder = new List<ContentPack>(official);
        loadOrder.AddRange(SortByDependencies(closure.Values));
        loadOrder.Add(underTest);
        return new ModCatalog(gameVersion, underTest, loadOrder, problems, optionalProblems, notInstalled);
    }

    /// <summary>1.7 游戏在 <paramref name="active"/> 下为 <paramref name="pack"/> 加载的目录，优先级高的在前。</summary>
    public IReadOnlyList<string> FoldersFor(ContentPack pack, ActiveMods active)
    {
        if (!folderCache.TryGetValue((pack, active), out IReadOnlyList<string>? folders))
        {
            folders = LoadFolderResolver.Resolve(pack, GameVersion, active);
            folderCache.Add((pack, active), folders);
        }

        return folders;
    }

    /// <summary>
    /// 游戏为依赖 Mod 加载的程序集（简单名 -> 文件），供程序集解析器使用。不含被测 Mod 与 Harmony：两者由验证程序自己提供。
    /// </summary>
    public IReadOnlyDictionary<string, string> DependencyAssemblies()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ContentPack pack in LoadOrder.Where(pack => !pack.IsOfficial && !pack.IsUnderTest))
        {
            foreach (LoadedFile file in LoadFolderResolver.Files(FoldersFor(pack, Full), "Assemblies", IsDll))
            {
                string name;
                try
                {
                    name = AssemblyName.GetAssemblyName(file.FullPath).Name ?? Path.GetFileNameWithoutExtension(file.FullPath);
                }
                catch (BadImageFormatException)
                {
                    continue;
                }

                if (!string.Equals(name, "0Harmony", StringComparison.OrdinalIgnoreCase))
                {
                    result.TryAdd(name, file.FullPath);
                }
            }
        }

        return result;
    }

    public static bool IsDll(string extension) => string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase);

    // "all" 展开为 Mod 的 LoadFolders.xml 中 IfModActive/IfModActiveAll 提到的所有包 id。
    private static IEnumerable<string> AssumedIds(ValidationOptions options, ContentPack underTest)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string requested in options.AssumeActive)
        {
            if (!requested.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                ids.Add(ActiveMods.Normalize(requested));
                continue;
            }

            foreach (string version in underTest.LoadFolders?.DefinedVersions ?? [])
            {
                foreach (LoadFolder folder in underTest.LoadFolders!.FoldersForVersion(version) ?? [])
                {
                    foreach (string id in (folder.RequiredAnyOf ?? []).Concat(folder.RequiredAllOf ?? []))
                    {
                        ids.Add(ActiveMods.Normalize(id));
                    }
                }
            }
        }

        return ids;
    }

    private static void AddClosure(IEnumerable<string> roots, List<ContentPack> official, Dictionary<string, ContentPack> installed,
        Dictionary<string, ContentPack> closure, List<string> problems, bool assumed)
    {
        var pending = new Queue<string>(roots);
        while (pending.Count > 0)
        {
            string id = pending.Dequeue();
            if (closure.ContainsKey(id) || official.Any(pack => pack.PackageId == id))
            {
                continue;
            }

            if (id.StartsWith("ludeon.rimworld", StringComparison.Ordinal))
            {
                problems.Add($"Required official content {id} is not installed.");
                continue;
            }

            if (!installed.TryGetValue(id, out ContentPack? dependency))
            {
                problems.Add($"Dependency {id} is not installed locally or in the workshop folder.");
                continue;
            }

            closure.Add(id, assumed ? dependency.AsAssumedActive() : dependency);
            foreach (string next in dependency.Dependencies)
            {
                pending.Enqueue(next);
            }
        }
    }

    private static Version ReadGameVersion(string gameDir)
    {
        string file = Path.Combine(gameDir, "Version.txt");
        string text = File.Exists(file) ? File.ReadAllText(file).Trim() : "";
        string number = text.Split(' ', 2)[0];
        if (!Version.TryParse(number, out Version? version) || version.Build < 0)
        {
            throw new EnvironmentException($"Cannot read the game version from {file} ('{text}').");
        }

        if (version.Major != 1 || version.Minor != 7)
        {
            throw new EnvironmentException($"The game at {gameDir} is version {version}; this suite validates RimWorld 1.7.");
        }

        return version;
    }

    // 先本地 Mods 目录（本地副本优先于创意工坊），再创意工坊目录。与被测 Mod 同 packageId 的副本（例如订阅的发布版）被忽略。
    private static Dictionary<string, ContentPack> FindInstalledMods(ValidationOptions options, ContentPack underTest, string majorMinor)
    {
        var result = new Dictionary<string, ContentPack>(StringComparer.Ordinal);
        foreach (string root in new[] { options.LocalModsDir, options.WorkshopDir })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
            {
                if (string.Equals(Path.GetFullPath(directory), underTest.RootDir, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ContentPack? pack;
                try
                {
                    pack = ContentPack.TryRead(directory, isOfficial: false, isUnderTest: false, majorMinor);
                }
                catch (System.Xml.XmlException)
                {
                    continue;
                }

                if (pack is not null && pack.PackageId != underTest.PackageId)
                {
                    result.TryAdd(pack.PackageId, pack);
                }
            }
        }

        return result;
    }

    // 依赖在被依赖者之前；闭包内遵守 loadAfter；同级按包 id 排序以保持确定性。
    private static List<ContentPack> SortByDependencies(IEnumerable<ContentPack> packs)
    {
        List<ContentPack> remaining = packs.OrderBy(pack => pack.PackageId, StringComparer.Ordinal).ToList();
        var sorted = new List<ContentPack>();
        while (remaining.Count > 0)
        {
            ContentPack next = remaining.FirstOrDefault(pack => pack.Dependencies.Concat(pack.LoadAfter)
                    .All(id => remaining.All(other => other.PackageId != id)))
                ?? remaining[0];
            sorted.Add(next);
            remaining.Remove(next);
        }

        return sorted;
    }

    private static HashSet<ContentPack> Closure(ContentPack root, IReadOnlyList<ContentPack> packs)
    {
        var result = new HashSet<ContentPack>();
        var pending = new Queue<string>(root.Dependencies);
        while (pending.Count > 0)
        {
            string id = pending.Dequeue();
            ContentPack? pack = packs.FirstOrDefault(p => p.PackageId == id);
            if (pack is not null && result.Add(pack))
            {
                foreach (string next in pack.Dependencies)
                {
                    pending.Enqueue(next);
                }
            }
        }

        return result;
    }
}
