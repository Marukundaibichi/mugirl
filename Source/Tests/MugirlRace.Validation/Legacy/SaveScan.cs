namespace MugirlRace.Validation.Legacy;

/// <summary>一个存档的扫描结果（见 <see cref="SaveTokenScanner"/>）。</summary>
internal sealed class SaveScan
{
    private readonly HashSet<(string Kind, string Token, string Context)> tokens = [];

    /// <summary><c>meta/modIds</c> 含本 Mod。</summary>
    public bool HasMod { get; set; }

    /// <summary><c>meta/gameVersion</c>。</summary>
    public string GameVersion { get; set; } = "";

    public IReadOnlyCollection<(string Kind, string Token, string Context)> Tokens => tokens;

    /// <summary>按类型命名空间判断写这个存档的构建时代：<c>MooGirl</c>、<c>Mugirl</c> 或两者。</summary>
    public string Era
    {
        get
        {
            var eras = new SortedSet<string>(StringComparer.Ordinal);
            foreach ((string kind, string token, _) in tokens)
            {
                if (kind is "type" or "type?")
                {
                    eras.Add(token.StartsWith("MooGirl", StringComparison.Ordinal) || token.StartsWith("BondageToys.", StringComparison.Ordinal) ? "MooGirl" : "Mugirl");
                }
            }

            return eras.Count == 0 ? "-" : string.Join(",", eras);
        }
    }

    public void Add(string kind, string token, string context) => tokens.Add((kind, token, context));
}
