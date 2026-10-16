namespace Mugirl.Platform.Ui;

/// <summary>
/// 自定义控件需要的逐事件数据（事件类型、鼠标键、局部鼠标位置、增量、按键），以及消费事件。
/// 只有这里读取 <c>Event.current</c>；业务代码不要直接访问它。
/// </summary>
/// <remarks>
/// <para>
/// 先找 Verse 的替代：点击用 <c>Widgets.ButtonInvisible</c>/<c>ButtonText</c>/<c>ButtonImage</c>（再用 <see cref="EngineInput"/> 区分左右键），
/// 悬停用 <c>Mouse.IsOver</c>，拖动排序用 <c>ReorderableWidget</c>/<c>DragAndDropWidget</c>，滚动用 <c>Widgets.BeginScrollView</c>，
/// 快捷键用 <c>KeyBindingDef.KeyDownEvent</c>，GUI 阶段用 <see cref="GuiPhase"/>。只有手写控件（拖动阈值线、自绘滑块、拖影跟随鼠标）才用本类。
/// </para>
/// <para>
/// 语义与 UnityStubs 的 <c>Event</c> 相同：<see cref="Type"/> 在没有拖动捕获（<see cref="UiHotControl"/>）且鼠标位于当前可点击裁剪区外时，
/// 鼠标事件返回 <c>Ignore</c>；被 <see cref="Use"/> 后为 <c>Used</c>。<see cref="RawType"/> 不受裁剪影响，但同样会变成 <c>Used</c>。
/// 宿主每帧合成鼠标事件：每个键至多一次 MouseDown、一次 MouseDrag、一次 MouseUp，顺序固定为全部 Down、Drag、Up。
/// </para>
/// </remarks>
public static class UiEvent
{
    // 自定义控件需要逐事件的鼠标与按键数据，Verse 没有对应封装；Event.current 只在本属性读取。
#pragma warning disable RS0030
    private static UnityEngine.Event? Current => UnityEngine.Event.current;
#pragma warning restore RS0030

    /// <summary>当前处于 GUI 回调中（存在 <c>Event.current</c>）。</summary>
    public static bool Exists => Current is not null;

    /// <summary>事件类型（<c>Event.current.type</c>）；没有事件时为 <c>Ignore</c>。</summary>
    public static UnityEngine.EventType Type => Current?.type ?? UnityEngine.EventType.Ignore;

    /// <summary>原始事件类型（<c>Event.current.rawType</c>），不受裁剪区影响；没有事件时为 <c>Ignore</c>。</summary>
    public static UnityEngine.EventType RawType => Current?.rawType ?? UnityEngine.EventType.Ignore;

    /// <summary>鼠标键（0 左、1 右、2 中）；没有事件时为 -1。非鼠标事件中的值没有意义。</summary>
    public static int Button => Current?.button ?? -1;

    /// <summary>
    /// 鼠标位置，GUI 坐标（原点左上、Y 向下），并且是当前 GUI 组的局部坐标，与传给 <c>Widgets</c> 的矩形同一坐标系；
    /// 没有事件时为零向量。与 <see cref="UnityRect.Contains"/> 搭配使用。屏幕级 UI 坐标用 Verse 的 <c>UI.MousePositionOnUIInverted</c>。
    /// </summary>
    public static Vector2 MousePosition
    {
        get
        {
            UnityEngine.Event? current = Current;
            return current is null ? Vector2.Zero : current.mousePosition.ToNumerics();
        }
    }

    /// <summary>
    /// 事件增量（<c>Event.current.delta</c>）：MouseDrag 中为本帧鼠标位移，ScrollWheel 中为滚轮量（向下为正 Y，每格 3，见
    /// <see cref="EngineInput.ScrollDeltaImgui"/>）；没有事件时为零向量。
    /// </summary>
    public static Vector2 Delta
    {
        get
        {
            UnityEngine.Event? current = Current;
            return current is null ? Vector2.Zero : current.delta.ToNumerics();
        }
    }

    /// <summary>按键事件的键码；没有事件时为 <c>None</c>。</summary>
    public static UnityEngine.KeyCode KeyCode => Current?.keyCode ?? UnityEngine.KeyCode.None;

    /// <summary>当前事件是指定键的 MouseDown（且未被消费、不在裁剪区外）。</summary>
    public static bool IsMouseDown(int button)
    {
        UnityEngine.Event? current = Current;
        return current is not null && current.type == UnityEngine.EventType.MouseDown && current.button == button;
    }

    /// <summary>当前事件是指定键的 MouseDrag（未被消费；拖动捕获期间在裁剪区外也会收到）。</summary>
    public static bool IsMouseDrag(int button)
    {
        UnityEngine.Event? current = Current;
        return current is not null && current.type == UnityEngine.EventType.MouseDrag && current.button == button;
    }

    /// <summary>当前事件是指定键的 MouseUp（未被消费、不在裁剪区外）。</summary>
    public static bool IsMouseUp(int button)
    {
        UnityEngine.Event? current = Current;
        return current is not null && current.type == UnityEngine.EventType.MouseUp && current.button == button;
    }

    /// <summary>
    /// 原始事件是指定键的 MouseUp，不论鼠标是否在裁剪区内（<c>rawType</c>）。用于在任何位置松开时结束拖动。
    /// </summary>
    public static bool IsRawMouseUp(int button)
    {
        UnityEngine.Event? current = Current;
        return current is not null && current.rawType == UnityEngine.EventType.MouseUp && current.button == button;
    }

    /// <summary>当前事件是指定键码的 KeyDown（未被消费）。</summary>
    public static bool IsKeyDown(UnityEngine.KeyCode key)
    {
        UnityEngine.Event? current = Current;
        return current is not null && current.type == UnityEngine.EventType.KeyDown && current.keyCode == key;
    }

    /// <summary>当前事件是滚轮事件（未被消费、不在裁剪区外）。</summary>
    public static bool IsScrollWheel => Type == UnityEngine.EventType.ScrollWheel;

    /// <summary>消费当前事件（<c>Event.current.Use()</c>），使后续控件看到 <c>Used</c>；没有事件时不做任何事。</summary>
    public static void Use() => Current?.Use();

    /// <summary>
    /// 当前事件对象，只用于传给签名中带 <c>Event</c> 的游戏 API，例如 <c>new GizmoResult(GizmoState.Interacted, UiEvent.CurrentForGameApi)</c>；
    /// 不要读取它的成员。
    /// </summary>
    public static UnityEngine.Event? CurrentForGameApi => Current;
}
