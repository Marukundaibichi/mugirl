using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Metadata;
using MugirlRace.Validation.Reporting;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// A. 产物与布局：About.xml 元数据、每个游戏版本从 Mod 目录加载什么（LoadFolders.xml 语义）、各版本发布的程序集，以及 1.7 程序集的引用。
/// </summary>
internal static class ArtifactChecks
{
    public const string AssemblyName = "MugirlRace";
    public const string AboutFile = "about.json";
    public const string LoadFoldersFile = "load-folders.txt";

    // 1.7 构建绝不能引用的程序集：Unity 兼容层（ADR-006）。AlienRace 是合法依赖，按当前实际加载文件核对版本。
    private static readonly string[] ForbiddenReferencePrefixes = ["UnityEngine", "Unity.", "Waylith.UnityCompat"];

    // 测试与工具程序集不得进入发布 DLL 的引用。
    private static readonly string[] ForbiddenReferences =
        ["Assembly-CSharp-firstpass", "MugirlRace.Tests", "MugirlRace.Validation", "MugirlRace.GameValidation", "NegativeControl"];

    // 发布目录中不应出现的临时文件与构建目录。
    private static readonly string[] StrayFilePatterns = ["*.tmp", "*.bak", "*.orig", "Thumbs.db", "desktop.ini"];
    private static readonly string[] StrayDirectories = ["bin", "obj"];
    private static readonly string[] ContentFolders = ["Assemblies", "Defs", "Patches", "Languages", "Textures", "Sounds", "Shaders"];

    public static void Run(Report report, ValidationOptions options, ModCatalog catalog)
    {
        report.Run("A.dependencies", "Dependency mods resolve for RimWorld 1.7", recorder => CheckDependencies(recorder, catalog));
        report.Run("A.about", "About.xml identity, supported versions and dependency metadata", recorder => CheckAbout(recorder, options, catalog));
        report.Run("A.layout", "LoadFolders.xml and the assemblies each game version loads", recorder => CheckLayout(recorder, options, catalog));
        report.Run("A.assembly-refs", "Identity and references of the 1.7 assembly", recorder => CheckReferences(recorder, options, catalog));
    }

    private static void CheckDependencies(CheckRecorder recorder, ModCatalog catalog)
    {
        foreach (string problem in catalog.Problems.Distinct(StringComparer.Ordinal))
        {
            recorder.Fail(problem.Split(' ')[1], problem);
        }

        foreach (string problem in catalog.OptionalProblems.Distinct(StringComparer.Ordinal))
        {
            recorder.Warn(problem.Split(' ')[1], "Optional integration (--assume-active): " + problem);
        }

        foreach (string id in catalog.AssumedNotInstalled)
        {
            recorder.Warn(id, "Assumed active (--assume-active) but not installed: its load folders are checked, its own Defs and assemblies are not available.");
        }

        foreach (ContentPack pack in catalog.LoadOrder.Where(pack => !pack.IsOfficial && !pack.IsUnderTest))
        {
            IReadOnlyList<string> folders = catalog.FoldersFor(pack, catalog.Full);
            recorder.Info(pack.PackageId, $"{pack.Name}: loads {string.Join(", ", folders.Select(f => Relative(pack.RootDir, f)))} on 1.7"
                + (pack.SupportedVersions.Contains("1.7") ? "" : " (no 1.7 support declared; falls back to its newest earlier version)")
                + (pack.IsAssumedActive ? " [assumed active]" : ""));
        }

        recorder.Summary = $"load order: {string.Join(" → ", catalog.LoadOrder.Select(p => p.PackageId))}";
    }

