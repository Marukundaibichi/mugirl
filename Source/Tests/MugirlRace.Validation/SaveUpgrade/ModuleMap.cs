namespace MugirlRace.Validation.SaveUpgrade;

/// <summary>
/// <c>Baselines/save-upgrade-modules.tsv</c>：旧名归哪个重构模块（ratchet 文件按模块分开，Wave S 各模块只改自己的文件）。
/// 查找顺序：旧类型名 / 旧 Def，然后当前类型的源码目录或当前 Def 的内容文件（最长前缀），否则 <c>unassigned</c>。
/// </summary>
internal sealed class ModuleMap
{
    public const string FileName = "save-upgrade-modules.tsv";
    public const string Unassigned = "unassigned";

    private readonly Dictionary<string, string> types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> defs = new(StringComparer.Ordinal);
    private readonly List<(string Prefix, string Module)> paths = [];

    private ModuleMap()
    {
    }

    /// <summary>格式错误的行。</summary>
    public List<string> Problems { get; } = [];

    public static ModuleMap Load(string baselineDir)
    {
        var map = new ModuleMap();
        string path = Path.Combine(baselineDir, FileName);
        if (!File.Exists(path))
        {
            return map;
        }

        foreach (string line in Legacy.LegacyCorpus.DataLines(path))
        {
            string[] parts = line.Split('\t');
            if (parts.Length != 3)
            {
                map.Problems.Add($"'{line}' needs kind<TAB>name<TAB>module");
                continue;
            }

            switch (parts[0])
            {
                case "type":
                    map.types[parts[1]] = parts[2];
                    break;
                case "def":
                    // 语料按数据库根的短名记录 Def（FacialAnimation.EmotionShapeDef → EmotionShapeDef）。
                    int slash = parts[1].IndexOf('/', StringComparison.Ordinal);
                    string key = slash > 0 ? parts[1][(parts[1].LastIndexOf('.', slash) + 1)..] : parts[1];
                    map.defs[key] = parts[2];
                    break;
                case "path":
                    map.paths.Add((parts[1], parts[2]));
                    break;
                default:
                    map.Problems.Add($"'{line}': unknown kind '{parts[0]}'");
                    break;
            }
        }

        map.paths.Sort((a, b) => b.Prefix.Length.CompareTo(a.Prefix.Length));
        return map;
    }

    public string? OfType(string legacyType)
    {
        // 嵌套类型归其外层类型。
        string outer = legacyType.Split('+')[0];
        return types.GetValueOrDefault(legacyType) ?? types.GetValueOrDefault(outer);
    }

    public string? OfDef(string root, string defName) => defs.GetValueOrDefault(root + "/" + defName);

    /// <summary>源码路径（相对 Source/MugirlRace）或内容文件（相对 Mod 根目录）所属的模块。</summary>
    public string? OfPath(string? path)
        => path is null ? null : paths.FirstOrDefault(p => path.StartsWith(p.Prefix, StringComparison.Ordinal)).Module;
}
