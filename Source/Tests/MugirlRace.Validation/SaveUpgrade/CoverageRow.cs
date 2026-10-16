namespace MugirlRace.Validation.SaveUpgrade;

/// <summary>save-upgrade-coverage.tsv 的一行：一条语料行的处置。</summary>
/// <param name="Key">语料行键（文件与事实列）。</param>
/// <param name="Tiers">来源层级。</param>
/// <param name="Disposition">unchanged、renamed、consumed、dropped、undisposed 或 n/a。</param>
/// <param name="Target">新名或策略。</param>
/// <param name="Via">清单条目、步骤或规则。</param>
/// <param name="Module">负责的模块（ratchet 文件按它分开）。</param>
/// <param name="Detail">未处置或不一致的原因。</param>
internal sealed record CoverageRow(string Key, string Tiers, string Disposition, string Target, string Via, string Module, string Detail)
{
    public bool IsRequired => Tiers.Split(',').Any(t => t is "published" or "observed");

    public bool IsDevOnly => !IsRequired && Tiers.Split(',').Any(t => t == "dev");

    public bool IsUndisposed => Disposition == "undisposed";

    public string ToLine() => string.Join('\t', Key, Tiers, Disposition, Target, Via, Module, Detail.Replace('\t', ' '));
}