    // About.xml 的期望值放在 Baselines/about.json；发布元数据缺项时 1.7 会在 Mod 列表或日志中警告。
    private static void CheckAbout(CheckRecorder recorder, ValidationOptions options, ModCatalog catalog)
    {
        using JsonDocument? expected = JsonBaseline.Load(recorder, options.BaselineDir, AboutFile);
        if (expected is null)
        {
            return;
        }

        string aboutPath = Path.Combine(catalog.UnderTest.RootDir, "About", "About.xml");
        var document = new XmlDocument();
        document.Load(aboutPath);
        XmlElement meta = document.DocumentElement!;
        JsonElement root = expected.RootElement;

        string? packageId = JsonBaseline.String(root, "packageId");
        string actualId = meta["packageId"]?.InnerText.Trim() ?? "";
        if (packageId is not null && actualId != packageId)
        {
            recorder.Fail("About.xml packageId", $"packageId is '{actualId}', expected '{packageId}' (saves, dependents and Harmony owners name it).");
        }

        List<string> versions = Items(meta, "supportedVersions");
        foreach (string version in JsonBaseline.Strings(root, "supportedVersions").Where(v => !versions.Contains(v)))
        {
            recorder.Fail("About.xml supportedVersions " + version, $"supportedVersions does not list {version}.");
        }

        var dependencies = meta["modDependencies"]?.ChildNodes.OfType<XmlElement>().ToList() ?? [];
        foreach (string id in JsonBaseline.Strings(root, "modDependencies"))
        {
            if (!dependencies.Any(li => string.Equals(li["packageId"]?.InnerText.Trim(), id, StringComparison.OrdinalIgnoreCase)))
            {
                recorder.Fail("About.xml modDependencies " + id, "Required dependency missing.");
            }
        }

        foreach (XmlElement dependency in dependencies)
        {
            string id = dependency["packageId"]?.InnerText.Trim() ?? "(no packageId)";
            bool hasUrl = new[] { "steamWorkshopUrl", "downloadUrl" }.Any(name => !string.IsNullOrWhiteSpace(dependency[name]?.InnerText));
            if (!hasUrl)
            {
                recorder.Fail("About.xml modDependencies " + id, "No steamWorkshopUrl or downloadUrl: RimWorld 1.7 cannot point players to the missing mod.");
            }
        }

        CheckList(recorder, meta, root, "loadAfter");
        CheckList(recorder, meta, root, "incompatibleWith");
        recorder.Summary = $"packageId {actualId}, supportedVersions {string.Join(",", versions)}, {dependencies.Count} dependencies";
    }

    private static void CheckList(CheckRecorder recorder, XmlElement meta, JsonElement root, string listName)
    {
        List<string> actual = Items(meta, listName);
        foreach (string id in JsonBaseline.Strings(root, listName))
        {
            if (!actual.Any(item => ActiveMods.Normalize(item) == ActiveMods.Normalize(id)))
            {
                recorder.Fail($"About.xml {listName} {id}", $"{listName} does not list {id}.");
            }
        }
    }

    private static List<string> Items(XmlElement meta, string listName)
        => meta[listName]?.ChildNodes.OfType<XmlElement>().Select(li => li.InnerText.Trim()).ToList() ?? [];

