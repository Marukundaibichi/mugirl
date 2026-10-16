namespace Mugirl.Rendering;

/// <summary>
/// 每局游戏的延后图形刷新（按类名写入每个存档，类名与命名空间不可改）。
/// 待刷新列表只在内存中，不写入存档；新建、读档时随组件重建而清空。
/// </summary>
public class GameComponent_PawnRenderRefresh : GameComponent
{
    private readonly PendingRenderRefreshSchedule<Pawn> schedule = new();

    public GameComponent_PawnRenderRefresh()
    {
    }

    // 原版 Game.FillComponents 以 (Game) 参数构造组件。
    public GameComponent_PawnRenderRefresh(Game game)
    {
    }

    public override void GameComponentTick()
    {
        if (schedule.Count == 0 || !TickUtility.TryGetCurrentGameTick(out int currentTick))
        {
            return;
        }

        schedule.Tick(currentTick, PawnRenderRefresh.IsRefreshTarget, PawnRenderRefresh.RefreshGraphics);
    }

    internal void Schedule(Pawn pawn, int currentTick)
    {
        schedule.Schedule(pawn, currentTick);
    }

    internal void ClearPending()
    {
        schedule.Clear();
    }
}
