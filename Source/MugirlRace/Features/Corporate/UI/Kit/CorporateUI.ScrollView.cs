using Mugirl.Platform;
using Mugirl.Platform.Ui;

namespace Mugirl.Features.Corporate;

internal static partial class CorporateUI
{
    private const float WheelStep = 32f;
    private const float ScrollSpeed = 18f;

    /// <summary>一层进行中的滚动视图：视口、内容、状态与进入前的作用域路径。</summary>
    private readonly struct ScrollFrame(Rect rect, Rect view, CorporateMotion.ScrollState state, Vector2 maximum, bool mouseInside, bool enabled, int previousPath)
    {
        internal readonly Rect Rect = rect;
        internal readonly Rect View = view;
        internal readonly CorporateMotion.ScrollState State = state;
        internal readonly Vector2 Maximum = maximum;
        internal readonly bool MouseInside = mouseInside;
        internal readonly bool Enabled = enabled;
        internal readonly int PreviousPath = previousPath;
    }

    /// <summary>鼠标位于所有外层滚动视口之内（外层视口之外的内容被裁掉，不应响应悬停与点击）。</summary>
    private static bool InsideScrollClips()
    {
        if (current is null)
        {
            return true;
        }

        foreach (ScrollFrame frame in current.Scrolls)
        {
            if (!frame.MouseInside)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 平滑滚动视图：滚轮与外部改写的位置先设为目标，Repaint 中以指数逼近移动；右侧为自绘滚动条，可拖动或点击轨道。
    /// 没有帧作用域时退回原版 <c>Widgets.BeginScrollView</c>。必须与 <see cref="EndScrollView"/> 成对调用。
    /// </summary>
    internal static void BeginScrollView(Rect rect, ref UVector2 position, Rect view, string? id = null)
    {
        FrameScope? scope = current;
        CorporateMotion? motion = scope?.Motion;
        if (scope is null || motion is null)
        {
            Widgets.BeginScrollView(rect, ref position, view);
            return;
        }

        int key = ControlKey(rect, ScrollKind, id);
        CorporateMotion.ScrollState state = motion.Scroll(key);
        Vector2 maximum = new(UnityMathf.Max(0f, view.width - rect.width), UnityMathf.Max(0f, view.height - rect.height));
        Vector2 requested = position.ToNumerics();
        if (!state.Initialized || UnityVector.SqrMagnitude(requested - state.LastReturned) > 0.01f)
        {
            state.Position = requested;
            state.Target = requested;
            state.Initialized = true;
        }

        state.Target = Clamp(state.Target, maximum);
        if (GuiPhase.IsRepaint && state.LastFrame != motion.Frame)
        {
            state.LastFrame = motion.Frame;
            state.Position = new Vector2(
                CorporateEasing.Approach(state.Position.X, state.Target.X, ScrollSpeed, motion.Delta),
                CorporateEasing.Approach(state.Position.Y, state.Target.Y, ScrollSpeed, motion.Delta));
        }

        state.Position = Clamp(state.Position, maximum);
        ScrollFrame frame = new(rect, view, state, maximum, InsideScrollClips() && Mouse.IsOver(rect), Interactive, scope.Path);
        DrawScrollBar(motion, frame, key);
        position = state.Position.ToUnity();
        state.LastReturned = state.Position;
        scope.Scrolls.Push(frame);
        scope.Path = key;
        Widgets.BeginGroup(rect);
        Widgets.BeginGroup(new Rect(-state.Position.X - view.x, -state.Position.Y - view.y,
            UnityMathf.Max(view.width + view.x, rect.width), UnityMathf.Max(view.height + view.y, rect.height)));
    }

    private static Vector2 Clamp(Vector2 value, Vector2 maximum)
        => new(UnityMathf.Clamp(value.X, 0f, maximum.X), UnityMathf.Clamp(value.Y, 0f, maximum.Y));

    private static void DrawScrollBar(CorporateMotion motion, ScrollFrame frame, int key)
    {
        CorporateMotion.ScrollState state = frame.State;
        if (frame.Maximum.Y <= 0f)
        {
            motion.ReleaseScrollInput(state);
            return;
        }

        Rect track = new(frame.Rect.xMax - 9f, frame.Rect.y + 2f, 7f, UnityMathf.Max(1f, frame.Rect.height - 4f));
        float thumbHeight = UnityMathf.Min(track.height, UnityMathf.Max(26f, track.height * frame.Rect.height / frame.View.height));
        float travel = UnityMathf.Max(1f, track.height - thumbHeight);
        Rect thumb = new(track.x + 2f, track.y + state.Position.Y / frame.Maximum.Y * travel, 3f, thumbHeight);
        int control = UiHotControl.NewControlId(key, track);
        if (!frame.Enabled || (state.Dragging && !UiHotControl.IsCaptured(control)))
        {
            motion.ReleaseScrollInput(state);
        }

        if (frame.Enabled && frame.MouseInside && UiEvent.IsMouseDown(0) && Mouse.IsOver(track))
        {
            Vector2 mouse = UiEvent.MousePosition;
            state.DragOffset = UnityRect.Contains(thumb, mouse) ? mouse.Y - thumb.y : thumbHeight * 0.5f;
            motion.CaptureInput(state, control);
            DragTo(state, frame, track, travel, mouse.Y);
            UiEvent.Use();
        }

        if (state.Dragging && UiHotControl.IsCaptured(control))
        {
            if (UiEvent.Type == UnityEngine.EventType.MouseDrag)
            {
                DragTo(state, frame, track, travel, UiEvent.MousePosition.Y);
                UiEvent.Use();
            }

            if (UiEvent.RawType == UnityEngine.EventType.MouseUp)
            {
                motion.ReleaseScrollInput(state);
                if (UiEvent.Type == UnityEngine.EventType.MouseUp)
                {
                    UiEvent.Use();
                }
            }
        }

        Fill(new Rect(track.center.x, track.y, 1f, track.height), Gray(0.2f));
        Fill(thumb, Gray(state.Dragging || (frame.Enabled && frame.MouseInside && Mouse.IsOver(track)) ? 0.92f : 0.48f));
    }

    private static void DragTo(CorporateMotion.ScrollState state, ScrollFrame frame, Rect track, float travel, float mouseY)
    {
        float y = UnityMathf.Clamp01((mouseY - track.y - state.DragOffset) / travel) * frame.Maximum.Y;
        state.Position.Y = y;
        state.Target.Y = y;
    }

    /// <summary>
    /// 结束 <see cref="BeginScrollView"/>。内层视图先处理滚轮，嵌套列表滚到头之前不会带动外层页面；
    /// 视图能移动（或仍在向边界靠近）时消费滚轮事件。
    /// </summary>
    internal static void EndScrollView()
    {
        FrameScope? scope = current;
        if (scope?.Motion is null)
        {
            Widgets.EndScrollView();
            return;
        }

        ScrollFrame frame = scope.Scrolls.Pop();
        Widgets.EndGroup();
        Widgets.EndGroup();
        scope.Path = frame.PreviousPath;
        if (!frame.Enabled || !frame.MouseInside || !UiEvent.IsScrollWheel)
        {
            return;
        }

        Vector2 delta = UiEvent.Delta;
        // 1.7 宿主把 Shift + 滚轮转成横向滚动；只能纵向滚动的视图仍按纵向处理，与 1.6 一致。
        if (EngineInput.ShiftDown && delta.Y == 0f && delta.X != 0f && frame.Maximum.X <= 0f)
        {
            delta = new Vector2(0f, delta.X);
        }

        CorporateMotion.ScrollState state = frame.State;
        Vector2 target = new(
            UnityMathf.Clamp(state.Target.X + delta.X * WheelStep, 0f, frame.Maximum.X),
            UnityMathf.Clamp(state.Target.Y + delta.Y * WheelStep, 0f, frame.Maximum.Y));
        bool changed = UnityVector.SqrMagnitude(target - state.Target) > 0.01f;
        bool settling = (delta.Y > 0f && state.Position.Y < frame.Maximum.Y - 0.1f)
            || (delta.Y < 0f && state.Position.Y > 0.1f)
            || (delta.X > 0f && state.Position.X < frame.Maximum.X - 0.1f)
            || (delta.X < 0f && state.Position.X > 0.1f);
        if (changed || settling)
        {
            state.Target = target;
            UiEvent.Use();
        }
    }
}
