using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Rendering;

/// <summary>
/// 雪牛娘图形的立即刷新加延后补刷。HAR 渲染树在食尸鬼化、怀孕、年龄阶段变化后可能滞后重建，
/// 所以除了立即脏化，还会在 30 tick 与 300 tick 时各补刷一次。待刷新列表属于当前这局游戏的
/// <see cref="GameComponent_PawnRenderRefresh"/>，返回主菜单后随 Game 一起释放。
/// </summary>
public static class PawnRenderRefresh
{
    public static void NotifyPawnChanged(Pawn? pawn)
    {
        if (!IsRefreshTarget(pawn))
        {
            return;
        }

        RefreshGraphics(pawn);
        if (GameUtility.IsPlaying()
            && TickUtility.TryGetCurrentGameTick(out int currentTick)
            && GameUtility.TryGetGameComponent<GameComponent_PawnRenderRefresh>(out var component))
        {
            component.Schedule(pawn, currentTick);
        }
    }

    internal static bool IsRefreshTarget([NotNullWhen(true)] Pawn? pawn)
    {
        return MugirlIdentity.IsMugirlPawn(pawn) && !pawn.Destroyed;
    }

    internal static void RefreshGraphics(Pawn pawn)
    {
        pawn.Drawer?.renderer?.SetAllGraphicsDirty();
    }
}
