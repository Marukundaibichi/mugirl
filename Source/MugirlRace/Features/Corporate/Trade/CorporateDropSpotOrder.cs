namespace Mugirl.Features.Corporate;

/// <summary>
/// 白银落点的候选顺序：先居住区外（档位 0）后居住区内，再按到锚点的距离平方，最后按原版 Map.AllCells 的枚举序号。
/// 合成的 long 键与 1.6 的 <c>OrderBy(档位).ThenBy(距离)</c> 稳定排序给出相同的全序，可放进优先队列逐个取出。
/// </summary>
internal static class CorporateDropSpotOrder
{
    /// <param name="inHomeArea">格子在居住区内。</param>
    /// <param name="distanceSquared">到锚点的水平距离平方，非负。</param>
    /// <param name="enumerationIndex">格子在 Map.AllCells 中的序号（z 外层、x 内层），非负。</param>
    internal static long Key(bool inHomeArea, int distanceSquared, int enumerationIndex)
    {
        return ((inHomeArea ? 1L : 0L) << 62) | ((long)distanceSquared << 31) | (uint)enumerationIndex;
    }
}
