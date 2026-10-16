using System.Globalization;
using Mugirl.Platform;
using Mugirl.Platform.Ui;

namespace Mugirl.Features.Corporate;

/// <summary>
/// 窗口级的瞬时界面状态：控件的悬停、按下、选中、波纹，以及平滑滚动与滚动条拖动。
/// 属于窗口实例，不进存档，也不做静态缓存；窗口关闭后随实例释放。
/// </summary>
internal sealed class CorporateMotion
{
    /// <summary>一个控件的动画值；只在 Repaint 中推进，同一引擎帧只推进一次。</summary>
    internal sealed class ControlState
    {
        internal float Hover;
        internal float Press;
        internal float Selection;
        internal float Enabled;
        internal bool Held;
        internal double RippleStarted = CorporateClock.Never;
        internal Vector2 RippleOrigin;
        internal double LastSeen;
        internal int LastFrame = -1;
    }

    /// <summary>一个滚动视图的当前位置、目标位置与拖动状态。</summary>
    internal sealed class ScrollState
    {
        internal Vector2 Position;
        internal Vector2 Target;
        internal Vector2 LastReturned;
        internal double LastSeen;
        internal float DragOffset;
        internal int LastFrame = -1;
        internal bool Initialized;
        internal bool Dragging;
    }

    private const int ControlLimit = 2048;
    private const int ScrollLimit = 96;
    private const int ControlNameLimit = 1024;
    private const double CleanupInterval = 5.0;
    private const double ControlLifetime = 25.0;
    private const double ScrollLifetime = 45.0;
    private const float MaxFrameDelta = 0.05f;
    private const float FirstFrameDelta = 1f / 60f;

    private readonly Dictionary<int, ControlState> controls = [];
    private readonly Dictionary<int, ScrollState> scrolls = [];
    private readonly Dictionary<int, string> controlNames = [];
    // 只记录被按下的控件，松开时不必遍历全部（最多 2048 个）控件。
    private readonly List<ControlState> held = [];
    private readonly List<int> expired = [];
    private readonly List<(double LastSeen, int Key)> evictionOrder = [];
    private int lastEngineFrame = -1;
    private bool hasRepainted;
    private double lastRepaint;
    private double lastCleanup;
    private int ownedHotControl;
    private ScrollState? ownedScroll;

    /// <summary>最近一次 <see cref="BeginFrame"/> 读到的实时秒数。</summary>
    internal double Now { get; private set; }

    /// <summary>两次 Repaint 之间的秒数，截断到 0.05 秒；第一帧按 60 FPS 计。</summary>
    internal float Delta { get; private set; }

    /// <summary>窗口自己的 Repaint 帧号，每个引擎帧最多加一。</summary>
    internal int Frame { get; private set; }

    /// <summary>让滚动条拖动独占鼠标（同时释放其他输入）。</summary>
    internal void CaptureInput(ScrollState state, int control)
    {
        ReleaseInput();
        ownedScroll = state;
        ownedHotControl = control;
        state.Dragging = true;
        UiHotControl.Capture(control);
    }

    /// <summary>释放滚动条拖动与所有按下状态；窗口关闭、内容不可交互时调用。</summary>
    internal void ReleaseInput()
    {
        if (ownedHotControl != 0)
        {
            UiHotControl.Release(ownedHotControl);
        }

        if (ownedScroll is not null)
        {
            ownedScroll.Dragging = false;
        }

        ownedHotControl = 0;
        ownedScroll = null;
        ReleaseHeld();
    }

    /// <summary>结束 <paramref name="state"/> 的拖动；它持有鼠标时一并释放全部输入。</summary>
    internal void ReleaseScrollInput(ScrollState state)
    {
        if (ownedScroll == state)
        {
            ReleaseInput();
        }
        else
        {
            state.Dragging = false;
        }
    }

    /// <summary>记录控件在本次按下中被按住，直到松开或释放输入。</summary>
    internal void Press(ControlState state)
    {
        if (!state.Held)
        {
            state.Held = true;
            held.Add(state);
        }
    }

    /// <summary>
    /// 每个帧作用域开始时调用：左键在任何位置松开都结束按下；每个引擎帧的第一个 Repaint 推进窗口帧号、
    /// 计算帧时长，并定期清理长时间未绘制的控件与滚动状态。
    /// </summary>
    internal void BeginFrame()
    {
        Now = CorporateClock.Now;
        if (UiEvent.IsRawMouseUp(0))
        {
            ReleaseHeld();
        }

        int engineFrame = EngineClock.FrameCount;
        if (!GuiPhase.IsRepaint || lastEngineFrame == engineFrame)
        {
            return;
        }

        lastEngineFrame = engineFrame;
        Delta = hasRepainted ? UnityMathf.Clamp((float)(Now - lastRepaint), 0f, MaxFrameDelta) : FirstFrameDelta;
        hasRepainted = true;
        lastRepaint = Now;
        Frame++;
        // 切换页签时列表可能在收到 MouseUp 之前消失，持有的拖动随之结束。
        if (ownedScroll is not null && ownedScroll.LastFrame < Frame - 1)
        {
            ReleaseInput();
        }

        if (Now - lastCleanup < CleanupInterval)
        {
            return;
        }

        lastCleanup = Now;
        RemoveExpired();
    }

