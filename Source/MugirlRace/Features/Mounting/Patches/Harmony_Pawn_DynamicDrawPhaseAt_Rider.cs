using HarmonyLib;

namespace Mugirl.Features.Mounting;

// 在载具绘制前后绘制骑手：朝南时骑手在载具身后（Prefix 先画），其他朝向在载具之上（Postfix 后画）。
// 与原版 Building_HoldingPlatform 绘制被收容者一样把三个 DrawPhase 都转给骑手的渲染器，
// 骑手的预渲染因此在 ParallelPreDraw 的 worker 线程完成，Draw 阶段不再同步补做。
// ParallelPreDraw 在 worker 线程执行：这里只读 Pawn、Comp 与 Def 数据，不写任何共享状态。
// 骑手的 Pawn.Drawer 是惰性创建的，同一帧先执行的 EnsureInitialized 阶段（主线程）会先访问它，worker 线程不会触发创建。
[HarmonyPatch(typeof(Pawn), nameof(Pawn.DynamicDrawPhaseAt))]
[MugirlPatch(PatchModule.Mounting, PatchRisk.High, "Mounted rider and weapon rendering is skipped.")]
public static class Harmony_Pawn_DynamicDrawPhaseAt_Rider
{
    public static void Prefix(Pawn __instance, DrawPhase phase, UVector3 drawLoc)
    {
        if (__instance.Rotation == Rot4.South)
        {
            DrawRider(__instance, phase, drawLoc);
        }
    }

    [HarmonyPriority(Priority.Normal - 1)]
    public static void Postfix(Pawn __instance, DrawPhase phase, UVector3 drawLoc)
    {
        if (__instance.Rotation != Rot4.South)
        {
            DrawRider(__instance, phase, drawLoc);
        }
    }

    private static void DrawRider(Pawn carrier, DrawPhase phase, UVector3 carrierDrawPos)
    {
        // 每个可见 Pawn 每帧调用三次；先用字段比较挡掉非雪牛娘，再查 Comp。
        if (!MugirlIdentity.IsMugirlPawn(carrier)
            || carrier.TryGetComp<CompMount>() is not { } comp
            || comp.MountedPawn is not { Destroyed: false } rider
            || rider.Drawer?.renderer is not { } renderer)
        {
            return;
        }

        renderer.DynamicDrawPhaseAt(phase, comp.RiderDrawPosAt(carrierDrawPos), carrier.Rotation, neverAimWeapon: true);
        if (phase == DrawPhase.Draw)
        {
            MountedCombatController.DrawWeapon(comp, carrierDrawPos);
        }
    }
}
