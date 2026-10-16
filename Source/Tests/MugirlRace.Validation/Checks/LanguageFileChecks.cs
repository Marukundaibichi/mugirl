using System.Xml;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Reporting;
using Verse;
using LoadFolder = MugirlRace.Validation.Content.LoadFolder;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// C. Mod 自己的语言文件（Keyed 的中英对照由 <c>C.keyed</c> 负责）：
/// <list type="bullet">
/// <item><c>C.language-files</c>：同一语言的相对路径在一个版本的多个加载目录中出现时只有优先级高的那个被读（1.7 视图失败，冻结的 1.6
/// 视图只警告）；一个文件里的重复键；DefInjected 键在同一语言里重复定义；DefInjected 指向不存在的 Def（翻译报告中的错误）；只随可选
/// 内容存在的 Def 的翻译放在总会加载的目录里；<c>rulesStrings</c> 必须按 <c>li</c> 列表翻译。</item>
/// <item><c>C.job-reports</c>：每个带 <c>reportString</c> 的 JobDef 都有简体中文翻译（否则中文界面显示英文工作描述）。</item>
/// </list>
/// </summary>
internal static class LanguageFileChecks
{
    private static readonly Version FrozenGameVersion = new(1, 6, 9999);

    public static void Run(Report report, ContentModel content)
    {
        IReadOnlyList<InjectedEntry> injected = ReadDefInjected(content);
        report.Run("C.language-files", "Language files: no shadowing, duplicates or orphan DefInjected keys", recorder => CheckFiles(recorder, content, injected));
        report.Run("C.job-reports", "Every JobDef reportString has a Simplified Chinese translation", recorder => CheckJobReports(recorder, content, injected));
    }

