namespace Mugirl.Platform.Ui;

/// <summary>
/// 手写拖动控件的鼠标捕获（Unity 的 <c>GUIUtility.hotControl</c>）。Verse 没有这层封装。
/// </summary>
/// <remarks>
/// <para>
/// 1.7 中捕获仍有实际作用：没有捕获时，鼠标位于当前裁剪区外的 MouseDrag/MouseUp 会被 UnityStubs 改成 <c>Ignore</c>；
/// 宿主在左键 MouseUp 后把捕获清零。典型写法：MouseDown 时 <see cref="Capture"/> 并 <see cref="UiEvent.Use"/>，
/// MouseDrag 中 <see cref="IsCaptured"/> 时更新，<see cref="UiEvent.IsRawMouseUp"/> 时 <see cref="Release"/>。
/// 能用 <c>Widgets.HorizontalSlider</c>、<c>Widgets.DraggableBar</c>、<c>ReorderableWidget</c> 时优先用它们。
/// </para>
/// <para>
/// UnityStubs 的 <c>GetControlID</c> 忽略 hint 与矩形，只按调用顺序递增，宿主在每个 GUI 事件开始时重置计数；
/// 所以 id 只在每个事件中调用顺序相同的前提下跨事件一致：不要在条件分支里有时调用、有时不调用。
/// </para>
/// </remarks>
public static class UiHotControl
{
    // GUIUtility 整体禁用；控件 id 与捕获状态没有 Verse 封装，手写拖动控件必须直接读写它们。
#pragma warning disable RS0030

    /// <summary>为一个被动（不取键盘焦点）的控件分配 id（<c>GUIUtility.GetControlID(hint, FocusType.Passive, position)</c>）。</summary>
    public static int NewControlId(int hint, Rect position)
        => UnityEngine.GUIUtility.GetControlID(hint, UnityEngine.FocusType.Passive, position);

    /// <summary>当前有控件捕获鼠标（<c>GUIUtility.hotControl != 0</c>），例如别的滑块正在拖动。</summary>
    public static bool AnyCaptured => UnityEngine.GUIUtility.hotControl != 0;

    /// <summary><paramref name="controlId"/> 正在捕获鼠标。0 永远为 false。</summary>
    public static bool IsCaptured(int controlId) => controlId != 0 && UnityEngine.GUIUtility.hotControl == controlId;

    /// <summary>让 <paramref name="controlId"/> 捕获鼠标（<c>GUIUtility.hotControl = controlId</c>）。</summary>
    public static void Capture(int controlId) => UnityEngine.GUIUtility.hotControl = controlId;

    /// <summary>
    /// 若 <paramref name="controlId"/> 正在捕获鼠标则释放并返回 true；捕获已属于别的控件或已被宿主清零时不改动、返回 false。
    /// </summary>
    public static bool Release(int controlId)
    {
        if (controlId == 0 || UnityEngine.GUIUtility.hotControl != controlId)
        {
            return false;
        }

        UnityEngine.GUIUtility.hotControl = 0;
        return true;
    }

#pragma warning restore RS0030
}
