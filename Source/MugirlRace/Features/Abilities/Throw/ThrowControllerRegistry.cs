namespace Mugirl.Features.Abilities;

/// <summary>
/// 已生成的投掷控制器计数，供每次 Verb 施放都会经过的开火锁快速判断“当前是否可能有投掷进行中”。
/// 计数为 0 时直接放行，不再查询地图的实体列表。
/// </summary>
/// <remarks>
/// StaticCacheLifecycle: 进程级计数，只在主线程的 SpawnSetup/DeSpawn 中增减，不持有任何游戏对象。
/// 退出到主菜单或移除地图时控制器不经过 DeSpawn，计数会偏大但永不偏小；偏大时调用方退回逐图查找，结果仍然正确，
/// 因此不需要在换局时清零。
/// </remarks>
internal static class ThrowControllerRegistry
{
    private static int spawnedControllers;

    internal static bool AnySpawned => spawnedControllers > 0;

    internal static void NotifySpawned() => spawnedControllers++;

    internal static void NotifyDespawned()
    {
        if (spawnedControllers > 0)
        {
            spawnedControllers--;
        }
    }
}
