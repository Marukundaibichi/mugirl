namespace Mugirl.SaveUpgrade;

/// <summary>设置文件升级行的种类。</summary>
internal enum SettingsRowKind
{
    /// <summary><c>ModSettings</c> 的 <c>Class</c> 属性改名。</summary>
    ClassRename,

    /// <summary>设置标签改名。</summary>
    LabelRename,

    /// <summary>删除设置标签（回到默认值）。</summary>
    LabelDrop,
}
