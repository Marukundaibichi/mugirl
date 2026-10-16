using System.Diagnostics;
using System.Linq;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// DOM 流水线：一次遍历建立索引，然后依次计划并应用框架表格步骤与模块步骤。第一个失败的步骤中止流水线；
/// 已应用的步骤保留（每个步骤幂等并按形状判断，下次读同一文件时重新运行是安全的）。
/// </summary>
/// <remarks>
/// 固定顺序：MooGirl 时代剥离 → 类型 → Def 删除 → Lord 状态图 → 标签、枚举、值与旧默认值 → 模块步骤
/// （按模块顺序与登记顺序）→ 加载 ID（最后一次遍历，处理所有步骤删除的对象与改名的物品）。
/// 表格是按存档来源版本过滤后的视图（<see cref="UpgradeTables.ForSource"/>）。整个流水线是纯函数：
/// 只读写给定的 XmlDocument，不读取 Verse 状态，可以离线运行。
/// </remarks>
internal static class UpgradePipeline
{
    /// <summary>报告中建立存档索引的条目 id。</summary>
    internal const string IndexStepId = "Framework.Index";

    /// <summary>在模块步骤之前运行的框架步骤。</summary>
    internal static IReadOnlyList<DomUpgradeStep> FrameworkSteps { get; } = CreateFrameworkSteps();

    /// <summary>在模块步骤之后运行的框架步骤（加载 ID）。</summary>
    internal static DomUpgradeStep FinalStep { get; } = CreateFinalStep();

    /// <summary>全部框架步骤（金样与清单用）。</summary>
    internal static IEnumerable<DomUpgradeStep> AllFrameworkSteps => FrameworkSteps.Append(FinalStep);

    /// <summary>对旧存档运行 DOM 流水线，结果记入 <paramref name="report"/>。</summary>
    internal static void RunDom(UpgradeContext context, UpgradeReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            context.BuildIndex(AllFrameworkSteps.SelectMany(step => step.IndexNames));
        }
        catch (Exception exception)
        {
            report.Fail(IndexStepId, exception.ToString());
            return;
        }

        report.Add(new StepReport(IndexStepId, UpgradePhase.Dom, 0, watch.ElapsedMilliseconds));
        foreach (DomUpgradeStep step in FrameworkSteps.Concat(context.Tables.DomSteps).Append(FinalStep))
        {
            watch.Restart();
            var plan = new EditPlan(context);
            try
            {
                step.Plan(context, plan);
            }
            catch (Exception exception)
            {
                report.Fail(step.Id, exception.ToString());
                return;
            }

            int actions;
            try
            {
                actions = plan.Apply(context.Index);
            }
            catch (Exception exception)
            {
                // 原语对有效计划不会抛出；到这里说明是框架缺陷，本步骤可能只应用了一部分。
                report.Fail(step.Id, "Applying the plan failed: " + exception);
                return;
            }

            context.Commit(plan);
            report.Add(new StepReport(step.Id, UpgradePhase.Dom, actions, watch.ElapsedMilliseconds));
        }
    }

    private static DomUpgradeStep[] CreateFrameworkSteps()
    {
        DomUpgradeStep[] steps = [new MooGirlEraStep(), new TypeTableStep(), new DefDropStep(), new LordPolicyStep(), new LabelTableStep()];
        foreach (DomUpgradeStep step in steps)
        {
            step.AssignModule(UpgradeTableBuilder.FrameworkModule);
        }

        return steps;
    }

    private static LoadIdStep CreateFinalStep()
    {
        var step = new LoadIdStep();
        step.AssignModule(UpgradeTableBuilder.FrameworkModule);
        return step;
    }
}
