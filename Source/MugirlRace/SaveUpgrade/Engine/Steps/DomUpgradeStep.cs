namespace Mugirl.SaveUpgrade;

/// <summary>
/// 读档前改写 XML 的步骤，分两段：<see cref="Plan"/> 只读取存档并把编辑记入 <see cref="EditPlan"/>（可以抛异常，抛出即中止升级），
/// 随后由流水线用安全的 DOM 原语应用这些编辑。
/// </summary>
internal abstract class DomUpgradeStep : UpgradeStep
{
    protected DomUpgradeStep(string id, string reason, params string[] consumes)
        : base(id, UpgradePhase.Dom, reason, consumes)
    {
    }

    /// <summary>
    /// 除 <see cref="UpgradeStep.Consumes"/> 中的标签外，本步骤要通过 <see cref="SaveIndex.ElementsNamed"/> 读取的元素名；
    /// 存档索引只在唯一一次遍历中收集登记过的名称。
    /// </summary>
    internal virtual IEnumerable<string> IndexNames => [];

    /// <summary>计划本步骤的编辑。不得直接修改 DOM。</summary>
    internal abstract void Plan(UpgradeContext context, EditPlan plan);
}