    private static void CheckLayout(CheckRecorder recorder, ValidationOptions options, ModCatalog catalog)
    {
        ContentPack mod = catalog.UnderTest;
        if (mod.LoadFolders is null)
        {
            recorder.Fail("LoadFolders.xml", "Missing: without it 1.6 and 1.7 would load the same folders.");
            return;
        }

        var lines = new List<string>();
        foreach (string version in mod.LoadFolders.DefinedVersions)
        {
            IReadOnlyList<LoadFolder> folders = mod.LoadFolders.FoldersForVersion(version)!;
            lines.Add(version + "\t" + string.Join(",", folders.Select(Describe)));
            foreach (LoadFolder folder in folders.Where(f => !Directory.Exists(Path.Combine(mod.RootDir, f.FolderName))))
            {
                recorder.Fail("LoadFolders.xml v" + version, $"Folder '{folder.FolderName}' does not exist.");
            }

            foreach (IGrouping<string, LoadFolder> duplicate in folders.GroupBy(f => f.FolderName.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                recorder.Fail("LoadFolders.xml v" + version, $"Folder '{duplicate.Key}' is listed {duplicate.Count()} times.");
            }
        }

        GoldenList.Compare(options.BaselineDir, LoadFoldersFile, lines, options.UpdateBaselines, recorder,
            "Expected LoadFolders.xml version lists: version<TAB>folders in file order, each with its IfModActive* conditions.\n"
            + "v1.6 is frozen. v1.7 loads the mod root only for the shared Textures and Sounds; optional content lives in 1.7/Mods/<Name>.");

        // 每个版本实际加载的内容（游戏的版本回退与文件去重）。1.6 视图冻结：额外文件只警告，由用户决定是否移除。
        var views = new[] { (Label: "1.6", Version: new Version(1, 6, 9999), Frozen: true), (Label: "1.7", Version: catalog.GameVersion, Frozen: false) };
        foreach ((string label, Version version, bool frozen) in views)
        {
            IReadOnlyList<string> folders = LoadFolderResolver.Resolve(mod, version, catalog.Full);
            CheckAssemblies(recorder, mod, label, folders, frozen);
            if (!frozen)
            {
                CheckStrayFiles(recorder, mod, label, folders);
            }
        }

        recorder.Summary = "LoadFolders.xml and the assemblies of the 1.6 and 1.7 views checked";
    }

    // "/"、"1.7"、"1.7/Mods/FacialAnimation[IfModActive=Nals.FacialAnimation]"。
    private static string Describe(LoadFolder folder)
    {
        string name = folder.FolderName.Length == 0 ? "/" : folder.FolderName.Replace('\\', '/');
        var conditions = new List<string>();
        if (folder.RequiredAnyOf is { Count: > 0 } any)
        {
            conditions.Add("IfModActive=" + string.Join(",", any));
        }

        if (folder.RequiredAllOf is { Count: > 0 } all)
        {
            conditions.Add("IfModActiveAll=" + string.Join(",", all));
        }

        if (folder.DisallowedAnyOf is { Count: > 0 } none)
        {
            conditions.Add("IfModNotActive=" + string.Join(",", none));
        }

        return conditions.Count == 0 ? name : name + "[" + string.Join(";", conditions) + "]";
    }

    private static void CheckAssemblies(CheckRecorder recorder, ContentPack mod, string version, IReadOnlyList<string> folders, bool frozen)
    {
        string subject = "game " + version;
        string expectedDll = Path.Combine(mod.RootDir, version, "Assemblies", AssemblyName + ".dll");
        IReadOnlyList<LoadedFile> loaded = LoadFolderResolver.Files(folders, "Assemblies");
        List<LoadedFile> dlls = loaded.Where(f => ModCatalog.IsDll(Path.GetExtension(f.FullPath))).ToList();
        Action<string, string> problem = frozen ? (s, m) => recorder.Warn(s, m) : (s, m) => recorder.Fail(s, m);

        if (!dlls.Any(f => string.Equals(f.FullPath, expectedDll, StringComparison.OrdinalIgnoreCase)))
        {
            recorder.Fail(subject, $"Must load {Relative(mod.RootDir, expectedDll)}; loads: "
                + (dlls.Count == 0 ? "nothing" : string.Join(", ", dlls.Select(f => Relative(mod.RootDir, f.FullPath)))));
        }

        foreach (LoadedFile dll in dlls.Where(f => !string.Equals(f.FullPath, expectedDll, StringComparison.OrdinalIgnoreCase)))
        {
            string name = Path.GetFileName(dll.FullPath);
            string relative = Relative(mod.RootDir, dll.FullPath);
            if (name.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Unity.", StringComparison.OrdinalIgnoreCase))
            {
                // UnityEngine 同名编译或测试门面一旦发布，游戏会加载它而不是重定向到 UnityStubs（ADR-006）。
                recorder.Fail(subject + ": " + relative, "A UnityEngine facade assembly in a loaded Assemblies folder: the compile-time facade must never ship.");
                continue;
            }

            problem(subject + ": " + relative, frozen
                ? "Extra DLL in the frozen 1.6 Assemblies folder (loaded by 1.6 and by any version that falls back to it). Remove it when it is not part of the release."
                : "Only " + AssemblyName + ".dll may be loaded: remove this DLL.");
        }

        foreach (LoadedFile file in loaded.Where(f => !ModCatalog.IsDll(Path.GetExtension(f.FullPath))))
        {
            string name = Path.GetFileName(file.FullPath);
            if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string why = name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)
                ? "build host files must not ship (the game's runtime would treat the folder as an application)"
                : "unexpected file in a loaded Assemblies folder";
            problem(subject + ": " + Relative(mod.RootDir, file.FullPath), why + ".");
        }

        // 被高优先级目录中同名文件遮蔽的 DLL 不会加载，但属于陈旧的打包内容。
        foreach (string folder in folders)
        {
            string directory = Path.Combine(folder, "Assemblies");
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories))
            {
                if (!loaded.Any(f => string.Equals(f.FullPath, file, StringComparison.OrdinalIgnoreCase)))
                {
                    problem(subject + ": " + Relative(mod.RootDir, file), "Shadowed DLL in a load folder of this version: remove it.");
                }
            }
        }
    }

    private static void CheckStrayFiles(CheckRecorder recorder, ContentPack mod, string version, IReadOnlyList<string> folders)
    {
        foreach (string folder in folders)
        {
            foreach (string content in ContentFolders.Select(name => Path.Combine(folder, name)).Where(Directory.Exists))
            {
                foreach (string directory in Directory.EnumerateDirectories(content, "*", SearchOption.AllDirectories)
                             .Where(d => StrayDirectories.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)))
                {
                    recorder.Fail($"game {version}: {Relative(mod.RootDir, directory)}", "Build output folder inside loaded content: it would be packaged and uploaded.");
                }

                foreach (string file in StrayFilePatterns.SelectMany(pattern => Directory.EnumerateFiles(content, pattern, SearchOption.AllDirectories)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    recorder.Fail($"game {version}: {Relative(mod.RootDir, file)}", "Temporary or system file inside loaded content: remove it.");
                }
            }
        }
    }

    private static void CheckReferences(CheckRecorder recorder, ValidationOptions options, ModCatalog catalog)
    {
        string shipped = Path.Combine(catalog.UnderTest.RootDir, "1.7", "Assemblies", AssemblyName + ".dll");
        var targets = new List<string> { options.ModAssembly };
        if (File.Exists(shipped) && !string.Equals(Path.GetFullPath(shipped), options.ModAssembly, StringComparison.OrdinalIgnoreCase))
        {
            targets.Add(shipped);
            if (!Sha256(shipped).Equals(Sha256(options.ModAssembly), StringComparison.Ordinal))
            {
                recorder.Info("1.7/Assemblies/" + AssemblyName + ".dll", "The shipped assembly is not the one under test (validate it with --mod before publishing).");
            }
        }

        Version? harVersion = catalog.DependencyAssemblies().TryGetValue("AlienRace", out string? harPath) ? System.Reflection.AssemblyName.GetAssemblyName(harPath).Version : null;
        foreach (string path in targets)
        {
            AssemblyFacts facts = AssemblyFacts.Read(path);
            string subject = path == options.ModAssembly ? "assembly under test" : "shipped 1.7 assembly";
            if (facts.Name != AssemblyName)
            {
                recorder.Fail(subject, $"Assembly name is '{facts.Name}', expected {AssemblyName}.");
            }

            if (facts.TargetFramework != ".NETCoreApp,Version=v10.0")
            {
                recorder.Fail(subject, $"Target framework is '{facts.TargetFramework ?? "(none)"}', expected .NETCoreApp,Version=v10.0.");
            }

            foreach ((string name, Version version) in facts.References)
            {
                if (ForbiddenReferencePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)) || ForbiddenReferences.Contains(name))
                {
                    recorder.Fail(subject + ": " + name, "Forbidden assembly reference (Unity compatibility layer, compile-time facade or test code).");
                }
                else if (name == "0Harmony" && version.Major != 3)
                {
                    recorder.Fail(subject + ": 0Harmony", $"Compiled against Harmony {version}; RimWorld 1.7 loads Harmony 3.");
                }
                else if (name == "Assembly-CSharp" && (version.Major != 1 || version.Minor != 7))
                {
                    recorder.Fail(subject + ": Assembly-CSharp", $"Compiled against Assembly-CSharp {version}, not 1.7.");
                }
                else if (name == "AlienRace")
                {
                    // 从实际 1.7 加载目录取得 HAR 版本，与编译引用对照；不能假定固定回退到 1.6。
                    if (harVersion is null)
                    {
                        recorder.Warn(subject + ": AlienRace", "Humanoid Alien Races is not installed here; the reference cannot be compared with the assembly 1.7 loads.");
                    }
                    else if (harVersion != version)
                    {
                        recorder.Fail(subject + ": AlienRace", $"Compiled against AlienRace {version}; RimWorld 1.7 loads AlienRace {harVersion} ({harPath}).");
                    }
                }
            }

            recorder.Info(subject, $"{Path.GetFileName(path)} {facts.Version} ({facts.TargetFramework}); references "
                + string.Join(", ", facts.References.Select(r => $"{r.Name} {r.Version}")));
        }

        recorder.Summary = $"identity and references of {targets.Count} assembl{(targets.Count == 1 ? "y" : "ies")} checked";
    }

    public static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static string Relative(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative == "." ? "/" : relative;
    }
}
