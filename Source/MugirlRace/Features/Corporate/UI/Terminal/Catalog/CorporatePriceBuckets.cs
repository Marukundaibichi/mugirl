using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.Corporate;

/// <summary>
/// 可分时取出的按价格稳定排序：<see cref="Add"/> 按扫描顺序登记，同价的项保持登记顺序；
/// 之后每次 <see cref="TryTakeNext"/> 取出一项，升序或降序由构造参数决定（降序时同价仍按登记顺序）。
/// 结果与 <c>OrderBy(price)</c> / <c>OrderByDescending(price)</c> 的稳定排序相同。
/// </summary>
/// <remarks>订购目录已按名称排序，所以同价桶天然保持 1.6 <c>ThenBy(name)</c> 的结果。</remarks>
internal sealed class CorporatePriceBuckets<T>(bool descending)
{
    private readonly SortedDictionary<int, List<T>> buckets = new();
    private List<int>? keys;
    private int keyIndex;
    private int itemIndex;

    /// <summary>登记一项；开始取出后不能再登记。</summary>
    internal void Add(int price, T item)
    {
        if (keys is not null)
        {
            throw new InvalidOperationException("Cannot add after taking items.");
        }

        if (!buckets.TryGetValue(price, out List<T>? bucket))
        {
            bucket = [];
            buckets.Add(price, bucket);
        }

        bucket.Add(item);
    }

    /// <summary>按排序取出下一项；全部取完时返回 false。</summary>
    internal bool TryTakeNext([MaybeNullWhen(false)] out T item)
    {
        if (keys is null)
        {
            keys = [.. buckets.Keys];
            if (descending)
            {
                keys.Reverse();
            }
        }

        while (keyIndex < keys.Count)
        {
            List<T> bucket = buckets[keys[keyIndex]];
            if (itemIndex < bucket.Count)
            {
                item = bucket[itemIndex++];
                return true;
            }

            keyIndex++;
            itemIndex = 0;
        }

        item = default;
        return false;
    }
}