    private static void CheckFiles(CheckRecorder recorder, ContentModel content, IReadOnlyList<InjectedEntry> injected)
    {
        ContentPack mod = content.Catalog.UnderTest;
        string root = mod.RootDir;
        CheckShadowing(recorder, mod, content.Catalog.FoldersFor(mod, content.Catalog.Full), "1.7", frozen: false);
        CheckShadowing(recorder, mod, LoadFolderResolver.Resolve(mod, FrozenGameVersion, content.Catalog.Full), "1.6", frozen: true);

        // 文件内重复键（Keyed 与 DefInjected）。
        int files = 0;
        foreach (string file in LanguageFiles(content.Catalog.FoldersFor(mod, content.Catalog.Full)).Select(f => f.FullPath))
        {
            files++;
            if (Load(recorder, root, file) is not { } element)
            {
                continue;
            }

            foreach (IGrouping<string, XmlElement> duplicate in element.ChildNodes.OfType<XmlElement>().GroupBy(e => e.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                recorder.Fail($"{XmlPaths.File(root, file)}: {duplicate.Key}", $"Key defined {duplicate.Count()} times in one file; the game keeps one and reports the rest as translation errors.");
            }
        }

        foreach (IGrouping<(string Language, string Folder, string Key), InjectedEntry> group in injected
                     .GroupBy(e => (e.Language, e.TypeFolder, e.Key)).Where(g => g.Select(e => e.File).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            recorder.Fail($"{group.Key.Language}/DefInjected/{group.Key.Folder}: {group.Key.Key}",
                "Defined in several files: " + string.Join(", ", group.Select(e => XmlPaths.File(root, e.File)).Distinct(StringComparer.Ordinal)));
        }

        IReadOnlyList<string> minimalFolders = content.Catalog.FoldersFor(mod, content.Catalog.Minimal);
        var unknownTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (InjectedEntry entry in injected)
        {
            string file = XmlPaths.File(root, entry.File);
            if (entry.DefType is null)
            {
                if (unknownTypes.Add(entry.TypeFolder))
                {
                    recorder.Fail($"DefInjected/{entry.TypeFolder}", "The folder name is not a Def type; none of its translations are applied.", file);
                }

                continue;
            }

            if (entry.Key.EndsWith(".rulesStrings", StringComparison.Ordinal) && !entry.Element.ChildNodes.OfType<XmlElement>().Any(e => e.Name == "li"))
            {
                recorder.Fail($"{entry.Language}: {entry.Key}", "rulesStrings must be translated as a list of <li> nodes.", file);
            }

            if (entry.UnavailablePackage is { } package)
            {
                recorder.Info($"{entry.Language}: {entry.Key}", $"Its folder loads only with {package}, which is not installed here; not verifiable.", file);
                continue;
            }

            string defName = entry.Key.Split('.', 2)[0];
            IReadOnlyList<DefEntry> found = content.Defs.Find(entry.DefType, defName);
            if (found.Count == 0)
            {
                recorder.Fail($"{entry.Language}: {entry.DefType.Name}/{defName}", $"DefInjected translation '{entry.Key}' for a def that does not exist (translation report error).", file);
            }
            else if (!found.Any(e => e.InMinimal) && minimalFolders.Contains(entry.LoadFolder, StringComparer.OrdinalIgnoreCase))
            {
                string providers = string.Join(", ", found.Select(e => e.Pack.PackageId).Distinct());
                recorder.Fail($"{entry.Language}: {entry.DefType.Name}/{defName}", $"Translates a def that exists only with optional content ({providers}) from an always-loaded folder; "
                    + "players without it get a translation error. Move it into the conditional 1.7/Mods folder.", file);
            }
        }

        recorder.Summary = $"{files} language file(s), {injected.Count} DefInjected key(s) checked";
    }

    // 游戏按语言目录内的相对路径去重：同一路径只读优先级最高的目录里那一份。
    private static void CheckShadowing(CheckRecorder recorder, ContentPack mod, IReadOnlyList<string> folders, string version, bool frozen)
    {
        foreach (IGrouping<string, LanguageFile> group in LanguageFiles(folders).GroupBy(f => f.LanguageRelative, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            string subject = $"game {version}: Languages/{group.Key}";
            string message = "Present in several load folders; only " + XmlPaths.File(mod.RootDir, group.First().FullPath) + " is read: "
                + string.Join(", ", group.Skip(1).Select(f => XmlPaths.File(mod.RootDir, f.FullPath))) + ". Rename one of them.";
            if (frozen)
            {
                recorder.Warn(subject, message + " (frozen 1.6 content)");
            }
            else
            {
                recorder.Fail(subject, message);
            }
        }
    }

    private static void CheckJobReports(CheckRecorder recorder, ContentModel content, IReadOnlyList<InjectedEntry> injected)
    {
        string root = content.Catalog.UnderTest.RootDir;
        var chinese = injected.Where(e => e.Language == "ChineseSimplified" && e.DefType is not null && typeof(JobDef).IsAssignableFrom(e.DefType))
            .GroupBy(e => e.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Element.InnerText.Trim(), StringComparer.Ordinal);
        int jobs = 0;
        foreach (XmlElement node in content.Corpus.Defs)
        {
            NodeSource? source = content.Corpus.SourceOf(node);
            if (!ContentModel.IsOurs(source?.Pack) || source!.InsertedByPatch || node.GetAttribute("Abstract").Equals("true", StringComparison.OrdinalIgnoreCase)
                || !DefIndex.MayRequireSatisfied(node, content.Catalog.Full))
            {
                continue;
            }

            Type? type = content.Types.GetTypeInAnyAssembly(node.GetAttribute("Class") is { Length: > 0 } cls ? cls : node.Name);
            string? defName = node["defName"]?.InnerText.Trim();
            if (type is null || !typeof(JobDef).IsAssignableFrom(type) || string.IsNullOrEmpty(defName)
                || content.Inheritance.Resolve(node)["reportString"]?.InnerText.Trim() is not { Length: > 0 })
            {
                continue;
            }

            jobs++;
            string key = defName + ".reportString";
            if (!chinese.TryGetValue(key, out string? text) || text.Length == 0)
            {
                recorder.Fail(key, "JobDef reportString without a Simplified Chinese DefInjected translation: the Chinese UI shows the English job text.", XmlPaths.File(root, source.File));
            }
        }

        recorder.Summary = $"{jobs} JobDef reportString(s) checked";
    }

    private static IReadOnlyList<InjectedEntry> ReadDefInjected(ContentModel content)
    {
        ContentPack mod = content.Catalog.UnderTest;
        IReadOnlyList<string> folders = content.Catalog.FoldersFor(mod, content.Catalog.Full);
        Dictionary<string, string?> unavailable = UnavailableFolders(mod, content.Catalog);
        var entries = new List<InjectedEntry>();
        foreach (LanguageFile file in LanguageFiles(folders))
        {
            string[] parts = file.LanguageRelative.Split('/');
            if (parts.Length < 4 || parts[1] != "DefInjected" || !file.FullPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            XmlDocument document = new();
            try
            {
                document.Load(file.FullPath);
            }
            catch (XmlException)
            {
                continue;
            }

            string typeFolder = parts[2];
            Type? type = content.Types.GetTypeInAnyAssembly(typeFolder) is { } resolved && typeof(Def).IsAssignableFrom(resolved) ? resolved : null;
            foreach (XmlElement element in document.DocumentElement?.ChildNodes.OfType<XmlElement>() ?? [])
            {
                entries.Add(new InjectedEntry(parts[0], typeFolder, type, element.Name, element, file.FullPath, file.LoadFolder, unavailable.GetValueOrDefault(file.LoadFolder)));
            }
        }

        return entries;
    }

    // 条件目录 -> 其条件中假定启用但本机未安装的包（它们的 Def 无法加载，翻译无从核对）。
    private static Dictionary<string, string?> UnavailableFolders(ContentPack mod, ModCatalog catalog)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (LoadFolder folder in mod.LoadFolders?.FoldersForVersion(catalog.GameVersion.ToString(2)) ?? [])
        {
            string? missing = (folder.RequiredAnyOf ?? []).Concat(folder.RequiredAllOf ?? [])
                .FirstOrDefault(id => catalog.AssumedNotInstalled.Contains(ActiveMods.Normalize(id), StringComparer.Ordinal));
            result[Path.Combine(mod.RootDir, folder.FolderName)] = missing;
        }

        return result;
    }

    // 各加载目录 Languages/ 下的 XML 文件；语言目录名规范化为 KeyedIndex 的语言名（旧名 ChineseSimplified 与新名等价）。
    private static IEnumerable<LanguageFile> LanguageFiles(IReadOnlyList<string> folders)
    {
        foreach (string folder in folders)
        {
            string languages = Path.Combine(folder, "Languages");
            if (!Directory.Exists(languages))
            {
                continue;
            }

            foreach (string languageDir in Directory.EnumerateDirectories(languages).Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(languageDir);
                string language = KeyedIndex.Languages.FirstOrDefault(pair => pair.Value.Contains(name, StringComparer.Ordinal)).Key ?? name.Split(" (", 2)[0];
                foreach (string file in Directory.EnumerateFiles(languageDir, "*.xml", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    yield return new LanguageFile(folder, language + "/" + Path.GetRelativePath(languageDir, file).Replace('\\', '/'), file);
                }
            }
        }
    }

    private static XmlElement? Load(CheckRecorder recorder, string root, string file)
    {
        try
        {
            var document = new XmlDocument();
            document.Load(file);
            return document.DocumentElement;
        }
        catch (XmlException exception)
        {
            recorder.Fail(XmlPaths.File(root, file), "Malformed language file: " + exception.Message);
            return null;
        }
    }

    private sealed record LanguageFile(string LoadFolder, string LanguageRelative, string FullPath);

    private sealed record InjectedEntry(string Language, string TypeFolder, Type? DefType, string Key, XmlElement Element, string File, string LoadFolder, string? UnavailablePackage);
}
