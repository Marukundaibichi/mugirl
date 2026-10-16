namespace MugirlRace.Validation.Content;

/// <summary>LoadFolders.xml 某个版本列表中的一个 <c>li</c>（<c>Verse.LoadFolder</c> 的复刻）。</summary>
/// <param name="FolderName">相对 Mod 根目录的目录；<c>/</c> 为空串。</param>
/// <param name="RequiredAnyOf"><c>IfModActive</c>：至少一个包 id 必须启用。</param>
/// <param name="RequiredAllOf"><c>IfModActiveAll</c>：这些包 id 必须全部启用。</param>
/// <param name="DisallowedAnyOf"><c>IfModNotActive</c>：这些包 id 都不能启用。</param>
internal sealed record LoadFolder(
    string FolderName,
    IReadOnlyList<string>? RequiredAnyOf,
    IReadOnlyList<string>? RequiredAllOf,
    IReadOnlyList<string>? DisallowedAnyOf)
{
    /// <summary>与 RimWorld 1.7 的 <c>LoadFolder.ShouldLoad</c> 相同的判断。</summary>
    public bool ShouldLoad(ActiveMods active)
    {
        if (RequiredAnyOf is { Count: > 0 } && !RequiredAnyOf.Any(active.IsActive))
        {
            return false;
        }

        if (RequiredAllOf is { Count: > 0 } && !RequiredAllOf.All(active.IsActive))
        {
            return false;
        }

        return DisallowedAnyOf is not { Count: > 0 } || !DisallowedAnyOf.Any(active.IsActive);
    }
}
