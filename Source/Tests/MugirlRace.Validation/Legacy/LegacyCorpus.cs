namespace MugirlRace.Validation.Legacy;

/// <summary>
/// 旧存档名字语料（<c>Baselines/legacy/</c>）：文件与列的定义，以及 <c>C.save-upgrade</c> 用的读取器。
/// </summary>
internal sealed class LegacyCorpus
{
    public const string BuildsFile = "builds.tsv";
    public const string TypesFile = "legacy-types.tsv";
    public const string LabelsFile = "legacy-labels.tsv";
    public const string GraphFile = "legacy-schema-graph.tsv";
    public const string EnumsFile = "legacy-enums.tsv";
    public const string DefsFile = "legacy-defs.tsv";
    public const string DefCompsFile = "legacy-def-comps.tsv";
    public const string XmlTypesFile = "legacy-xml-types.tsv";
    public const string SavedValuesFile = "legacy-saved-values.tsv";
    public const string LoadIdsFile = "legacy-load-ids.tsv";
    public const string SettingsFile = "legacy-settings.tsv";
    public const string JobsLordsFile = "legacy-jobs-lords.tsv";
    public const string ObservedFile = "legacy-observed.tsv";
    public const string ToolsFile = "legacy-tools.tsv";
    public const string ExtraFile = "legacy-extra.tsv";

    /// <summary>
    /// 提取器版本：派生事实的规则（存档根、标签归属、外来名字的排除）改变时加一。只追加适用于同一版本的输入；版本改变时语料用
    /// <c>--rewrite</c> 整体重写，差异经审阅后提交。
    /// </summary>
    public const int ExtractorVersion = 3;

    /// <summary>builds.tsv 中记录版本的注释行前缀。</summary>
    public const string VersionPrefix = "# extractor version: ";

    private readonly Dictionary<string, List<CorpusRow>> rows;

    private LegacyCorpus(string directory, IReadOnlyList<BuildInfo> builds, Dictionary<string, List<CorpusRow>> rows, IReadOnlyList<string> problems)
    {
        Directory = directory;
        Builds = builds;
        this.rows = rows;
        Problems = problems;
    }

    public string Directory { get; }

    public IReadOnlyList<BuildInfo> Builds { get; }

    /// <summary>格式错误的行等问题。</summary>
    public IReadOnlyList<string> Problems { get; }

    public int RowCount => rows.Values.Sum(list => list.Count);

    /// <summary>提取器生成的全部表（不含 builds.tsv 与手写的 legacy-extra.tsv）。</summary>
    public static IReadOnlyList<CorpusTable> NewTables() =>
    [
        new(TypesFile, "every type of every legacy build with the save root it is written under (Class=; - = never written by class name), its direct base in the mod, the first base outside the mod and flags",
            "type", "kind", "saveRoot", "modBase", "gameBase", "flags"),
        new(LabelsFile, "Scribe labels per owner type: kind, value type, LookMode, default value text, forceSave",
            "owner", "label", "scribeKind", "valueType", "lookMode", "default", "forceSave"),
        new(GraphFile, "schema graph: mod types saved Deep under an owner label", "owner", "label", "elementType"),
        new(EnumsFile, "members of enums written into saves (Scribe writes the member name)", "enum", "member", "value"),
        new(DefsFile, "non-abstract defs of every legacy content tree (database root, defName, def class), incl. defs the game generates from them",
            "root", "defName", "defClass"),
        new(DefCompsFile, "legacy def -> comp relation (comp labels live inline in the def's thing/hediff node)", "root", "defName", "propsClass", "compClass", "via"),
        new(XmlTypesFile, "mod types referenced from legacy XML (Class attributes, *Class/*Type fields, def elements)", "type", "usage"),
        new(SavedValuesFile, "literals stored into string fields that are saved with Scribe_Values (keyed = a Keyed key of that build)", "field", "value", "valueKind"),
        new(LoadIdsFile, "GetUniqueLoadID literals per type (load IDs are saved in references)", "type", "literals"),
        new(SettingsFile, "mod settings class, labels, value types and defaults", "settingsClass", "label", "valueType", "default"),
        new(JobsLordsFile, "JobDriver and LordJob types (toil order and lord graphs are index-based state)", "family", "type", "abstract"),
        new(ObservedFile, "Mugirl-owned tokens found in real saves (exact corpus names; label = written by Mugirl code, label? = origin unknown, review only)", "kind", "token", "context"),
        new(ToolsFile, "tool ids of legacy ThingDefs (verb load IDs embed them: <ownerID>_<toolId>_<ManeuverDef>)", "thingDef", "toolId"),
    ];

