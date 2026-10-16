using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Reporting;
using Verse;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// C. Mugirl 自己的内容约定（移植自 1.6 的 Invoke-Phase6StaticValidation.ps1 与 Invoke-TextureAssetValidation.ps1，改为针对 1.7 加载集）：
/// <list type="bullet">
/// <item><c>C.content-contracts</c>：<c>Baselines/content-contracts.json</c> 中的契约——ThingDef 名不以数字结尾、休眠功能的文件保持为空、
/// 指定 Def 字段的值、只能出现在条件目录中的贴图前缀、必须存在与不得回流的资源文件、禁止的贴图尺寸、9.20 服装的贴图组合。</item>
/// <item><c>C.patch-governance</c>：每个补丁文件开头有 <c>PatchGovernance:</c> 注释，写明 Targets、Scope、Duplicate guard、Failure。</item>
/// <item><c>C.forbidden-patterns</c>：<c>Baselines/forbidden-patterns.txt</c> 中的旧名与笔误不出现在 1.7 内容与源码中。</item>
/// </list>
/// </summary>
internal static partial class ContentContractChecks
{
    public const string ContractsFile = "content-contracts.json";
    public const string ForbiddenPatternsFile = "forbidden-patterns.txt";

    private static readonly string[] TextExtensions = [".xml", ".txt", ".glsl", ".cs"];

    public static void Run(Report report, ValidationOptions options, ContentModel content)
    {
        report.Run("C.content-contracts", "Mugirl content contracts (Baselines/content-contracts.json)", recorder => CheckContracts(recorder, options, content));
        report.Run("C.patch-governance", "Every patch file starts with a PatchGovernance comment", recorder => CheckGovernance(recorder, content));
        report.Run("C.forbidden-patterns", "Legacy names and typos stay out of the 1.7 content and source", recorder => CheckForbiddenPatterns(recorder, options, content));
    }

    private static void CheckContracts(CheckRecorder recorder, ValidationOptions options, ContentModel content)
    {
        using JsonDocument? document = JsonBaseline.Load(recorder, options.BaselineDir, ContractsFile);
        if (document is null)
        {
            return;
        }

        JsonElement root = document.RootElement;
        string modRoot = content.Catalog.UnderTest.RootDir;
        int contracts = 0;
        if (JsonBaseline.String(root, "thingDefNameNoTrailingDigit") is { Length: > 0 } digitReason)
        {
            contracts++;
            CheckDefNameDigits(recorder, content, digitReason);
        }

        foreach ((string file, string reason) in Map(recorder, root, "emptyFiles"))
        {
            contracts++;
            string path = Path.Combine(modRoot, file);
            if (File.Exists(path) && TryLoad(path)?.DocumentElement?.ChildNodes.OfType<XmlElement>().Any() == true)
            {
                recorder.Fail(file, "Must stay empty (no Def elements): " + reason);
            }
        }

        foreach (JsonElement rule in Array(root, "defValues"))
        {
            contracts++;
            CheckDefValue(recorder, content, rule);
        }

        foreach (JsonElement rule in Array(root, "conditionalTextures"))
        {
            contracts++;
            CheckConditionalTextures(recorder, content, rule);
        }

        foreach ((string pattern, string reason) in Map(recorder, root, "requiredFiles"))
        {
            contracts++;
            foreach (string file in ExpandBraces(pattern).Where(file => !File.Exists(Path.Combine(modRoot, file))))
            {
                recorder.Fail(file, "Required file missing: " + reason);
            }
        }

        foreach ((string pattern, string reason) in Map(recorder, root, "forbiddenFiles"))
        {
            contracts++;
            foreach (string file in ExpandBraces(pattern).SelectMany(glob => Glob(modRoot, glob)))
            {
                recorder.Fail(file, "Must not exist: " + reason);
            }
        }

        foreach (JsonElement rule in Array(root, "forbiddenPngSizes"))
        {
            contracts++;
            CheckPngSizes(recorder, modRoot, rule);
        }

        foreach (JsonElement rule in Array(root, "wornApparel"))
        {
            contracts++;
            CheckWornApparel(recorder, content, rule);
        }

        recorder.Summary = $"{contracts} contract(s) checked";
    }

