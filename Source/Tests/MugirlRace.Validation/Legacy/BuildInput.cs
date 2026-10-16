namespace MugirlRace.Validation.Legacy;

/// <summary>
/// 驱动脚本（tools/Export-LegacySchema.ps1）交给提取器的一个输入，即输入清单的一行：
/// <c>id&lt;TAB&gt;tier&lt;TAB&gt;kind&lt;TAB&gt;source&lt;TAB&gt;file&lt;TAB&gt;root&lt;TAB&gt;firstSeen&lt;TAB&gt;lastSeen</c>。
/// </summary>
/// <param name="Id">builds.tsv 中的 id。</param>
/// <param name="Tier">published、observed、dev 或 info。</param>
/// <param name="Kind">dll+content、dll、content 或 save。</param>
/// <param name="Source">可移植的来源描述。</param>
/// <param name="File">DLL 或存档文件（本机路径）；没有时为 <c>-</c>。</param>
/// <param name="Root">内容树根目录（本机路径）；没有时为 <c>-</c>。</param>
/// <param name="FirstSeen">最早出现时间。</param>
/// <param name="LastSeen">最晚出现时间。</param>
internal sealed record BuildInput(string Id, string Tier, string Kind, string Source, string File, string Root, string FirstSeen, string LastSeen)
{
    public bool HasFile => File is { Length: > 0 } and not "-";

    public bool HasRoot => Root is { Length: > 0 } and not "-";

    public static IReadOnlyList<BuildInput> ReadAll(string path)
    {
        var inputs = new List<BuildInput>();
        int number = 0;
        foreach (string raw in System.IO.File.ReadLines(path))
        {
            number++;
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] parts = line.Split('\t');
            if (parts.Length != 8)
            {
                throw new Infrastructure.UsageException($"{path}:{number}: expected 8 tab-separated columns (id tier kind source file root firstSeen lastSeen).");
            }

            inputs.Add(new BuildInput(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], parts[6], parts[7]));
        }

        if (inputs.Select(input => input.Id).Distinct(StringComparer.Ordinal).Count() != inputs.Count)
        {
            throw new Infrastructure.UsageException($"{path}: duplicate build ids.");
        }

        return inputs;
    }
}
