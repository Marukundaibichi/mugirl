using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Mugirl.GameValidation.Core;

/// <summary>一次运行的参数，由 tools/Test-InGame.ps1 写入临时存档目录。</summary>
/// <remarks>
/// 参数文件同时是启用标记：只有命令行带 <c>-mugirlvalidate</c> 且 <c>-savedatafolder</c> 指向的目录里有这个文件时才接管游戏，
/// 玩家正常的存档目录中不会有它。参数不走命令行，因为 GenCommandLine 拒绝含第二个 '=' 的值，带空格的路径还要转义。
/// </remarks>
internal sealed class ValidationOptions
{
    public const string CommandLineSwitch = "mugirlvalidate";
    public const string FileName = "mugirl-gamevalidation.json";

    private const string LoadEntryPrefix = "load:";

    private ValidationOptions(string reportPath, IReadOnlyList<string> suites)
    {
        ReportPath = reportPath;
        Suites = suites;
    }

    /// <summary>报告文件；每条结果立即追加写入，进程中途退出也不丢失。</summary>
    public string ReportPath { get; }

    /// <summary>要运行的套件：套件名、"default" 或 "all"。</summary>
    public IReadOnlyList<string> Suites { get; }

    /// <summary>被测构建的 packageId（<see cref="MugirlTarget"/> 按程序集名识别后核对它）。</summary>
    public string TargetPackageId { get; private init; } = MugirlTarget.DefaultPackageId;

    /// <summary>被测 Mod 必须从这个绝对路径加载（null 表示不检查）。创意工坊副本 packageId 相同，需要区分。</summary>
    public string? ExpectedModRoot { get; private init; }

    /// <summary>脚本确认的 1.7/Assemblies/MugirlRace.dll 的 SHA-256（十六进制，null 表示不检查）。</summary>
    public string? ExpectedModDllSha256 { get; private init; }

    /// <summary>为 false 时不截图，截图前后的检查照常运行。</summary>
    public bool Screenshots { get; private init; } = true;

    /// <summary>整次运行的实时上限（秒）；脚本的进程超时更长，用于兜底。</summary>
    public int TimeoutSeconds { get; private init; } = 600;

    /// <summary>运行标识（脚本的时间戳），写在报告开头。</summary>
    public string RunId { get; private init; } = "-";

    /// <summary>进入游戏的方式；参数 <c>entry</c> 为 "quicktest"（默认）或 "load:&lt;存档名&gt;"。</summary>
    public EntryMode Entry { get; private init; } = EntryMode.QuickTest;

    /// <summary><see cref="EntryMode.LoadSave"/> 时从主菜单读取的存档名。</summary>
    public string? EntrySave { get; private init; }

    /// <summary>同一夹具的第几次启动（1 = 读旧存档，2 = 新进程读第 1 次的重存档）。</summary>
    public int Boot { get; private init; } = 1;

    /// <summary>本次启动的套件写重存档时使用的存档名（null 时由套件自定）。</summary>
    public string? ResaveName { get; private init; }

    /// <summary>本次运行可用的存档夹具。</summary>
    public IReadOnlyList<FixtureRef> Fixtures { get; private init; } = [];

    /// <summary>套件写机器可读结果（JSON、事实表）的目录；脚本把它复制到结果目录。null 时不写。</summary>
    public string? ArtifactsDir { get; private init; }

    /// <summary>旧名单（每行一个记号）：升级后的存档中不得再出现。null 时由套件从升级清单推导。</summary>
    public string? TokenSetPath { get; private init; }

    /// <summary>同一存档的离线升级报告（--save-corpus 运行），用于核对每步的动作数。</summary>
    public string? OfflineReportPath { get; private init; }

    /// <summary>验证器的旧版存档语料目录（Baselines/legacy，只读）：旧 Def 的组件与各类型的 Scribe 标签。</summary>
    public string? LegacyCorpusDir { get; private init; }

    /// <summary>补充的日志白名单（正则）；评审过的原版底噪见 <see cref="LogPolicy"/>。</summary>
    public IReadOnlyList<string> LogAllowlist { get; private init; } = [];

    /// <summary>只属于未加载 Mod 的记号；只含这些记号的错误归为 "foreign"。</summary>
    public IReadOnlyList<string> ForeignTokens { get; private init; } = [];