    // ThingDef（含子类，例如 HAR 的 ThingDef_AlienRace）的 defName 不以数字结尾。
    private static void CheckDefNameDigits(CheckRecorder recorder, ContentModel content, string reason)
    {
        foreach (DefEntry entry in content.Defs.All.Where(e => ContentModel.IsOurs(e.Pack) && !e.Implied && typeof(ThingDef).IsAssignableFrom(e.Type)))
        {
            if (entry.DefName.Length > 0 && char.IsDigit(entry.DefName[^1]))
            {
                recorder.Fail($"ThingDef/{entry.DefName}", "defName ends in a digit: " + reason, XmlPaths.File(entry.Pack.RootDir, entry.File));
            }
        }
    }

    // { def: "Type/defName", path: "a/b", equals: "value", reason }，在继承与补丁之后的节点上比较。
    private static void CheckDefValue(CheckRecorder recorder, ContentModel content, JsonElement rule)
    {
        string def = JsonBaseline.String(rule, "def") ?? "";
        string path = JsonBaseline.String(rule, "path") ?? "";
        string expected = JsonBaseline.String(rule, "equals") ?? "";
        string reason = JsonBaseline.String(rule, "reason") ?? "";
        string[] parts = def.Split('/', 2);
        Type? type = parts.Length == 2 ? content.Types.GetTypeInAnyAssembly(parts[0]) : null;
        if (type is null || reason.Length == 0)
        {
            recorder.Fail($"Baselines/{ContractsFile}: defValues {def}", "Expected { def: \"DefType/defName\", path, equals, reason } with a resolvable def type.");
            return;
        }

        XmlElement? node = content.Corpus.Defs.FirstOrDefault(n => n["defName"]?.InnerText.Trim() == parts[1]
            && ContentModel.IsOurs(content.Corpus.SourceOf(n)?.Pack)
            && content.Types.GetTypeInAnyAssembly(n.GetAttribute("Class") is { Length: > 0 } cls ? cls : n.Name) is { } t && type.IsAssignableFrom(t));
        if (node is null)
        {
            recorder.Fail(def, "The def named by the contract does not exist in the 1.7 content: " + reason);
            return;
        }

        string? actual = content.Inheritance.Resolve(node).SelectSingleNode(path)?.InnerText.Trim();
        if (actual != expected)
        {
            recorder.Fail($"{def} {path}", $"Is '{actual ?? "(absent)"}', expected '{expected}': {reason}");
        }
    }

