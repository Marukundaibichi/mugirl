using System.Text.Json;
using System.Text.RegularExpressions;

namespace MugirlRace.Validation.SaveUpgrade;

/// <summary>
/// 升级器的声明清单（契约 K1）：<c>Mugirl.SaveUpgrade.SaveUpgradeManifest.ToJson()</c> 返回的 JSON。只读数据；读取时不运行游戏代码。
/// </summary>
internal sealed class UpgradeManifest
{
    private UpgradeManifest()
    {
    }

    public int Schema { get; private init; }

    public string StampLabel { get; private init; } = "";

    public string StampType { get; private init; } = "";

    public IReadOnlyList<ManifestEntry> Types { get; private init; } = [];

    public IReadOnlyList<ManifestEntry> Defs { get; private init; } = [];

    public IReadOnlyList<ManifestEntry> Labels { get; private init; } = [];

    public IReadOnlyList<ManifestEntry> Enums { get; private init; } = [];

    public IReadOnlyList<ManifestEntry> Values { get; private init; } = [];

    /// <summary><c>defaults</c>：旧默认值的物化（Old = oldDefault，Owner/Label 为所属）。</summary>
    public IReadOnlyList<ManifestEntry> Defaults { get; private init; } = [];

    public IReadOnlyList<ManifestStep> Steps { get; private init; } = [];

    /// <summary><c>jobPolicies</c>：Old = 类型，Policy = endInFlight、preserve 或 claimed:&lt;stepId&gt;。</summary>
    public IReadOnlyList<ManifestEntry> JobPolicies { get; private init; } = [];

    /// <summary><c>lordPolicies</c>：Old = 类型，Policy = resetGraph、removeLord、preserve 或 claimed:&lt;stepId&gt;。</summary>
    public IReadOnlyList<ManifestEntry> LordPolicies { get; private init; } = [];

    public (string Old, string New)? SettingsClass { get; private init; }

    public IReadOnlyList<ManifestEntry> SettingsLabels { get; private init; } = [];

    public int? SettingsSchema { get; private init; }

    public IReadOnlyList<string> MooGirlTypePrefixes { get; private init; } = [];

    public IReadOnlyList<Regex> MooGirlDefPatterns { get; private init; } = [];

    /// <summary>解析时发现的格式问题（缺字段、未知取值、无效正则）。</summary>
    public IReadOnlyList<string> Problems { get; private init; } = [];

