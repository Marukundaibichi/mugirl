namespace Mugirl.Platform.Ui;

/// <summary>
/// 当前 GUI 阶段（Layout、Repaint 或某个输入事件），基于 Verse 的 <see cref="OriginalEventUtility.EventType"/>。
/// 替代 <c>Event.current.type == EventType.Repaint/Layout</c> 这类“只在某阶段工作”的判断。
/// </summary>
/// <remarks>
/// <para>
/// 窗口内（<c>Window.InnerWindowOnGUI</c>）与 <c>UIRoot.UIRootOnGUI</c> 中，返回该次绘制开始时记录的事件类型，不会因控件
/// <c>Use()</c> 而变成 <c>Used</c>；其他位置返回 <c>Event.current.rawType</c>。对 Repaint 与 Layout 两者与 <c>Event.current.type</c> 相同。
/// </para>
/// <para>
/// 判断鼠标、键盘事件本身（是否已被别的控件消费、按的是哪个键）不要用本类，用 <see cref="UiEvent"/> 或 Verse 控件。
/// 1.7 宿主在每个输入事件前都派发一次 Layout，最后派发 Repaint；没有 MouseMove 事件。
/// </para>
/// </remarks>
public static class GuiPhase
{
    /// <summary>当前阶段；不在 GUI 回调中（没有 <c>Event.current</c>）时为 <c>EventType.Ignore</c>。</summary>
    public static UnityEngine.EventType Current
    {
        get
        {
            // OriginalEventUtility.EventType 在窗口外回退到 Event.current.rawType，没有 GUI 事件时会抛 NullReferenceException，
            // 所以先判空；Verse 没有“是否处于 GUI 回调”的封装。
#pragma warning disable RS0030 // 只读判空，不使用事件内容。
            if (UnityEngine.Event.current is null)
            {
                return UnityEngine.EventType.Ignore;
            }
#pragma warning restore RS0030

            return OriginalEventUtility.EventType;
        }
    }

    /// <summary>处于 Repaint 阶段：只在这里绘制、推进动画或记录绘制结果。</summary>
    public static bool IsRepaint => Current == UnityEngine.EventType.Repaint;

    /// <summary>处于 Layout 阶段：可跳过只为绘制准备的昂贵计算（Verse 控件在 Layout 中不绘制）。</summary>
    public static bool IsLayout => Current == UnityEngine.EventType.Layout;
}
