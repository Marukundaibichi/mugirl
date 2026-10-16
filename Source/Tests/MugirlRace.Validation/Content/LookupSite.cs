using MugirlRace.Validation.Metadata;

namespace MugirlRace.Validation.Content;

/// <summary>Mod 中按字符串查找某物的一处调用（或字段写入）。</summary>
/// <param name="Kind">字符串命名的对象种类。</param>
/// <param name="Value">字符串可能的字面量，或未知。</param>
/// <param name="Location"><c>Type::Method+IL_xxxx</c>。</param>
/// <param name="Method"><c>Type::Method</c>（动态值在例外清单中的键）。</param>
/// <param name="DefType">Def 查找：被查的 Def 类型（静态未知时为 <see langword="null"/>）。</param>
/// <param name="GraphicClass">贴图路径：已知时为加载它的 Graphic 类。</param>
/// <param name="ReportsFailure">调用传入 <c>reportFailure: false</c>（探测）时为 false。</param>
/// <param name="Source">调用所在源码文件相对 Source/MugirlRace 的路径（来自 PDB；未知时为 <see langword="null"/>）。</param>
internal sealed record LookupSite(LookupKind Kind, StringSet Value, string Location, string Method, Type? DefType, Type? GraphicClass, bool ReportsFailure, string? Source = null)
{
    /// <summary>动态值的例外清单类别：def、translation、texture 或 shader。</summary>
    public string Category => Kind switch
    {
        LookupKind.DefName or LookupKind.DefNameSilent => "def",
        LookupKind.TranslationKey or LookupKind.TranslationProbe => "translation",
        LookupKind.TexturePath or LookupKind.TextureFolder => "texture",
        _ => "shader",
    };

    /// <summary>动态站点在 <c>Baselines/dynamic-lookups.allow.txt</c> 中的键。</summary>
    public string AllowKey => Category + "\t" + Method;

    /// <summary>位于第三方兼容层（Source/MugirlRace/Compatibility/，ADR-003）。</summary>
    public bool InCompatibilityLayer => Source?.StartsWith("Compatibility/", StringComparison.Ordinal) == true;
}