    // { prefix: "FA/", folder: "1.7/Mods/FacialAnimation", reason }：以该前缀开头的 XML 值只能写在该目录的文件里，且贴图只在该目录。
    private static void CheckConditionalTextures(CheckRecorder recorder, ContentModel content, JsonElement rule)
    {
        string prefix = JsonBaseline.String(rule, "prefix") ?? "";
        string folder = (JsonBaseline.String(rule, "folder") ?? "").TrimEnd('/');
        string reason = JsonBaseline.String(rule, "reason") ?? "";
        ContentPack mod = content.Catalog.UnderTest;
        string folderPath = Path.GetFullPath(Path.Combine(mod.RootDir, folder));
        int references = 0;
        foreach (string loadFolder in content.Catalog.FoldersFor(mod, content.Catalog.Full))
        {
            foreach (string kind in new[] { "Defs", "Patches" })
            {
                foreach (LoadedFile file in LoadFolderResolver.Files([loadFolder], kind, XmlCorpus.IsXml))
                {
                    bool inside = Path.GetFullPath(file.FullPath).StartsWith(folderPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                    XmlDocument? document = TryLoad(file.FullPath);
                    foreach (XmlElement leaf in document?.SelectNodes("//*[not(*)]")?.OfType<XmlElement>() ?? [])
                    {
                        string value = leaf.InnerText.Trim().Replace('\\', '/');
                        if (!value.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        references++;
                        string where = XmlPaths.File(mod.RootDir, file.FullPath);
                        if (!inside)
                        {
                            recorder.Fail($"{where}: {value}", $"Reference to '{prefix}' content outside {folder}: {reason}");
                            continue;
                        }

                        string asset = Path.Combine(folderPath, "Textures", value.Replace('/', Path.DirectorySeparatorChar));
                        if (!Directory.Exists(asset) || !Directory.EnumerateFiles(asset, "*.png", SearchOption.AllDirectories).Any())
                        {
                            if (!File.Exists(asset + ".png"))
                            {
                                recorder.Fail($"{where}: {value}", $"No textures at {folder}/Textures/{value}.");
                            }
                        }
                    }
                }
            }
        }

        foreach (string textures in content.Catalog.FoldersFor(mod, content.Catalog.Full).Select(f => Path.Combine(f, "Textures", prefix.TrimEnd('/'))))
        {
            if (Directory.Exists(textures) && !Path.GetFullPath(textures).StartsWith(folderPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                recorder.Fail(XmlPaths.File(mod.RootDir, textures), $"'{prefix}' textures outside {folder}: {reason}");
            }
        }

        recorder.Info(prefix, $"{references} reference(s) to '{prefix}' content, all expected in {folder}.");
    }

    // { width, height, roots: [...], except: [...], reason }：PNG 头中的尺寸。
    private static void CheckPngSizes(CheckRecorder recorder, string modRoot, JsonElement rule)
    {
        int width = rule.TryGetProperty("width", out JsonElement w) ? w.GetInt32() : 0;
        int height = rule.TryGetProperty("height", out JsonElement h) ? h.GetInt32() : 0;
        List<string> except = JsonBaseline.Strings(rule, "except");
        string reason = JsonBaseline.String(rule, "reason") ?? "";
        int checkedFiles = 0;
        foreach (string root in JsonBaseline.Strings(rule, "roots").Select(r => Path.Combine(modRoot, r)).Where(Directory.Exists))
        {
            foreach (string file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
            {
                string relative = XmlPaths.File(modRoot, file);
                if (except.Any(prefix => relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                checkedFiles++;
                if (PngSize(file) is var (fileWidth, fileHeight) && fileWidth == width && fileHeight == height)
                {
                    recorder.Fail(relative, $"{width}x{height} texture: {reason}");
                }
            }
        }

        recorder.Info($"{width}x{height}", $"{checkedFiles} PNG header(s) checked.");
    }

    // 9.20 服装资源契约：只限成人；身体层按体型后缀、头部层不加后缀，每个方向（及遮罩）的贴图都存在。
    private static void CheckWornApparel(CheckRecorder recorder, ContentModel content, JsonElement rule)
    {
        string file = JsonBaseline.String(rule, "file") ?? "";
        string stage = JsonBaseline.String(rule, "developmentalStageFilter") ?? "";
        string bodySuffix = JsonBaseline.String(rule, "bodySuffix") ?? "";
        List<string> headParents = JsonBaseline.Strings(rule, "headParents");
        List<string> headLayers = JsonBaseline.Strings(rule, "headLayers");
        List<string> directions = JsonBaseline.Strings(rule, "directions");
        Dictionary<string, List<string>> extra = rule.TryGetProperty("extraDirections", out JsonElement extraElement) && extraElement.ValueKind == JsonValueKind.Object
            ? extraElement.EnumerateObject().ToDictionary(p => p.Name, p => JsonBaseline.Strings(extraElement, p.Name), StringComparer.Ordinal)
            : [];
        ContentPack mod = content.Catalog.UnderTest;
        string fullPath = Path.GetFullPath(Path.Combine(mod.RootDir, file));
        int textures = 0;
        foreach (XmlElement node in content.Corpus.Defs.Where(n => content.Corpus.SourceOf(n) is { InsertedByPatch: false } s
                     && string.Equals(Path.GetFullPath(s.File), fullPath, StringComparison.OrdinalIgnoreCase) && n["defName"] is not null))
        {
            XmlElement resolved = content.Inheritance.Resolve(node);
            string defName = node["defName"]!.InnerText.Trim();
            XmlElement? apparel = resolved["apparel"];
            if (apparel?["developmentalStageFilter"]?.InnerText.Trim() != stage)
            {
                recorder.Fail(defName, $"9.20 apparel must have developmentalStageFilter {stage}.", file);
            }

            string? worn = apparel?["wornGraphicPath"]?.InnerText.Trim();
            if (string.IsNullOrEmpty(worn))
            {
                continue;
            }

            List<string> layers = apparel!["layers"]?.ChildNodes.OfType<XmlElement>().Select(li => li.InnerText.Trim()).ToList() ?? [];
            bool head = headParents.Contains(node.GetAttribute("ParentName"), StringComparer.Ordinal) || layers.Any(layer => headLayers.Contains(layer, StringComparer.Ordinal));
            bool mask = apparel["useWornGraphicMask"]?.InnerText.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) == true;
            foreach (string direction in directions.Concat(extra.GetValueOrDefault(defName) ?? []))
            {
                foreach (string maskSuffix in mask ? new[] { "", "m" } : [""])
                {
                    textures++;
                    string texture = $"{worn}{(head ? "" : bodySuffix)}_{direction}{maskSuffix}";
                    if (content.Textures.Find(texture) is TextureMatch.Missing or TextureMatch.ContentCaseMismatch)
                    {
                        recorder.Fail($"{defName}: {texture}", "Worn apparel texture missing (direction, body type and mask of the 9.20 contract).", file);
                    }
                }
            }
        }

        recorder.Info(file, $"{textures} worn texture path(s) checked.");
    }

    private static void CheckGovernance(CheckRecorder recorder, ContentModel content)
    {
        ContentPack mod = content.Catalog.UnderTest;
        int files = 0;
        foreach ((ContentPack pack, LoadedFile file) in content.Corpus.PatchFiles.Where(p => ContentModel.IsOurs(p.Pack)))
        {
            string text = File.ReadAllText(file.FullPath);
            if (!PatchRootPattern().IsMatch(text))
            {
                continue;
            }

            files++;
            if (!GovernancePattern().IsMatch(text))
            {
                recorder.Fail(XmlPaths.File(pack.RootDir, file.FullPath), "The patch file must start with a <!-- PatchGovernance: ... --> comment naming Targets, Scope, Duplicate guard and Failure (README).");
            }
        }

        recorder.Summary = $"{files} patch file(s) of {mod.PackageId} checked";
    }

    private static void CheckForbiddenPatterns(CheckRecorder recorder, ValidationOptions options, ContentModel content)
    {
        string path = Path.Combine(options.BaselineDir, ForbiddenPatternsFile);
        var patterns = new List<(Regex Regex, string Reason)>();
        foreach (string raw in File.Exists(path) ? File.ReadLines(path) : [])
        {
            string line = raw.TrimEnd();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int tab = line.LastIndexOf('\t');
            if (tab <= 0 || line[(tab + 1)..].Trim().Length == 0)
            {
                recorder.Fail("Baselines/" + ForbiddenPatternsFile, $"'{line}' has no reason (format: regex<TAB>reason).");
                continue;
            }

            patterns.Add((new Regex(line[..tab], RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)), line[(tab + 1)..].Trim()));
        }

        ContentPack mod = content.Catalog.UnderTest;
        var roots = new List<string>(content.Catalog.FoldersFor(mod, content.Catalog.Full)
            .Where(f => Directory.Exists(f) && !string.Equals(Path.GetFullPath(f).TrimEnd(Path.DirectorySeparatorChar), mod.RootDir, StringComparison.OrdinalIgnoreCase)));
        if (Directory.Exists(options.SourceDir))
        {
            roots.Add(options.SourceDir);
        }

        // 1.7 目录包含 1.7/Mods/* 条件目录，同一文件只扫一次。
        var scanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int files = 0;
        int exempted = 0;
        foreach (string root in roots)
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(f => TextExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
            {
                string normalized = file.Replace('\\', '/');
                if (normalized.Contains("/bin/", StringComparison.Ordinal) || normalized.Contains("/obj/", StringComparison.Ordinal) || !scanned.Add(Path.GetFullPath(file)))
                {
                    continue;
                }

                // 升级层的旧名表本来就要写旧名（笔误、旧前缀）：只有 Source/MugirlRace/SaveUpgrade/ 可以。
                if (root == options.SourceDir && Path.GetRelativePath(options.SourceDir, file).Replace('\\', '/').StartsWith(StructureChecks.UpgradeFolder, StringComparison.Ordinal))
                {
                    exempted++;
                    continue;
                }

                files++;
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    foreach ((Regex regex, string reason) in patterns)
                    {
                        if (regex.Match(lines[i]) is { Success: true } match)
                        {
                            recorder.Fail($"{XmlPaths.File(mod.RootDir, file)}:{i + 1}: {match.Value}", reason);
                        }
                    }
                }
            }
        }

        recorder.Summary = $"{patterns.Count} pattern(s) over {files} file(s)" + (exempted > 0 ? $" ({exempted} upgrade-layer file(s) exempt)" : "");
    }

    private static IEnumerable<(string Key, string Reason)> Map(CheckRecorder recorder, JsonElement root, string property)
        => root.TryGetProperty(property, out JsonElement element)
            ? JsonBaseline.ReasonMap(recorder, ContractsFile, element, property + ".").Select(pair => (pair.Key, pair.Value))
            : [];

    private static IEnumerable<JsonElement> Array(JsonElement root, string property)
        => root.TryGetProperty(property, out JsonElement element) && element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().ToList() : [];

    // "a/{x,y}/b_{1,2}" -> 四个路径（可嵌套多组，不支持嵌套括号）。
    internal static IEnumerable<string> ExpandBraces(string pattern)
    {
        int open = pattern.IndexOf('{', StringComparison.Ordinal);
        int close = open < 0 ? -1 : pattern.IndexOf('}', open);
        if (open < 0 || close < 0)
        {
            return [pattern];
        }

        return pattern[(open + 1)..close].Split(',')
            .SelectMany(option => ExpandBraces(pattern[..open] + option + pattern[(close + 1)..]));
    }

    // 相对 Mod 根目录的简单通配：段内 *，跨目录 **。
    private static IEnumerable<string> Glob(string modRoot, string pattern)
    {
        string fixedPart = pattern.Split('*', 2)[0];
        string baseDir = Path.Combine(modRoot, fixedPart.Contains('/', StringComparison.Ordinal) ? fixedPart[..fixedPart.LastIndexOf('/')] : "");
        if (!pattern.Contains('*', StringComparison.Ordinal))
        {
            return File.Exists(Path.Combine(modRoot, pattern)) || Directory.Exists(Path.Combine(modRoot, pattern)) ? [pattern] : [];
        }

        if (!Directory.Exists(baseDir))
        {
            return [];
        }

        var regex = new Regex("^" + Regex.Escape(pattern).Replace(@"\*\*", "\u0001", StringComparison.Ordinal).Replace(@"\*", "[^/]*", StringComparison.Ordinal)
            .Replace("\u0001", ".*", StringComparison.Ordinal) + "$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return Directory.EnumerateFileSystemEntries(baseDir, "*", SearchOption.AllDirectories)
            .Select(path => XmlPaths.File(modRoot, path))
            .Where(path => regex.IsMatch(path))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static (int Width, int Height)? PngSize(string file)
    {
        Span<byte> header = stackalloc byte[24];
        using FileStream stream = File.OpenRead(file);
        if (stream.Read(header) != 24)
        {
            return null;
        }

        return ((header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19], (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23]);
    }

    private static XmlDocument? TryLoad(string path)
    {
        try
        {
            var document = new XmlDocument();
            document.Load(path);
            return document;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"<Patch\b", RegexOptions.CultureInvariant)]
    private static partial Regex PatchRootPattern();

    [GeneratedRegex(@"(?s)<Patch>\s*<!--\s*PatchGovernance:.*?Targets:.*?Scope:.*?Duplicate guard:.*?Failure:.*?-->", RegexOptions.CultureInvariant)]
    private static partial Regex GovernancePattern();
}
