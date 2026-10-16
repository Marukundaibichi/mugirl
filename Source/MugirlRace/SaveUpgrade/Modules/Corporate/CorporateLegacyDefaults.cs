using Mugirl.Features.Corporate;

namespace Mugirl.SaveUpgrade;

internal static class CorporateLegacyDefaults
{
    internal static int DistributionTarget(int count) => Math.Max(1, CorporateMoney.CeilingShare(count, 0.8f));
}
