using HarmonyLib;

namespace Mugirl.Features.WeaponWheel;

// 剑舞冲刺越过目标时补发被 TryCastNextBurstShot 前缀推迟的近战结算。
// 该方法是 protected，经委托调用；委托指向方法入口，Harmony 补丁（含本模组的前缀/后缀）照常生效。
internal static class SwordDanceDelayedStrike
{
    // StaticCacheLifecycle: 进程级委托，首次剑舞命中时解析一次 Verb.TryCastNextBurstShot；不持有游戏对象。
    private static Action<Verb>? tryCastNextBurstShot;
    private static bool resolved;

    internal static bool TryInvoke(Verb verb)
    {
        Action<Verb>? invoke = Resolve();
        if (invoke == null)
        {
            return false;
        }

        try
        {
            invoke(verb);
            return true;
        }
        catch (Exception exception)
        {
            MugirlLog.WarningOnce(
                "WeaponWheel.DelayedSwordDanceBurstFailed",
                "Delayed sword-dance impact failed: " + exception.GetBaseException().Message);
            return false;
        }
    }

    // 只在主线程的 tick 中调用（冲刺推进），无需同步。
    private static Action<Verb>? Resolve()
    {
        if (resolved)
        {
            return tryCastNextBurstShot;
        }

        resolved = true;
        System.Reflection.MethodInfo? method = AccessTools.Method(typeof(Verb), "TryCastNextBurstShot");
        if (method == null)
        {
            MugirlLog.WarningOnce(
                "WeaponWheel.DelayedSwordDanceBurstMissing",
                "Unable to resolve Verb.TryCastNextBurstShot for delayed sword-dance impact.");
            return null;
        }

        try
        {
            tryCastNextBurstShot = AccessTools.MethodDelegate<Action<Verb>>(method);
        }
        catch (ArgumentException exception)
        {
            MugirlLog.WarningOnce(
                "WeaponWheel.DelayedSwordDanceBurstMissing",
                "Unable to bind Verb.TryCastNextBurstShot for delayed sword-dance impact: " + exception.Message);
        }
        return tryCastNextBurstShot;
    }
}