    /// <summary>套件自己的参数（键名带套件前缀，例如 <c>sweep.ticks</c>）。</summary>
    public IReadOnlyDictionary<string, string> SuiteOptions { get; private init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public static string FilePath => Path.Combine(GenFilePaths.SaveDataFolderPath, FileName);

    /// <summary><see cref="FixtureRef"/> 中存档名等于 <paramref name="saveName"/> 的夹具；没有时取第一个。</summary>
    public FixtureRef? FixtureFor(string? saveName)
        => Fixtures.FirstOrDefault(f => string.Equals(f.SaveName, saveName, StringComparison.OrdinalIgnoreCase)) ?? Fixtures.FirstOrDefault();

    public string SuiteOption(string key, string fallback)
        => SuiteOptions.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    public int SuiteOption(string key, int fallback)
        => SuiteOptions.TryGetValue(key, out string? value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;

    public static ValidationOptions? TryLoad(out string problem)
    {
        string path = FilePath;
        if (!File.Exists(path))
        {
            problem = "no " + FileName + " in the save-data folder " + GenFilePaths.SaveDataFolderPath
                + "; start the game through tools/Test-InGame.ps1";
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            string? report = String(root, "reportPath");
            if (string.IsNullOrWhiteSpace(report))
            {
                problem = FileName + " has no reportPath";
                return null;
            }

            List<string> suites = Strings(root, "suites");
            if (suites.Count == 0)
            {
                suites.Add(SuiteCatalog.DefaultSelection);
            }

            string entry = String(root, "entry") ?? "quicktest";
            string? entrySave = null;
            EntryMode mode = EntryMode.QuickTest;
            if (entry.StartsWith(LoadEntryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                mode = EntryMode.LoadSave;
                entrySave = entry[LoadEntryPrefix.Length..].Trim();
                if (entrySave.Length == 0)
                {
                    problem = FileName + ": entry \"load:\" names no save";
                    return null;
                }
            }
            else if (!string.Equals(entry, "quicktest", StringComparison.OrdinalIgnoreCase))
            {
                problem = $"{FileName}: unknown entry \"{entry}\" (expected quicktest or load:<save name>)";
                return null;
            }

            problem = string.Empty;
            return new ValidationOptions(report, suites)
            {
                TargetPackageId = String(root, "targetPackageId") is { Length: > 0 } target ? target : MugirlTarget.DefaultPackageId,
                ExpectedModRoot = String(root, "expectedModRoot"),
                ExpectedModDllSha256 = String(root, "expectedModDllSha256"),
                Screenshots = !root.TryGetProperty("screenshots", out JsonElement shots) || shots.ValueKind != JsonValueKind.False,
                TimeoutSeconds = Int(root, "timeoutSeconds") is > 0 and int seconds ? seconds : 600,
                RunId = String(root, "runId") ?? "-",
                Entry = mode,
                EntrySave = entrySave,
                Boot = Int(root, "boot") is > 0 and int boot ? boot : 1,
                ResaveName = String(root, "resaveName"),
                Fixtures = ReadFixtures(root),
                ArtifactsDir = String(root, "artifactsDir"),
                TokenSetPath = String(root, "tokenSetPath"),
                OfflineReportPath = String(root, "offlineReportPath"),
                LegacyCorpusDir = String(root, "legacyCorpusDir"),
                LogAllowlist = Strings(root, "logAllowlist"),
                ForeignTokens = Strings(root, "foreignTokens"),
                SuiteOptions = ReadSuiteOptions(root),
            };
        }
        catch (Exception ex)
        {
            problem = "cannot read " + path + ": " + ex.Message;
            return null;
        }
    }

    public override string ToString()
        => $"run {RunId}, suites [{string.Join(", ", Suites)}], target {TargetPackageId}, entry {(Entry == EntryMode.LoadSave ? "load:" + EntrySave : "quicktest")}, "
            + $"boot {Boot}, screenshots {(Screenshots ? "on" : "off")}, timeout {TimeoutSeconds} s";

    private static string? String(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;

    // 数组或逗号分隔的字符串（PowerShell 的 ConvertTo-Json 会把单元素数组写成字符串）。
    private static List<string> Strings(JsonElement root, string name)
    {
        var result = new List<string>();
        if (!root.TryGetProperty(name, out JsonElement list))
        {
            return result;
        }

        IEnumerable<string?> items = list.ValueKind switch
        {
            JsonValueKind.Array => list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()),
            JsonValueKind.String => (list.GetString() ?? string.Empty).Split(','),
            _ => [],
        };
        foreach (string? item in items)
        {
            if (!string.IsNullOrWhiteSpace(item))
            {
                result.Add(item.Trim());
            }
        }

        return result;
    }

    private static List<FixtureRef> ReadFixtures(JsonElement root)
    {
        var fixtures = new List<FixtureRef>();
        if (!root.TryGetProperty("fixtures", out JsonElement list))
        {
            return fixtures;
        }

        IEnumerable<JsonElement> items = list.ValueKind switch
        {
            JsonValueKind.Array => list.EnumerateArray(),
            JsonValueKind.Object => [list],
            _ => [],
        };
        foreach (JsonElement item in items.Where(i => i.ValueKind == JsonValueKind.Object))
        {
            if (String(item, "id") is { Length: > 0 } id && String(item, "save") is { Length: > 0 } save)
            {
                string kindText = String(item, "kind") ?? FixtureKinds.Text(FixtureKind.Published);
                if (!FixtureKinds.TryParse(kindText, out FixtureKind kind))
                {
                    throw new FormatException($"fixture {id} has the unknown kind \"{kindText}\"");
                }

                fixtures.Add(new FixtureRef(id, save, String(item, "manifest"), kind, String(item, "gameVersion"), String(item, "variantManifest")));
            }
        }

        return fixtures;
    }

    private static Dictionary<string, string> ReadSuiteOptions(JsonElement root)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("suiteOptions", out JsonElement map) && map.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in map.EnumerateObject())
            {
                options[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.GetRawText();
            }
        }

        return options;
    }
}
