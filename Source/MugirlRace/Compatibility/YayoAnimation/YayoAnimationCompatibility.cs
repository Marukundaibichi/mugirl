using System.Reflection;
using HarmonyLib;

namespace Mugirl.Compatibility.YayoAnimation;

/// <summary>
/// Yayo Animation（com.yayo.yayoAni.continued）：轮盘过渡图形跟随 Yayo 调整后的身体位置。
/// </summary>
/// <remarks>
/// Yayo Animation 通过原版私有方法 <c>PawnRenderer.GetBodyPos(Vector3, PawnPosture, out bool)</c> 改变身体位置；
/// 该方法在 1.7 中签名不变，站立时原样返回传入坐标。
/// </remarks>
internal static class YayoAnimationCompatibility
{
    private delegate UVector3 GetBodyPositionDelegate(PawnRenderer renderer, UVector3 drawLoc, PawnPosture posture, out bool showBody);

    internal static bool Active => ModCompat.YayoAnimation;

    internal static void RegisterHooks()
    {
        if (Active)
        {
            Mugirl.Features.WeaponWheel.WeaponWheelHooks.Current.SetBodyPosition("YayoAnimation", WheelBodyPosition);
        }
    }

    private static Vector3 WheelBodyPosition(Thing thing) => DrawPosition(thing).ToNumerics();

    /// <summary>
    /// 轮盘身体坐标回调：Pawn 返回身体位置，其他 Thing 原样返回 DrawPos。
    /// </summary>
    internal static UVector3 DrawPosition(Thing thing)
    {
        if (thing is not Pawn pawn || pawn.Drawer?.renderer is not { } renderer
            || ReflectionCache.GetBodyPosition is not { } getBodyPosition || ReflectionCache.InvocationFailed)
        {
            return thing.DrawPos;
        }

        try
        {
            return getBodyPosition(renderer, pawn.DrawPos, pawn.GetPosture(), out _);
        }
        catch (Exception exception)
        {
            // 绘制每帧调用：失败一次后不再尝试，直接用原坐标。
            ReflectionCache.InvocationFailed = true;
            MugirlLog.WarningOnce(
                "Compatibility.YayoAnimation.GetBodyPos",
                () => "Yayo Animation compatibility could not read the pawn body position; weapon-wheel graphics use the plain draw position: "
                    + exception.GetType().Name + ": " + exception.Message);
            return pawn.DrawPos;
        }
    }

    private static class ReflectionCache
    {
        // StaticCacheLifecycle: 进程级；轮盘第一次自定义绘制时解析原版私有方法。InvocationFailed 一经置位，本进程内不再尝试，无需清理。
        internal static readonly GetBodyPositionDelegate? GetBodyPosition;
        internal static bool InvocationFailed;

        static ReflectionCache()
        {
            GetBodyPosition = CreateBodyPositionDelegate();
            if (GetBodyPosition is null)
            {
                CompatAccessors.ReportUnbound("Yayo Animation", "Verse.PawnRenderer.GetBodyPos(Vector3, PawnPosture, out bool)");
            }
        }

        private static GetBodyPositionDelegate? CreateBodyPositionDelegate()
        {
            try
            {
                MethodInfo? method = AccessTools.DeclaredMethod(
                    typeof(PawnRenderer),
                    "GetBodyPos",
                    [typeof(UVector3), typeof(PawnPosture), typeof(bool).MakeByRefType()]);
                return method is null || method.IsStatic
                    ? null
                    : method.CreateDelegate<GetBodyPositionDelegate>();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
