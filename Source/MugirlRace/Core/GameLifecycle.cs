using System.Threading;

namespace Mugirl.Core;

/// <summary>
/// 每局游戏的生命周期中心：功能在静态方法上标注 <see cref="GameLifecycleHookAttribute"/>，在这里按显式顺序被调用。
/// 功能之间不再互相调用重置方法。
/// </summary>
/// <remarks>
/// 事件来源：<see cref="GameLifecycleEvent.BeforeLoad"/> 与 <see cref="GameLifecycleEvent.LoadedGame"/>（及
/// <see cref="GameLifecycleEvent.AddedToExistingGame"/>）由存档升级的 <c>Game.LoadGame</c> 前缀与后缀发出，
/// <see cref="GameLifecycleEvent.BeforeNewGame"/> 由 <c>Game.InitNewGame</c> 前缀发出，其余由 <see cref="GameComponent_Mugirl"/> 发出。
/// 读档事件在长事件工作线程上发出，处理器不得调用 Unity 或打开界面。
/// </remarks>
internal static class GameLifecycle
{
    // StaticCacheLifecycle: 进程级不可变处理器表，首次发出事件时扫描本程序集建立。
    private static readonly Lazy<GameLifecycleRegistry> Registry = new(
        () => GameLifecycleRegistry.FromAssembly(typeof(GameLifecycle).Assembly),
        LazyThreadSafetyMode.ExecutionAndPublication);

    // StaticCacheLifecycle: 进程级标志，发现问题只报告一次。
    private static int problemsReported;

    /// <summary>依次调用 <paramref name="lifecycleEvent"/> 的处理器。从不抛出：调用方是读档与新游戏的原版流程。</summary>
    internal static void Raise(GameLifecycleEvent lifecycleEvent)
    {
        GameLifecycleRegistry registry;
        try
        {
            registry = Registry.Value;
        }
        catch (Exception exception)
        {
            MugirlLog.ExceptionOnce("GameLifecycle.Discovery", "Lifecycle hooks could not be discovered; per-game resets do not run.", exception);
            return;
        }

        if (registry.Problems.Count > 0 && Interlocked.Exchange(ref problemsReported, 1) == 0)
        {
            MugirlLog.Error("Invalid lifecycle hooks were skipped:\n- " + string.Join("\n- ", registry.Problems));
        }

        registry.Raise(lifecycleEvent, (hook, exception) =>
            MugirlLog.Exception("Lifecycle hook " + hook.Id + " failed during " + lifecycleEvent + ".", exception));
    }
}
