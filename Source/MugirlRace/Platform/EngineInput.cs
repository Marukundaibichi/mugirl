using System.Runtime.CompilerServices;
using Waylith.Input;

namespace Mugirl.Platform;

/// <summary>
/// 只读访问 Waylith 原生输入状态，替代 <c>UnityEngine.Input</c> 以及借 <c>Event.current</c> 判断鼠标键的写法。
/// </summary>
/// <remarks>
/// <para>
/// 引擎每帧先调用 <c>InputState.BeginFrame</c>，再在同一次 <c>Glfw.PollEvents</c> 中让 <c>GlfwInputSource</c> 写入原生状态、
/// 宿主 <c>GlfwInputBridge</c> 写入 Unity 兼容输入，之后才派发本帧的全部 IMGUI 事件。所以在 GUI 回调里，这些属性回答的是
/// “本引擎帧内是否发生过”。同一帧会派发多次 GUI 事件（每个输入事件前都有 Layout，最后有 Repaint），
/// <c>…ThisFrame</c> 在这些事件中都为真：要“只处理一次”时用 <see cref="EngineClock.FrameCount"/> 去重。
/// </para>
/// <para>
/// 优先使用 Verse 控件与 <c>KeyBindingDef</c>；只在 Verse 没有封装时使用本类（例如在区域按钮回调里区分左右键）。
/// 窗口失去焦点时原生状态被清空，键盘事件也不再记录。
/// </para>
/// </remarks>
public static class EngineInput
{
    /// <summary>宿主把一格滚轮换算成 IMGUI <c>Event.delta</c> 的倍数（<c>ImguiDispatch.RunScrollPass</c> 中的 <c>* 3f</c>）。</summary>
    public const float ImguiScrollUnitsPerNotch = 3f;

    /// <summary>本引擎帧内右键按下过。</summary>
    public static bool RightMousePressedThisFrame => InputState.WasMouseButtonPressedThisFrame(MouseButton.Right);

    /// <summary>本引擎帧内右键松开过。</summary>
    public static bool RightMouseReleasedThisFrame => InputState.WasMouseButtonReleasedThisFrame(MouseButton.Right);

    /// <summary>右键当前处于按住状态。</summary>
    public static bool RightMouseDown => InputState.IsMouseButtonDown(MouseButton.Right);

    /// <summary>本引擎帧内左键按下过。</summary>
    public static bool LeftMousePressedThisFrame => InputState.WasMouseButtonPressedThisFrame(MouseButton.Left);

    /// <summary>本引擎帧内左键松开过。</summary>
    public static bool LeftMouseReleasedThisFrame => InputState.WasMouseButtonReleasedThisFrame(MouseButton.Left);

    /// <summary>左键当前处于按住状态。</summary>
    public static bool LeftMouseDown => InputState.IsMouseButtonDown(MouseButton.Left);

    /// <summary>本引擎帧内中键按下过。</summary>
    public static bool MiddleMousePressedThisFrame => InputState.WasMouseButtonPressedThisFrame(MouseButton.Middle);

    /// <summary>本引擎帧内中键松开过。</summary>
    public static bool MiddleMouseReleasedThisFrame => InputState.WasMouseButtonReleasedThisFrame(MouseButton.Middle);

    /// <summary>中键当前处于按住状态。</summary>
    public static bool MiddleMouseDown => InputState.IsMouseButtonDown(MouseButton.Middle);

    /// <summary>
    /// 按 Unity/IMGUI 的键号（0 左、1 右、2 中，与 <c>Event.button</c> 相同，也与 Waylith <c>MouseButton</c> 的值相同）查询本帧是否按下。
    /// 超出范围的键号得 false。
    /// </summary>
    public static bool ButtonPressedThisFrame(int imguiButton) => InputState.WasMouseButtonPressedThisFrame((MouseButton)imguiButton);

    /// <summary>按 IMGUI 键号查询本帧是否松开；超出范围得 false。</summary>
    public static bool ButtonReleasedThisFrame(int imguiButton) => InputState.WasMouseButtonReleasedThisFrame((MouseButton)imguiButton);

    /// <summary>按 IMGUI 键号查询当前是否按住；超出范围得 false。</summary>
    public static bool ButtonDown(int imguiButton) => InputState.IsMouseButtonDown((MouseButton)imguiButton);

    /// <summary>任一 Shift 按住。</summary>
    public static bool ShiftDown => InputState.ShiftDown;

    /// <summary>任一 Ctrl 按住。</summary>
    public static bool ControlDown => InputState.CtrlDown;

    /// <summary>任一 Alt 按住。</summary>
    public static bool AltDown => InputState.AltDown;

    /// <summary>
    /// 本引擎帧累计的原始滚轮偏移：向上滚为正 Y，单位是滚轮格（macOS 已除以 3），与 Unity <c>Input.mouseScrollDelta</c> 的方向相同
    /// （宿主把同一个 GLFW 回调同时写入两边）。替代 <c>Input.mouseScrollDelta</c>；在 GUI 中处理滚动时用 <see cref="ScrollDeltaImgui"/>。
    /// </summary>
    public static Vector2 ScrollDelta => InputState.ScrollDelta;

    /// <summary>
    /// 本引擎帧累计的滚轮量，换算为 IMGUI <c>Event.delta</c> 的约定：向下滚为正 Y，每格 3；按住 Shift 且没有横向滚动时，
    /// 纵向滚动转为横向（X）。与宿主派发给 <c>ScrollWheel</c> 事件的 <c>delta</c> 相同。没有滚动时为零向量。
    /// </summary>
    public static Vector2 ScrollDeltaImgui => ToImguiScrollDelta(InputState.ScrollDelta, InputState.ShiftDown);

    /// <summary>
    /// 把 GLFW 原始滚轮偏移（向上滚为正 Y，macOS 已除以 3）换算成 IMGUI 约定，逐步照抄宿主 <c>ImguiDispatch.RunScrollPass</c>：
    /// Shift 且 <c>x == 0</c> 时改为 <c>(y, 0)</c>，然后取负（IL <c>neg</c>）并乘 3。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 ToImguiScrollDelta(Vector2 rawScroll, bool shift)
    {
        if (shift && rawScroll.X == 0f)
        {
            rawScroll = new Vector2(rawScroll.Y, 0f);
        }

        return new Vector2(-rawScroll.X * ImguiScrollUnitsPerNotch, -rawScroll.Y * ImguiScrollUnitsPerNotch);
    }
}
