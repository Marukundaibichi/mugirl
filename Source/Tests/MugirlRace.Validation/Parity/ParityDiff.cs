namespace MugirlRace.Validation.Parity;

/// <summary>一致性比较发现的一处差异。</summary>
/// <param name="Category">类别：type、field、method、body、call-target、string-literal、constant、order、harmony-order、content 等。</param>
/// <param name="Subject">差异所在（类型、成员、文件或 Def）。</param>
/// <param name="Detail">两侧的内容。</param>
internal sealed record ParityDiff(string Category, string Subject, string Detail)
{
    public override string ToString() => $"[{Category}] {Subject}: {Detail}";
}
