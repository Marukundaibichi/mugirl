namespace Mugirl.GameValidation.Core;

/// <summary><see cref="FixtureKind"/> 与清单文本（P、E、R、C、X-alias、X-settings）之间的转换。</summary>
internal static class FixtureKinds
{
    private static readonly (FixtureKind Kind, string Text)[] Names =
    [
        (FixtureKind.Published, "P"),
        (FixtureKind.Evidence, "E"),
        (FixtureKind.Real, "R"),
        (FixtureKind.Control, "C"),
        (FixtureKind.Alias, "X-alias"),
        (FixtureKind.Settings, "X-settings"),
    ];

    public static string Text(FixtureKind kind) => Names.First(n => n.Kind == kind).Text;

    /// <summary>解析清单文本；未知文本返回 false。</summary>
    public static bool TryParse(string? text, out FixtureKind kind)
    {
        foreach ((FixtureKind candidate, string name) in Names)
        {
            if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }

        kind = FixtureKind.Published;
        return false;
    }

    /// <summary>该种类的存档是旧档（版本 0，升级器必须运行）；对照组是被测构建自己写的。</summary>
    public static bool IsLegacy(FixtureKind kind) => kind != FixtureKind.Control;
}
