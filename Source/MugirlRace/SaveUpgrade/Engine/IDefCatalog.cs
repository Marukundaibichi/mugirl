namespace Mugirl.SaveUpgrade;

/// <summary>
/// 升级流水线需要的当前 Def 信息。游戏内由 DefDatabase 提供；离线验证（存档语料运行）由内容模型提供。
/// </summary>
/// <remarks>根类型用 DefDatabase 根类型短名（例如 <c>ThingDef</c>）；类型名用完整类型名或存档写法，比较时按存档写法规范化。</remarks>
public interface IDefCatalog
{
    /// <summary>当前构建中 <paramref name="root"/> 数据库是否有名为 <paramref name="defName"/> 的 Def。</summary>
    bool Contains(string root, string defName);

    /// <summary>
    /// 当前构建中该 Def（<c>ThingDef</c> 或 <c>HediffDef</c>）的组件类名（<c>compClass</c>）；Def 不存在时返回 null。
    /// </summary>
    IReadOnlyCollection<string>? CompClasses(string root, string defName);

    /// <summary>当前 Def 的整数配置字段（含继承默认值）；不存在、不是整数或无法证明时返回 null。</summary>
    int? IntegerField(string root, string defName, string field) => null;
}
