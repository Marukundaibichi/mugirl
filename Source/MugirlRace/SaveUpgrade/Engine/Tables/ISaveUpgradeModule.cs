namespace Mugirl.SaveUpgrade;

/// <summary>
/// 一个功能模块对升级表格的贡献。模块类放在 <c>SaveUpgrade/Modules/&lt;模块&gt;/</c>，标注
/// <see cref="SaveUpgradeModuleAttribute"/>，由框架按显式顺序发现；新增模块不需要修改任何中央文件。
/// </summary>
/// <remarks>
/// <see cref="Register"/> 只登记数据与步骤对象，不得读取 Verse 状态（DefDatabase、Find、Current）：
/// 升级清单在离线验证中构建，读档时表格在工作线程上构建。
/// </remarks>
internal interface ISaveUpgradeModule
{
    void Register(UpgradeModuleBuilder module);
}
