using System.Reflection;
using HarmonyLib;

namespace Mugirl.GameValidation.Core;

/// <summary>-Headless 下让同步长事件（读档等）能够执行。</summary>
/// <remarks>
/// 同步长事件要等 LongEventHandler 的 OnGUI 显示过它的窗口才运行；-Headless 宿主从不运行 OnGUI（E48），
/// 这类事件会一直等待并挡住之后排队的所有事件。这里把当前事件标为已显示，这也是窗口绘制对它做的全部事情
/// （hk SmokeTestMod 的 LongEventDisplay）。只访问测试需要的内部字段；-NoWindow 运行不调用。
/// </remarks>
internal static class HeadlessLongEvents
{
    // StaticCacheLifecycle: 进程级反射句柄，类型结构在进程内不变。
    private static readonly FieldInfo? CurrentEvent = AccessTools.Field(typeof(LongEventHandler), "currentEvent");

    // StaticCacheLifecycle: 进程级反射句柄，首次遇到长事件时解析。
    private static FieldInfo? alreadyDisplayed;

    /// <summary>反射目标是否存在；不存在时 -Headless 下的读档会卡住，由步骤超时报告。</summary>
    public static bool Available => CurrentEvent is not null;

    public static void Emulate()
    {
        object? current = CurrentEvent?.GetValue(null);
        if (current is null)
        {
            return;
        }

        alreadyDisplayed ??= AccessTools.Field(current.GetType(), "alreadyDisplayed");
        if (alreadyDisplayed?.GetValue(current) is false)
        {
            alreadyDisplayed.SetValue(current, true);
        }
    }
}
