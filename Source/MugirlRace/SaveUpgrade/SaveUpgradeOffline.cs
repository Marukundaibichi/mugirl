using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 离线运行 DOM 流水线（验证器的存档语料模式）：与游戏内读档前缀执行同一套识别与步骤，不运行读档后阶段，不做备份。
/// </summary>
public static class SaveUpgradeOffline
{
    /// <summary>
    /// 就地升级 <paramref name="document"/>，返回与 <see cref="SaveUpgradeDiagnostics.LastReportJson"/> 相同格式的报告
    /// （只含识别与 DOM 步骤）。表格无效时抛出 <see cref="UpgradeTableException"/>。
    /// </summary>
    /// <param name="catalog">当前构建的 Def 信息（验证器的内容模型）。</param>
    public static string Run(XmlDocument document, IDefCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(catalog);
        SaveUpgradeSession session = SaveUpgradeSession.Start(0, document, filePath: null);
        session.RunDom(UpgradeTables.Default, catalog);
        return session.Report.ToJson();
    }
}