    public static UpgradeManifest Parse(string json)
    {
        var problems = new List<string>();
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        JsonElement root = document.RootElement;
        int manifestVersion = root.TryGetProperty("manifestVersion", out JsonElement version) && version.TryGetInt32(out int parsedVersion) ? parsedVersion : 1;
        if (manifestVersion is < 1 or > 2) { problems.Add($"Unsupported manifestVersion {manifestVersion}."); }
        var patterns = new List<Regex>();
        foreach (string pattern in Strings(root, "moogirlEra", "defPatterns"))
        {
            try
            {
                // K1 v2 与运行时 GlobPattern 相同：只有星号是通配符，必须匹配完整名字。
                string expression = manifestVersion >= 2 ? "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$" : pattern;
                patterns.Add(new Regex(expression, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
            }
            catch (ArgumentException exception)
            {
                problems.Add($"moogirlEra.defPatterns: '{pattern}' is not a valid regular expression ({exception.Message}).");
            }
        }

        (string, string)? settingsClass = null;
        int? settingsSchema = null;
        List<ManifestEntry> settingsLabels = [];
        if (root.TryGetProperty("settings", out JsonElement settings) && settings.ValueKind == JsonValueKind.Object)
        {
            if (settings.TryGetProperty("fileClass", out JsonElement fileClass) && fileClass.ValueKind == JsonValueKind.Object)
            {
                settingsClass = (Text(fileClass, "old") ?? "", Text(fileClass, "new") ?? "");
            }

            settingsLabels = Entries(settings, "labels", "settings.labels", problems);
            settingsSchema = settings.TryGetProperty("schema", out JsonElement schema) && schema.TryGetInt32(out int value) ? value : null;
        }

        var manifest = new UpgradeManifest
        {
            Schema = root.TryGetProperty("schema", out JsonElement current) && current.TryGetInt32(out int number) ? number : -1,
            StampLabel = Text(root, "stampLabel") ?? "",
            StampType = Text(root, "stampType") ?? "",
            Types = Entries(root, "types", "types", problems),
            Defs = Entries(root, "defs", "defs", problems),
            Labels = Entries(root, "labels", "labels", problems),
            Enums = Entries(root, "enums", "enums", problems),
            Values = Entries(root, "values", "values", problems),
            Defaults = Entries(root, "defaults", "defaults", problems),
            Steps = StepsOf(root, problems),
            JobPolicies = Entries(root, "jobPolicies", "jobPolicies", problems),
            LordPolicies = Entries(root, "lordPolicies", "lordPolicies", problems),
            SettingsClass = settingsClass,
            SettingsLabels = settingsLabels,
            SettingsSchema = settingsSchema,
            MooGirlTypePrefixes = Strings(root, "moogirlEra", "typePrefixes"),
            MooGirlDefPatterns = patterns,
            Problems = problems,
        };
        foreach (ManifestEntry entry in manifest.Types.Concat(manifest.Defs).Concat(manifest.Labels))
        {
            if (entry.Disposition is not ("rename" or "drop" or "consumed"))
            {
                problems.Add($"{entry.Section}[{entry.Index}] ({entry.Old}): disposition must be rename, drop or consumed (labels only), not '{entry.Disposition}'.");
            }
        }

        return manifest;
    }

    private static List<ManifestEntry> Entries(JsonElement root, string property, string section, List<string> problems)
    {
        var entries = new List<ManifestEntry>();
        if (!root.TryGetProperty(property, out JsonElement array))
        {
            return entries;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{section}: expected an array.");
            return entries;
        }

        int index = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{section}[{index}]: expected an object.");
            }
            else
            {
                entries.Add(new ManifestEntry(section, index,
                    Old: Text(item, "old") ?? Text(item, "oldDefault") ?? Text(item, "type") ?? "",
                    New: Text(item, "new"),
                    Disposition: Text(item, "disposition") ?? (Text(item, "new") is null ? "drop" : "rename"),
                    Policy: Text(item, "policy"),
                    Module: Text(item, "module") ?? "",
                    Reason: Text(item, "reason") ?? "",
                    Root: Text(item, "root"),
                    Owner: Text(item, "owner") ?? Text(item, "enum") ?? Text(item, "context"),
                    Label: Text(item, "label"),
                    Step: Text(item, "step"),
                    LegacyRoot: Text(item, "legacyRoot")));
            }

            index++;
        }

        return entries;
    }

    private static List<ManifestStep> StepsOf(JsonElement root, List<string> problems)
    {
        var steps = new List<ManifestStep>();
        if (!root.TryGetProperty("steps", out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return steps;
        }

        foreach (JsonElement item in array.EnumerateArray())
        {
            string id = Text(item, "id") ?? "";
            string phase = Text(item, "phase") ?? "";
            if (id.Length == 0)
            {
                problems.Add("steps: a step without an id.");
            }

            if (phase is not ("dom" or "A" or "B" or "C"))
            {
                problems.Add($"steps[{id}]: phase must be dom, A, B or C, not '{phase}'.");
            }

            List<string> consumes = item.TryGetProperty("consumes", out JsonElement list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
                : [];
            steps.Add(new ManifestStep(id, phase, Text(item, "module") ?? "", consumes, Text(item, "reason") ?? ""));
        }

        return steps;
    }

    private static string? Text(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "True",
                JsonValueKind.False => "False",
                _ => null,
            }
            : null;

    private static List<string> Strings(JsonElement root, string parent, string property)
        => root.TryGetProperty(parent, out JsonElement element) && element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out JsonElement array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
            : [];
}
