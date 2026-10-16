namespace Mugirl.Features.WeaponWheel;

/// <summary>轮盘的内部扩展边界；兼容层只在 Mod 构造阶段登记，领域代码不依赖具体外部模组。</summary>
internal static class WeaponWheelHooks
{
    // StaticCacheLifecycle: 进程级注册表；Mod 构造期填入，之后只读，不持有 Pawn 或存档对象。
    internal static WeaponWheelHookSet Current { get; } = new();
}
