namespace MugirlRace.Validation.SaveUpgrade;

/// <summary>清单中的一条映射（types、defs、labels、enums、values、defaults、jobPolicies、lordPolicies、settings.labels）。</summary>
/// <param name="Section">所在数组，例如 <c>types</c>。</param>
/// <param name="Index">数组中的位置（消息用）。</param>
/// <param name="Old">旧名（defaults 为旧默认值；policies 为类型）。</param>
/// <param name="New">新名；drop 时为空。</param>
/// <param name="Disposition">rename、drop 或 consumed（labels）。</param>
/// <param name="Policy">drop 的节点策略或 job/lord 策略。</param>
/// <param name="Module">负责的模块。</param>
/// <param name="Reason">理由。</param>
/// <param name="Root">defs 的数据库根短名。</param>
/// <param name="LegacyRoot">旧语料的数据库根；仅用于旧名匹配，不用于新数据库查询。</param>
/// <param name="Owner">labels/defaults 的 owner 选择器，enums 的枚举名，values 的上下文。</param>
/// <param name="Label">defaults 的标签。</param>
/// <param name="Step">labels 的 consumed 步骤 id。</param>
internal sealed record ManifestEntry(string Section, int Index, string Old, string? New, string Disposition, string? Policy, string Module, string Reason,
    string? Root, string? Owner, string? Label, string? Step, string? LegacyRoot = null)
{
    public override string ToString()
    {
        string scope = Owner ?? Root ?? "";
        return $"{Section}[{Index}] {scope}{(scope.Length == 0 ? "" : "/")}{Old} -> {New ?? "(drop)"}";
    }
}
