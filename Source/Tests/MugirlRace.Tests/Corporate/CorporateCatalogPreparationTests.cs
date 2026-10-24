using System.Reflection;
using Mugirl.Features.Corporate;

namespace Mugirl.Tests.Corporate;

[CollectionDefinition("Corporate catalog caches", DisableParallelization = true)]
public sealed class CorporateCatalogCacheCollection;

/// <summary>
/// 订购目录与供货分析是只依赖 Def 的进程级惰性缓存：未开户的读档和新开局不预建，留到首次使用时构建。
/// 离线没有当前游戏，巨企视为未开户；无条件预建会在这里触发构建（离线因 DefOf 未绑定而抛出）。
/// </summary>
[Collection("Corporate catalog caches")]
public sealed class CorporateCatalogPreparationTests
{
    [Fact]
    public void LockedLoadAndNewGameLeaveCatalogAndSupplyAnalysisUnbuilt()
    {
        using var caches = new UnbuiltCaches();
        var network = new CorporateNetwork(null!);
        Assert.False(network.Unlocked);

        network.StartedNewGame();
        network.LoadedGame();

        Assert.False(caches.CatalogBuilt, "the order catalog must not be prepared before the corporation is unlocked");
        Assert.False(caches.SupplyAnalysisBuilt, "the supply analysis must not be prepared before the corporation is unlocked");
    }

    // 暂时清空两个缓存的快照（CorporateDefCache.snapshot），结束时原样放回，不影响同一进程中的其他测试。
    private sealed class UnbuiltCaches : IDisposable
    {
        private readonly CacheSlot catalog = CacheSlot.Clear(typeof(CorporateNetwork), "OrderCatalogCache");
        private readonly CacheSlot supply = CacheSlot.Clear(typeof(CorporateSupplyAnalysis), "SharedCache");

        internal bool CatalogBuilt => catalog.Built;

        internal bool SupplyAnalysisBuilt => supply.Built;

        public void Dispose()
        {
            catalog.Restore();
            supply.Restore();
        }
    }

    private sealed class CacheSlot(object cache, FieldInfo snapshot, object? original)
    {
        internal bool Built => snapshot.GetValue(cache) is not null;

        internal static CacheSlot Clear(Type owner, string cacheField)
        {
            object cache = owner.GetField(cacheField, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            FieldInfo snapshot = cache.GetType().GetField("snapshot", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var slot = new CacheSlot(cache, snapshot, snapshot.GetValue(cache));
            snapshot.SetValue(cache, null);
            return slot;
        }

        internal void Restore() => snapshot.SetValue(cache, original);
    }
}
