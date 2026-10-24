using Mugirl.Features.Milk;
using Mugirl.Features.Mounting;
using Mugirl.Features.Roping;
using Mugirl.Rendering;

namespace Mugirl.Features;

/// <summary>
/// 每局游戏的生命周期中心：在固定的原版回调点，按固定顺序清空各功能的进程级每局状态。
/// 放在 Features 根命名空间，Core 不依赖具体功能。
/// </summary>
/// <remarks>
/// <see cref="BeforeLoad"/> 由存档升级的 <c>Game.LoadGame</c> 前缀调用，<see cref="BeforeNewGame"/> 由 <c>Game.InitNewGame</c> 前缀调用，
/// <see cref="ResetTransientState"/> 在剧情组件的 <c>FinalizeInit</c> 中调用。读档在长事件工作线程上进行，这里的清理不得调用 Unity 或打开界面。
/// 单项清理抛出只记录错误，不影响其余各项，也不向原版流程抛出。
/// </remarks>
internal static class GameLifecycle
{
    /// <summary>读档开始、任何存档数据被读取之前。上一局的状态不得泄漏进新读档的 ExposeData。</summary>
    internal static void BeforeLoad() => Run(PendingHitchRegistry.Reset);

    /// <summary>新游戏开始生成之前。</summary>
    internal static void BeforeNewGame() => Run(PendingHitchRegistry.Reset);

    /// <summary>新建与读档的 FinalizeInit 中清空每局瞬态缓存，每局一次。</summary>
    internal static void ResetTransientState()
    {
        Run(MugirlLog.ResetOnceWarnings);
        Run(MilkingAnimation.ResetTransientState);
        Run(MountedCombatController.ResetTransientState);
        Run(ClearPendingRenderRefresh);
    }

    private static void ClearPendingRenderRefresh()
    {
        if (GameUtility.TryGetGameComponent<GameComponent_PawnRenderRefresh>(out var component))
        {
            component.ClearPending();
        }
    }

    private static void Run(Action reset)
    {
        try
        {
            reset();
        }
        catch (Exception exception)
        {
            MugirlLog.Exception("Lifecycle reset " + reset.Method.DeclaringType?.FullName + "." + reset.Method.Name + " failed.", exception);
        }
    }
}
