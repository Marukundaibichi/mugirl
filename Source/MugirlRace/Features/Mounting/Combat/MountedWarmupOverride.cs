namespace Mugirl.Features.Mounting;

// 骑乘施放期间把某个 Verb 的 WarmupTime 临时改为指定值，由 Harmony_Verb_WarmupTime_MountedCast 读取。
// 覆盖只存在于 TryStartCastOn 调用期间，所以用单个静态槽保存当前覆盖：该 Postfix 对全游戏每次
// WarmupTime 调用都会执行，空槽时只做一次引用比较。作用域按栈嵌套，Dispose 恢复进入前的槽。
internal readonly struct MountedWarmupOverride : IDisposable
{
    // StaticCacheLifecycle: 只在主线程 tick 的 using 作用域内非空；卸下装备时 Forget，
    // MountedCombatController.ResetTransientState（新开局、读档、初始化）时 Clear。
    private static Verb? activeVerb;
    private static float activeWarmupTime;

    private readonly Verb? previousVerb;
    private readonly float previousWarmupTime;
    private readonly bool active;

    internal MountedWarmupOverride(Verb? verb, float warmupTime)
    {
        previousVerb = activeVerb;
        previousWarmupTime = activeWarmupTime;
        active = verb is not null;
        if (active)
        {
            activeVerb = verb;
            activeWarmupTime = warmupTime;
        }
    }

    public void Dispose()
    {
        if (active)
        {
            activeVerb = previousVerb;
            activeWarmupTime = previousWarmupTime;
        }
    }

    internal static bool TryGet(Verb? verb, out float warmupTime)
    {
        if (verb is not null && ReferenceEquals(verb, activeVerb))
        {
            warmupTime = activeWarmupTime;
            return true;
        }

        warmupTime = 0f;
        return false;
    }

    internal static void Forget(Verb verb)
    {
        if (ReferenceEquals(verb, activeVerb))
        {
            activeVerb = null;
        }
    }

    internal static void Clear()
    {
        activeVerb = null;
        activeWarmupTime = 0f;
    }
}
