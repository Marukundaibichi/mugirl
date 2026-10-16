using System.Linq;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 阶段 B 仅结束显式登记为 <see cref="JobDriverPolicy.EndInFlight"/> 的工作。
/// 无版本戳本身不是状态不兼容的证据；已核对保持不变或由迁移步骤认领的工作继续原有状态。
/// </summary>
/// <param name="Drivers">另外要结束的旧 JobDriver 类型名。</param>
/// <param name="Claimed">由步骤处理、默认策略跳过的旧 JobDriver 类型名。</param>
internal sealed record InFlightJobSelection(IReadOnlyList<string> Drivers, IReadOnlyList<string> Claimed)
{
    /// <summary>没有任何工作需要结束。</summary>
    internal bool IsEmpty => Drivers.Count == 0;

    /// <param name="applicable">按存档来源版本过滤后的表格视图。</param>
    internal static InFlightJobSelection For(UpgradeTables applicable)
    {
        ArgumentNullException.ThrowIfNull(applicable);
        return new InFlightJobSelection(
            [.. applicable.JobPolicies.Where(row => row.Policy == JobDriverPolicy.EndInFlight).Select(row => row.Type)],
            [.. applicable.JobPolicies.Where(row => row.Policy == JobDriverPolicy.Claimed).Select(row => row.Type)]);
    }
}
