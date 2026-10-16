using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Mugirl.Tests.Support;

// 捕获原版实际发出的错误调用，避免依赖离线宿主未必启用的原生日志回调。
// 使用它的测试必须放进 DisableParallelization collection；作用域即使异常也只撤销自己的补丁。
internal sealed class ScribeErrorCapture : IDisposable
{
    [ThreadStatic]
    private static ScribeErrorCapture? current;
    private static readonly MethodInfo ErrorMethod = typeof(Log).GetMethod(nameof(Log.Error), [typeof(string)])!;
    private readonly Harmony harmony = new("Mugirl.Tests.ScribeErrors." + Guid.NewGuid().ToString("N"));
    private bool disposed;

    internal List<string> Messages { get; } = [];

    internal ScribeErrorCapture()
    {
        if (current is not null)
        {
            throw new InvalidOperationException("Scribe error capture scopes must not overlap on one thread.");
        }

        try
        {
            harmony.Patch(ErrorMethod, prefix: new HarmonyMethod(typeof(ScribeErrorCapture), nameof(Capture)));
            current = this;
        }
        catch
        {
            harmony.Unpatch(ErrorMethod, HarmonyPatchType.Prefix, harmony.Id);
            throw;
        }
    }

    private static bool Capture(string text)
    {
        if (current is null)
        {
            return true;
        }

        current.Messages.Add(text);
        // 错误内容已保留；离线测试不触发暂停、窗口或原生日志宿主。
        return false;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            harmony.Unpatch(ErrorMethod, HarmonyPatchType.Prefix, harmony.Id);
        }
        finally
        {
            current = null;
        }
    }
}