    /// <summary>取得（必要时创建）控件状态，并在 Repaint 中推进其动画。</summary>
    internal ControlState Control(int id, bool hovered, bool enabled, bool selected)
    {
        if (!controls.TryGetValue(id, out ControlState? state))
        {
            if (controls.Count >= ControlLimit)
            {
                EvictOldestControls();
            }

            state = new ControlState { Enabled = enabled ? 1f : 0f, Selection = selected ? 1f : 0f };
            controls.Add(id, state);
        }

        state.LastSeen = Now;
        // Layout 与输入事件中的可用状态可能被暂时锁定；第一个 Repaint 前一直按当前值播种，输入锁定不会显示成禁用。
        if (state.LastFrame < 0)
        {
            state.Enabled = enabled ? 1f : 0f;
            state.Selection = selected ? 1f : 0f;
        }

        if (GuiPhase.IsRepaint && state.LastFrame != Frame)
        {
            state.LastFrame = Frame;
            state.Hover = CorporateEasing.Approach(state.Hover, hovered ? 1f : 0f, 17f, Delta);
            state.Press = CorporateEasing.Approach(state.Press, state.Held && hovered && enabled ? 1f : 0f, 25f, Delta);
            state.Selection = CorporateEasing.Approach(state.Selection, selected ? 1f : 0f, 15f, Delta);
            state.Enabled = CorporateEasing.Approach(state.Enabled, enabled ? 1f : 0f, 13f, Delta);
        }

        return state;
    }

    /// <summary>取得（必要时创建）滚动视图状态；超过上限时淘汰最久未绘制的一个。</summary>
    internal ScrollState Scroll(int id)
    {
        if (!scrolls.TryGetValue(id, out ScrollState? state))
        {
            if (scrolls.Count >= ScrollLimit)
            {
                int oldest = 0;
                double time = double.MaxValue;
                foreach (KeyValuePair<int, ScrollState> pair in scrolls)
                {
                    if (pair.Value.LastSeen < time)
                    {
                        time = pair.Value.LastSeen;
                        oldest = pair.Key;
                    }
                }

                ReleaseScrollInput(scrolls[oldest]);
                scrolls.Remove(oldest);
            }

            state = new ScrollState();
            scrolls.Add(id, state);
        }

        state.LastSeen = Now;
        return state;
    }

    /// <summary>输入框的控件名（用于命名焦点）；按控件键缓存，避免每个事件拼接字符串。</summary>
    internal string ControlName(int key)
    {
        if (!controlNames.TryGetValue(key, out string? name))
        {
            if (controlNames.Count >= ControlNameLimit)
            {
                controlNames.Clear();
            }

            name = "corporate/input/" + key.ToString(CultureInfo.InvariantCulture);
            controlNames.Add(key, name);
        }

        return name;
    }

    private void ReleaseHeld()
    {
        for (int i = 0; i < held.Count; i++)
        {
            held[i].Held = false;
        }

        held.Clear();
    }

    private void RemoveExpired()
    {
        expired.Clear();
        foreach (KeyValuePair<int, ControlState> pair in controls)
        {
            if (Now - pair.Value.LastSeen > ControlLifetime)
            {
                expired.Add(pair.Key);
            }
        }

        for (int i = 0; i < expired.Count; i++)
        {
            controls.Remove(expired[i]);
        }

        expired.Clear();
        foreach (KeyValuePair<int, ScrollState> pair in scrolls)
        {
            if (Now - pair.Value.LastSeen > ScrollLifetime)
            {
                expired.Add(pair.Key);
            }
        }

        for (int i = 0; i < expired.Count; i++)
        {
            ReleaseScrollInput(scrolls[expired[i]]);
            scrolls.Remove(expired[i]);
        }

        expired.Clear();
    }

    // 达到上限时一次淘汰最久未绘制的四分之一：滚动大目录会不断产生新控件，逐个淘汰每次都要全表扫描。
    private void EvictOldestControls()
    {
        evictionOrder.Clear();
        foreach (KeyValuePair<int, ControlState> pair in controls)
        {
            evictionOrder.Add((pair.Value.LastSeen, pair.Key));
        }

        evictionOrder.Sort(static (a, b) => a.LastSeen.CompareTo(b.LastSeen));
        int count = Math.Max(1, evictionOrder.Count / 4);
        for (int i = 0; i < count; i++)
        {
            controls.Remove(evictionOrder[i].Key);
        }

        evictionOrder.Clear();
    }
}
