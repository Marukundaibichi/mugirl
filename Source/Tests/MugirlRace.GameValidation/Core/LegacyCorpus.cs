using System.IO;

namespace Mugirl.GameValidation.Core;

/// <summary>
/// 验证器的旧版存档语料（<c>Source/Tests/MugirlRace.Validation/Baselines/legacy</c>，只读）中游戏内验证需要的部分：
/// 旧 Def 带有哪些组件（<c>legacy-def-comps.tsv</c>）、各类型写出哪些 Scribe 标签（<c>legacy-labels.tsv</c>）以及类型在 Mod 内的直接基类
/// （<c>legacy-types.tsv</c>，用来补上从基类继承的标签）。
/// </summary>
/// <remarks>组件的标签直接写在物品或健康状态节点里，与原版和其他 Mod 的标签同在一个元素中；只有按这份语料才能知道哪些子元素属于雪牛娘。</remarks>
internal sealed class LegacyCorpus
{
    public const string DefCompsFile = "legacy-def-comps.tsv";
    public const string LabelsFile = "legacy-labels.tsv";
    public const string TypesFile = "legacy-types.tsv";

    private readonly Dictionary<(string Root, string Def), HashSet<string>> comps = [];
    private readonly Dictionary<string, HashSet<string>> labels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> modBases = new(StringComparer.Ordinal);

    private LegacyCorpus()
    {
    }

    /// <summary>空语料：没有任何组件与标签。</summary>
    public static LegacyCorpus Empty { get; } = new();

    public int DefCompRows { get; private set; }

    public int LabelRows { get; private set; }

    /// <summary>旧 Def 的全部 (根类型, defName)。</summary>
    public IEnumerable<(string Root, string Def)> Defs => comps.Keys;

    /// <summary>读取 <paramref name="folder"/> 中的三个文件；缺文件时返回 null 并说明。</summary>
    public static LegacyCorpus? TryLoad(string? folder, out string problem)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            problem = "the legacy corpus folder is not available: " + (folder ?? "(not given)");
            return null;
        }

        string[] files = [DefCompsFile, LabelsFile, TypesFile];
        string? missing = files.Select(name => Path.Combine(folder, name)).FirstOrDefault(path => !File.Exists(path));
        if (missing is not null)
        {
            problem = "missing " + missing;
            return null;
        }

        problem = string.Empty;
        return Parse(
            File.ReadAllText(Path.Combine(folder, DefCompsFile)),
            File.ReadAllText(Path.Combine(folder, LabelsFile)),
            File.ReadAllText(Path.Combine(folder, TypesFile)));
    }

    /// <summary>从三个 TSV 文件的文本构造（列见各文件的表头注释）。</summary>
    public static LegacyCorpus Parse(string defComps, string labels, string types)
    {
        var corpus = new LegacyCorpus();
        foreach (string[] row in Rows(defComps, 4))
        {
            // root defName propsClass compClass via …
            if (!corpus.comps.TryGetValue((row[0], row[1]), out HashSet<string>? set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                corpus.comps[(row[0], row[1])] = set;
            }

            set.Add(row[3]);
            corpus.DefCompRows++;
        }

        foreach (string[] row in Rows(labels, 2))
        {
            // owner label scribeKind …
            if (!corpus.labels.TryGetValue(row[0], out HashSet<string>? set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                corpus.labels[row[0]] = set;
            }

            set.Add(row[1]);
            corpus.LabelRows++;
        }

        foreach (string[] row in Rows(types, 4))
        {
            // type kind saveRoot modBase …
            if (row[3] is { Length: > 0 } modBase && modBase != "-")
            {
                corpus.modBases[row[0]] = modBase;
            }
        }

        return corpus;
    }

    /// <summary>旧构建中 (根类型, defName) 的 Def 带有的组件类（旧全名）。</summary>
    public IReadOnlyCollection<string> CompsOf(string root, string defName)
        => comps.TryGetValue((root, defName), out HashSet<string>? set) ? set : [];

    /// <summary>旧类型 <paramref name="ownerType"/> 写出的 Scribe 标签，含 Mod 内基类写出的。</summary>
    public IReadOnlySet<string> LabelsOf(string ownerType)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? type = ownerType; type is not null && seen.Add(type); type = modBases.GetValueOrDefault(type))
        {
            if (labels.TryGetValue(type, out HashSet<string>? own))
            {
                result.UnionWith(own);
            }
        }

        return result;
    }

    private static IEnumerable<string[]> Rows(string text, int minimumColumns)
    {
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            string[] columns = trimmed.Split('\t');
            if (columns.Length >= minimumColumns)
            {
                yield return columns;
            }
        }
    }
}
