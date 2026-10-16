using System.Reflection;
using HarmonyLib;

namespace Mugirl.Features.Roping;

// 原版 Pawn_RopeTracker.BreakRopeWithRoper 是私有方法（1.7 方法体：从牵引者处放下绳子并清空 ropedTo），
// 首次使用时解析为强类型委托，之后不再走 MethodInfo.Invoke。
internal static class RopeTrackerAccess
{
    // StaticCacheLifecycle: 进程级委托，只指向原版方法，不持有游戏对象，无需按局清理。
    private static readonly Action<Pawn_RopeTracker>? BreakRopeWithRoperAction = Bind();

    // 解析失败时退回 BreakAllRopes（1.6 的兜底行为），并警告一次。
    internal static void BreakRopeWithRoper(Pawn_RopeTracker tracker)
    {
        if (BreakRopeWithRoperAction is null)
        {
            MugirlLog.WarningOnce(
                "RopingTick.BreakRopeWithRoperMissing",
                () => "Mugirl.RopingTick.BreakRopeWithRoperMissing".Translate().ToString());
            tracker.BreakAllRopes();
            return;
        }

        BreakRopeWithRoperAction(tracker);
    }

    private static Action<Pawn_RopeTracker>? Bind()
    {
        MethodInfo? method = AccessTools.DeclaredMethod(typeof(Pawn_RopeTracker), "BreakRopeWithRoper", Type.EmptyTypes);
        if (method is null || method.ReturnType != typeof(void))
        {
            return null;
        }

        try
        {
            return AccessTools.MethodDelegate<Action<Pawn_RopeTracker>>(method);
        }
        catch (Exception ex) when (ex is ArgumentException or MemberAccessException)
        {
            MugirlLog.ExceptionOnce("RopingTick.BreakRopeWithRoperBind", "Could not bind Pawn_RopeTracker.BreakRopeWithRoper; using BreakAllRopes.", ex);
            return null;
        }
    }
}