    /// <summary>读取 <paramref name="directory"/> 下的语料；目录或 builds.tsv 不存在时返回 <see langword="null"/>。</summary>
    public static LegacyCorpus? Load(string directory)
    {
        string buildsPath = Path.Combine(directory, BuildsFile);
        if (!File.Exists(buildsPath))
        {
            return null;
        }

        var problems = new List<string>();
        var builds = new List<BuildInfo>();
        foreach (string line in DataLines(buildsPath))
        {
            if (BuildInfo.Parse(line) is { } build)
            {
                builds.Add(build);
            }
            else
            {
                problems.Add($"{BuildsFile}: malformed line '{line}'");
            }
        }

        var rows = new Dictionary<string, List<CorpusRow>>(StringComparer.Ordinal);
        foreach (CorpusTable table in NewTables())
        {
            var list = new List<CorpusRow>();
            rows[table.FileName] = list;
            string path = Path.Combine(directory, table.FileName);
            if (!File.Exists(path))
            {
                problems.Add($"{table.FileName}: missing");
                continue;
            }

            int width = table.Columns.Count + CorpusTable.AggregateColumns.Length;
            int n = table.Columns.Count;
            foreach (string line in DataLines(path))
            {
                string[] parts = line.Split('\t');
                if (parts.Length != width)
                {
                    problems.Add($"{table.FileName}: malformed line '{line}'");
                    continue;
                }

                list.Add(new CorpusRow(table.FileName, parts[..n], parts[n], parts[n + 1], parts[n + 2], parts[n + 3]));
            }
        }

        rows[ExtraFile] = ReadExtra(Path.Combine(directory, ExtraFile), problems);
        return new LegacyCorpus(directory, builds, rows, problems);
    }

    public IReadOnlyList<CorpusRow> Rows(string file) => rows.TryGetValue(file, out List<CorpusRow>? list) ? list : [];

    /// <summary>
    /// 语料中出现的全部旧名字：类型、defName、存进字符串字段的字面量（含旧 Keyed 键）与存档观测名字。升级层的表格以字符串写它们，
    /// 键形字面量规则不把它们当作翻译键。
    /// </summary>
    public IReadOnlySet<string> LegacyNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        names.UnionWith(Rows(TypesFile).Select(row => row[0]));
        names.UnionWith(Rows(DefsFile).Select(row => row[1]));
        names.UnionWith(Rows(SavedValuesFile).Select(row => row[1].Length >= 2 && row[1][0] == '"' ? System.Text.Json.JsonSerializer.Deserialize<string>(row[1]) ?? row[1] : row[1]));
        names.UnionWith(Rows(ObservedFile).Concat(Rows(ExtraFile)).Select(row => row[1]));
        return names;
    }

    /// <summary>builds.tsv 记录的提取器版本；没有版本行（第一版）时为 1，文件不存在时为 <see langword="null"/>。</summary>
    public static int? VersionOf(string directory)
    {
        string path = Path.Combine(directory, BuildsFile);
        if (!File.Exists(path))
        {
            return null;
        }

        foreach (string line in File.ReadLines(path).Take(8))
        {
            if (line.StartsWith(VersionPrefix, StringComparison.Ordinal)
                && int.TryParse(line[VersionPrefix.Length..].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int version))
            {
                return version;
            }
        }

        return 1;
    }

    /// <summary>非注释、非空的行（LF 或 CRLF）。</summary>
    public static IEnumerable<string> DataLines(string path)
        => File.ReadLines(path).Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0 && !line.StartsWith('#'));

    // legacy-extra.tsv：手写，只给真实存档或证据中出现、但任何构建都没有的名字。kind<TAB>name<TAB>context<TAB>evidence。
    private static List<CorpusRow> ReadExtra(string path, List<string> problems)
    {
        var list = new List<CorpusRow>();
        if (!File.Exists(path))
        {
            return list;
        }

        foreach (string line in DataLines(path))
        {
            string[] parts = line.Split('\t');
            if (parts.Length != 4 || parts[3].Trim().Length == 0)
            {
                problems.Add($"{ExtraFile}: '{line}' needs kind<TAB>name<TAB>context<TAB>evidence");
                continue;
            }

            list.Add(new CorpusRow(ObservedFile, parts[..3], "observed", "-", "extra", "extra"));
        }

        return list;
    }
}
