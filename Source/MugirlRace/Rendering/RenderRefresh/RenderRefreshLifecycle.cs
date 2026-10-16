namespace Mugirl.Rendering;

/// <summary>在既有瞬态重置点清空当前这局的延后刷新列表。</summary>
internal static class RenderRefreshLifecycle
{
    [GameLifecycleHook(GameLifecycleEvent.TransientStateReset, Order = 600)]
    private static void Reset()
    {
        if (GameUtility.TryGetGameComponent<GameComponent_PawnRenderRefresh>(out var component))
        {
            component.ClearPending();
        }
    }
}
