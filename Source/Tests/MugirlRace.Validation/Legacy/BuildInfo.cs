namespace MugirlRace.Validation.Legacy;

/// <summary>builds.tsv 的一行：一个语料输入（构建、存档或参考程序集）的身份与来源。</summary>
/// <param name="Id">稳定 id：<c>p.zip.0927</c>、<c>p.git.324501c</c>、<c>d.git.1edc8178</c>、<c>d.bak.…</c>、<c>o.…</c>（存档）、<c>r.…</c>（参考）。</param>
/// <param name="Tier">published、observed、dev、info（重构前快照）或 ref（参考程序集）。</param>
/// <param name="Kind">dll+content、dll、content、save 或 ref。</param>
/// <param name="Source">可移植的来源描述（仓库相对路径、<c>git:</c>、<c>zip:</c>、<c>workshop:</c>、<c>saves:</c> 等）。</param>
/// <param name="Sha256">DLL、存档或参考程序集文件的 sha256（大写）；纯内容为 <c>-</c>。</param>
/// <param name="GitBlob">DLL 的 Git blob id；其他为 <c>-</c>。</param>
/// <param name="AssemblyName">程序集名；其他为 <c>-</c>。</param>
/// <param name="Era">Mugirl、MooGirl（两者以逗号连接）或 <c>-</c>。</param>
/// <param name="PackageId">内容树 About.xml 的 packageId；存档为本 Mod 的包 id；其他为 <c>-</c>。</param>
/// <param name="FirstSeen">最早出现时间（UTC，ISO 8601）。</param>
/// <param name="LastSeen">最晚出现时间。</param>
/// <param name="ContentRef">内容指纹或存档游戏版本；没有时为 <c>-</c>。</param>
internal sealed record BuildInfo(string Id, string Tier, string Kind, string Source, string Sha256, string GitBlob, string AssemblyName, string Era,
    string PackageId, string FirstSeen, string LastSeen, string ContentRef)
{
    public static readonly string[] Columns = ["id", "tier", "kind", "source", "sha256", "gitBlob", "assemblyName", "era", "packageId", "firstSeen", "lastSeen", "contentRef"];

    /// <summary>published 与 observed 的名字必须有处置；dev 只作提示；info 与 ref 不参与。</summary>
    public bool IsRequired => Tier is "published" or "observed";

    public string ToLine() => string.Join('\t', new[] { Id, Tier, Kind, Source, Sha256, GitBlob, AssemblyName, Era, PackageId, FirstSeen, LastSeen, ContentRef }
        .Select(value => value.Length == 0 ? "-" : value.Replace('\t', ' ')));

    public static BuildInfo? Parse(string line)
    {
        string[] parts = line.Split('\t');
        return parts.Length == Columns.Length
            ? new BuildInfo(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], parts[6], parts[7], parts[8], parts[9], parts[10], parts[11])
            : null;
    }
}
