namespace MugirlRace.Validation.Legacy;

/// <summary>
/// 存档中本 Mod 元素（带 Class）的直接子元素名归谁：只有本 Mod 代码写的标签才进语料。按顺序判断：
/// <list type="number">
/// <item>元素类型或其本 Mod 祖先在某个旧构建中静态写这个标签：记在那个 owner 上（<c>label</c>，与 legacy-labels 同一个键）。</item>
/// <item>游戏或依赖程序集在 Scribe 调用中写这个字面量标签（<c>def</c>、<c>id</c>、原版组件的标签）：不属于本 Mod，不记。</item>
/// <item>某个就地写入父元素的旧 Mod 类型（组件等，没有存档根）写这个标签：记在每个这样的 owner 上。</item>
/// <item>元素类型链上有无法求值的标签：记在元素类型上（<c>label</c>，必需）。</item>
/// <item>否则来源不明（其他 Mod 的组件、验证构建）：记为 <c>label?</c>，只供审阅，不要求处置。</item>
/// </list>
/// </summary>
internal sealed class ObservedLabelClassifier
{
    private readonly Dictionary<string, HashSet<string>> labelsByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> chains = new(StringComparer.Ordinal);
    private readonly HashSet<string> dynamicOwners = new(StringComparer.Ordinal);
    private readonly SortedSet<string> inlineOwners = new(StringComparer.Ordinal);
    private readonly HashSet<string> foreignLabels = new(StringComparer.Ordinal);

    /// <summary>加入一个旧构建的标签、类型链、动态 owner 与存档根。</summary>
    public void AddBuild(IReadOnlyList<string[]> labels, IReadOnlyDictionary<string, List<string>> buildChains, IEnumerable<string> buildDynamicOwners, IReadOnlyList<string[]> types)
    {
        foreach (string[] row in labels)
        {
            if (!labelsByOwner.TryGetValue(row[0], out HashSet<string>? set))
            {
                labelsByOwner[row[0]] = set = new HashSet<string>(StringComparer.Ordinal);
            }

            set.Add(row[1]);
        }

        foreach ((string type, List<string> chain) in buildChains)
        {
            if (!chains.TryGetValue(type, out List<string>? known))
            {
                chains[type] = known = [];
            }

            foreach (string name in chain.Where(name => !known.Contains(name, StringComparer.Ordinal)))
            {
                known.Add(name);
            }
        }

        dynamicOwners.UnionWith(buildDynamicOwners);
        foreach (string[] row in types.Where(row => row[2] == "-" && row[1] == "class"))
        {
            inlineOwners.Add(row[0]);
        }
    }

    /// <summary>游戏与依赖写成字面量的标签（见 <see cref="ScribeLabelLiterals"/>）。</summary>
    public void AddForeignLabels(IEnumerable<string> labels) => foreignLabels.UnionWith(labels);

    /// <summary>元素类型 <paramref name="owner"/> 下的子元素 <paramref name="label"/> 的语料行（kind、标签、owner）；不属于本 Mod 时为空。</summary>
    public IEnumerable<(string Kind, string Label, string Owner)> Classify(string label, string owner)
    {
        List<string> chain = chains.TryGetValue(owner, out List<string>? known) ? known : [owner];
        if (chain.FirstOrDefault(type => Writes(type, label)) is { } writer)
        {
            return [("label", label, writer)];
        }

        if (foreignLabels.Contains(label))
        {
            return [];
        }

        List<(string, string, string)> inline = inlineOwners.Where(type => Writes(type, label)).Select(type => ("label", label, type)).ToList();
        if (inline.Count > 0)
        {
            return inline;
        }

        return chain.Any(dynamicOwners.Contains) ? [("label", label, owner)] : [("label?", label, owner)];
    }

    private bool Writes(string owner, string label) => labelsByOwner.TryGetValue(owner, out HashSet<string>? set) && set.Contains(label);
}
