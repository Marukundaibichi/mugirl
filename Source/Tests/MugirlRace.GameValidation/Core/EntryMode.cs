namespace Mugirl.GameValidation.Core;

/// <summary>会话如何进入游戏。</summary>
internal enum EntryMode
{
    /// <summary>命令行 -quicktest 生成的快速测试地图。</summary>
    QuickTest,

    /// <summary>从主菜单读取临时存档目录 Saves/ 中的存档（<see cref="ValidationOptions.EntrySave"/>）。</summary>
    LoadSave,
}
