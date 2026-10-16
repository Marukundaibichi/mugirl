using Mugirl.Compatibility.DualWield;
using Mugirl.Compatibility.MeleeAnimation;
using Mugirl.Compatibility.RunAndGun;
using Mugirl.Compatibility.TrainingFacility;
using Mugirl.Compatibility.YayoAnimation;
using Mugirl.Compatibility.YayoCombat;

namespace Mugirl.Compatibility;

/// <summary>在原 Mod 构造阶段接线；回调顺序对应原 Harmony 的显式优先级，外部反射访问仍延迟到首次使用。</summary>
internal static class CombatCompatibilityBootstrap
{
    // StaticCacheLifecycle: 进程级幂等启动标记；不随读档重置。
    private static bool registered;

    internal static void Register()
    {
        if (registered)
        {
            return;
        }
        DualWieldCompatibility.RegisterHooks();
        RunAndGunCompatibility.RegisterHooks();
        MeleeAnimationCompatibility.RegisterHooks();
        YayoCombatCompatibility.RegisterHooks();
        YayoAnimationCompatibility.RegisterHooks();
        TrainingFacilityCompatibility.RegisterHooks();
        registered = true;
    }
}
