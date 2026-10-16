namespace MugirlRace.Validation.Legacy;

/// <summary>语料文件的一行（见 <see cref="CorpusTable"/>）。</summary>
/// <param name="File">文件名，例如 <c>legacy-labels.tsv</c>。</param>
/// <param name="Facts">事实列。</param>
/// <param name="Tiers">来源层级（逗号分隔）。</param>
/// <param name="Eras">时代（逗号分隔）或 <c>-</c>。</param>
/// <param name="First">第一个含这条事实的构建。</param>
/// <param name="Last">最后一个含这条事实的构建。</param>
internal sealed record CorpusRow(string File, IReadOnlyList<string> Facts, string Tiers, string Eras, string First, string Last)
{
    /// <summary>published 或 observed 层级中出现过：必须有处置。</summary>
    public bool IsRequired => Tiers.Split(',').Any(tier => tier is "published" or "observed");

    /// <summary>只在 dev 层级中出现过：未处置只作警告。</summary>
    public bool IsDevOnly => !IsRequired && Tiers.Split(',').Any(tier => tier == "dev");

    /// <summary>只在 MooGirl 时代的构建中出现过。</summary>
    public bool IsMooGirlOnly => Eras == "MooGirl";

    public string this[int column] => Facts[column];

    /// <summary>覆盖报告与 ratchet 使用的稳定键：文件名与事实列以竖线连接。</summary>
    public string Key => File + "|" + string.Join("|", Facts);
}
