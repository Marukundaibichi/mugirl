using System.Collections.Generic;
using System.Linq;
using Mugirl.Features.Corporate;

namespace Mugirl.Tests.Corporate;

/// <summary>白银落点优先队列的键与 1.6 的整图稳定排序（区外优先、距离升序、同距保持 Map.AllCells 顺序）给出相同的全序。</summary>
public sealed class CorporateDropSpotOrderTests
{
    [Fact]
    public void Key_OrdersLikeTheStableLinqSort()
    {
        var random = new Random(4242);
        for (int trial = 0; trial < 50; trial++)
        {
            int count = random.Next(1, 3000);
            var cells = Enumerable.Range(0, count)
                .Select(index => (Index: index, Home: random.Next(4) == 0, Distance: random.Next(0, random.Next(1, 200000))))
                .ToList();

            int[] linq = cells.OrderBy(c => c.Home ? 1 : 0).ThenBy(c => c.Distance).Select(c => c.Index).ToArray();
            var queue = new PriorityQueue<int, long>(cells.Select(c => (c.Index, CorporateDropSpotOrder.Key(c.Home, c.Distance, c.Index))));
            var dequeued = new List<int>(count);
            while (queue.TryDequeue(out int index, out _))
            {
                dequeued.Add(index);
            }

            Assert.Equal(linq, dequeued);
        }
    }

    [Fact]
    public void Key_KeepsFieldsApartAtTheirLimits()
    {
        long farOutside = CorporateDropSpotOrder.Key(false, int.MaxValue, int.MaxValue);
        long nearInside = CorporateDropSpotOrder.Key(true, 0, 0);
        Assert.True(farOutside < nearInside, "every cell outside the home area sorts before any cell inside it");
        Assert.True(CorporateDropSpotOrder.Key(false, 1, 0) > CorporateDropSpotOrder.Key(false, 0, int.MaxValue),
            "distance dominates the enumeration index");
        Assert.True(farOutside > 0);
    }
}
