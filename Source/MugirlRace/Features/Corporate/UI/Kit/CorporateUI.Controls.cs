using Mugirl.Platform.Ui;

namespace Mugirl.Features.Corporate;

internal static partial class CorporateUI
{
    private const float RippleDuration = 0.52f;

    /// <summary>
    /// 控件的悬停、按下与点击。点击仍由 <c>Widgets.ButtonInvisible</c> 判定（任意鼠标键、按下与松开须在同一矩形内）；
    /// 按下与松开的状态读 <see cref="UiEvent"/>，与 1.6 的 <c>Event.current</c> 判断逐项对应。
    /// </summary>
    private static CorporateMotion.ControlState? Interaction(Rect rect, int key, bool enabled, bool selected, out bool clicked)
    {
        bool acceptsInput = enabled && Interactive;
        bool insideClips = InsideScrollClips();
        bool hover = acceptsInput && insideClips && Mouse.IsOver(rect);
        CorporateMotion? motion = current?.Motion;
        CorporateMotion.ControlState? state = motion?.Control(key, hover, enabled, selected);
        if (motion is not null && state is not null)
        {
            if (!acceptsInput || UiEvent.IsRawMouseUp(0))
            {
                state.Held = false;
            }

            if (hover && UiEvent.IsMouseDown(0))
            {
                motion.Press(state);
                StartRipple(state, rect);
            }
        }

        // 不可用的控件不调用 ButtonInvisible：它会消费事件。
        clicked = acceptsInput && insideClips && Widgets.ButtonInvisible(rect);
        if (clicked && state is not null)
        {
            StartRipple(state, rect);
        }

        return state;
    }

    private static void StartRipple(CorporateMotion.ControlState state, Rect rect)
    {
        Vector2 mouse = UiEvent.MousePosition;
        state.RippleOrigin = new Vector2(mouse.X - rect.x, mouse.Y - rect.y);
        state.RippleStarted = CorporateClock.Now;
    }

    /// <summary>
    /// 带悬停、按下、选中动画与点击波纹的按钮。<paramref name="primary"/> 表示强调（选中）样式。
    /// <paramref name="id"/> 与 <paramref name="index"/> 组成控件键；列表项用固定 id 加序号，避免每个事件拼接字符串。
    /// </summary>
    internal static bool Button(Rect rect, string label, bool enabled = true, bool primary = false, string? id = null, int index = NoIndex)
    {
        CorporateMotion.ControlState? state = Interaction(rect, ControlKey(rect, ButtonKind, id, index), enabled, primary, out bool clicked);
        float hover = state?.Hover ?? (enabled && Interactive && InsideScrollClips() && Mouse.IsOver(rect) ? 1f : 0f);
        float press = state?.Press ?? 0f;
        float selection = state?.Selection ?? (primary ? 1f : 0f);
        float availability = state?.Enabled ?? (enabled ? 1f : 0f);
        Rect face = rect.ContractedBy(press * 1.4f);
        Color secondary = Gray(UnityMathf.Lerp(0.115f, 0.225f, hover));
        Color fill = UnityColor.Lerp(Gray(0.09f), UnityColor.Lerp(secondary, Gray(UnityMathf.Lerp(0.94f, 1f, hover)), selection), availability);
        Fill(new Rect(face.x, face.yMax, face.width, 2f), Gray(0f, 0.26f * hover));
        Fill(face, fill);
        Outline(face, UnityColor.Lerp(Gray(0.22f + 0.24f * hover), Gray(1f), selection));
        DrawRipple(face, state, selection > 0.5f ? Gray(0f, 0.13f) : Gray(1f, 0.12f));
        // 选中动作的底色经过中灰时保持文字对比度。
        Color text = UnityColor.Lerp(Muted, fill.r > 0.48f ? Gray(0.035f) : Ink, availability);
        Label(face.ContractedBy(4f), label, color: text, anchor: TextAnchor.MiddleCenter);
        return clicked;
    }

    /// <summary>可点击的列表行或卡片底板：悬停加亮、选中时左侧标记与描边。</summary>
    internal static bool Row(Rect rect, bool selected, string? id = null, int index = NoIndex)
    {
        CorporateMotion.ControlState? state = Interaction(rect, ControlKey(rect, RowKind, id, index), true, selected, out bool clicked);
        float hover = state?.Hover ?? (Interactive && InsideScrollClips() && Mouse.IsOver(rect) ? 1f : 0f);
        float selection = state?.Selection ?? (selected ? 1f : 0f);
        Fill(rect, Gray(0.07f + hover * 0.035f + selection * 0.065f));
        Fill(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Gray(0.21f, 0.6f));
        if (selection > 0.001f)
        {
            float height = UnityMathf.Lerp(8f, rect.height - 12f, CorporateEasing.EaseOutCubic(selection));
            Fill(new Rect(rect.x, rect.center.y - height / 2f, 3f, height), Gray(1f, selection));
            Outline(rect, Gray(0.72f, selection * 0.35f));
        }

        DrawRipple(rect, state, Gray(1f, 0.09f));
        return clicked;
    }

    private static void DrawRipple(Rect rect, CorporateMotion.ControlState? state, Color color)
    {
        if (state is null || !GuiPhase.IsRepaint)
        {
            return;
        }

        float elapsed = CorporateClock.Since(state.RippleStarted);
        if (elapsed < 0f || elapsed > RippleDuration)
        {
            return;
        }

        float progress = UnityMathf.Clamp01(elapsed / RippleDuration);
        float radius = UnityMathf.Max(rect.width, rect.height) * 1.15f * CorporateEasing.EaseOutCubic(progress);
        color.a *= 1f - progress;
        float centerX = rect.x + state.RippleOrigin.X;
        float centerY = rect.y + state.RippleOrigin.Y;
        Rect square = new(centerX - radius, centerY - radius, radius * 2f, radius * 2f);
        float left = UnityMathf.Max(square.x, rect.x);
        float top = UnityMathf.Max(square.y, rect.y);
        float right = UnityMathf.Min(square.xMax, rect.xMax);
        float bottom = UnityMathf.Min(square.yMax, rect.yMax);
        if (right > left && bottom > top)
        {
            Fill(new Rect(left, top, right - left, bottom - top), color);
        }

        Color edge = color;
        edge.a *= 1.6f;
        if (square.x > rect.x)
        {
            Fill(new Rect(square.x, top, 1f, UnityMathf.Max(0f, bottom - top)), edge);
        }

        if (square.xMax < rect.xMax)
        {
            Fill(new Rect(square.xMax - 1f, top, 1f, UnityMathf.Max(0f, bottom - top)), edge);
        }
    }

    /// <summary>
    /// 无边框单行输入框（自绘底板、描边与聚焦下划线）。不可交互的作用域中只在 Repaint 中绘制、不处理输入，并放弃焦点。
    /// </summary>
    internal static string Input(Rect rect, string? value, string? placeholder = null, string? id = null, int index = NoIndex)
    {
        int key = ControlKey(rect, InputKind, id, index);
        CorporateMotion? motion = current?.Motion;
        string name = InputName(motion, key);
        bool focused = UiFocus.IsFocused(name);
        bool interactive = Interactive;
        bool hovered = interactive && InsideScrollClips() && Mouse.IsOver(rect);
        CorporateMotion.ControlState? state = motion?.Control(key, hovered, true, focused);
        float focus = state?.Selection ?? (focused ? 1f : 0f);
        float hover = state?.Hover ?? (hovered ? 1f : 0f);
        Fill(rect, Gray(0.045f + hover * 0.015f));
        Outline(rect, Gray(0.24f + hover * 0.12f + focus * 0.40f));
        if (focus > 0.001f)
        {
            float width = rect.width * CorporateEasing.EaseOutCubic(focus);
            Fill(new Rect(rect.center.x - width * 0.5f, rect.yMax - 2f, width, 2f), Gray(0.96f, focus));
        }

        string text = value ?? string.Empty;
        string result = text;
        if (interactive || GuiPhase.IsRepaint)
        {
            // 与原版 QuickSearchWidget 一致：字段绘制期间颜色纯白、文字色固定，不传最大长度，以免打断键盘与输入法组合输入。
            UiFocus.SetNextControlName(name);
            result = UiTextInput.Chromeless(rect.ContractedBy(1f), text, Ink, 9, 4, TextAnchor.MiddleLeft);
        }

        if (!interactive)
        {
            result = text;
            if (focused)
            {
                UI.UnfocusCurrentControl();
            }
        }

        if (string.IsNullOrEmpty(result) && !focused && !string.IsNullOrEmpty(placeholder))
        {
            Label(rect.ContractedBy(9f, 3f), placeholder, color: Muted, anchor: TextAnchor.MiddleLeft);
        }

        return result;
    }

    /// <summary>与 <see cref="Input"/> 同一键的输入框当前持有键盘焦点。</summary>
    internal static bool InputFocused(Rect rect, string? id, int index = NoIndex)
        => UiFocus.IsFocused(InputName(current?.Motion, ControlKey(rect, InputKind, id, index)));

    private static string InputName(CorporateMotion? motion, int key)
        => motion?.ControlName(key) ?? "corporate/input/" + key.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// 整数输入框：编辑中保留原文；失去焦点、在框外按下鼠标或按回车时把文本规范为钳制后的数值。
    /// <paramref name="maximum"/> 小于 <paramref name="minimum"/> 时按 <paramref name="minimum"/> 处理。
    /// </summary>
    internal static void Numeric(Rect rect, ref int value, ref string buffer, int minimum, int maximum, string id, int index = NoIndex)
    {
        maximum = Math.Max(minimum, maximum);
        value = UnityMathf.Clamp(value, minimum, maximum);
        if (!InputFocused(rect, id, index))
        {
            buffer = value.ToString();
        }

        buffer = Input(rect, buffer, null, id, index);
        if (int.TryParse(buffer, out int parsed))
        {
            value = UnityMathf.Clamp(parsed, minimum, maximum);
        }

        bool commit = !InputFocused(rect, id, index)
            || (UiEvent.RawType == UnityEngine.EventType.MouseDown && !UnityRect.Contains(rect, UiEvent.MousePosition))
            || UiEvent.IsKeyDown(UnityEngine.KeyCode.Return)
            || UiEvent.IsKeyDown(UnityEngine.KeyCode.KeypadEnter);
        if (commit)
        {
            buffer = value.ToString();
        }
    }

    /// <summary>数量步进器：减、数值框、加与“最大”四个控件；<paramref name="maximum"/> 为 0 时只能选 1 且“最大”不可用。</summary>
    internal static void Quantity(Rect rect, ref int quantity, ref string buffer, int maximum, string id, string maximumLabel)
    {
        const float gap = 4f;
        float stepWidth = UnityMathf.Clamp(rect.width * 0.13f, 22f, 30f);
        float maxWidth = UnityMathf.Clamp(rect.width * 0.26f, 46f, 70f);
        float inputWidth = UnityMathf.Max(32f, rect.width - 2f * stepWidth - maxWidth - 3f * gap);
        if (Button(new Rect(rect.x, rect.y, stepWidth, rect.height), "−", quantity > 1, id: id, index: 0))
        {
            quantity--;
            buffer = quantity.ToString();
        }

        Numeric(new Rect(rect.x + stepWidth + gap, rect.y, inputWidth, rect.height), ref quantity, ref buffer, 1, Math.Max(1, maximum), id, 1);
        if (Button(new Rect(rect.xMax - maxWidth - gap - stepWidth, rect.y, stepWidth, rect.height), "+", quantity < maximum, id: id, index: 2))
        {
            quantity++;
            buffer = quantity.ToString();
        }

        if (Button(new Rect(rect.xMax - maxWidth, rect.y, maxWidth, rect.height), maximumLabel, maximum > 0, id: id, index: 3))
        {
            quantity = maximum;
            buffer = quantity.ToString();
        }
    }

    /// <summary>
    /// 原版搜索框（组合输入、候选位置与焦点由 <see cref="QuickSearchWidget"/> 处理）加占位文字。
    /// 不可交互的作用域中只在 Repaint 中绘制，输入与清除按钮都不响应。
    /// </summary>
    internal static string Search(Rect rect, string? value, QuickSearchWidget widget, string placeholder)
    {
        string text = value ?? string.Empty;
        // 过滤器的 Text 赋值会清空其匹配缓存，只在内容不同时赋值。
        if (!string.Equals(widget.filter.Text, text, StringComparison.Ordinal))
        {
            widget.filter.Text = text;
        }

        bool interactive = Interactive;
        if (interactive || GuiPhase.IsRepaint)
        {
            widget.OnGUI(rect);
        }

        if (!interactive)
        {
            widget.Unfocus();
        }

        if (!widget.filter.Active && !widget.CurrentlyFocused())
        {
            Label(new Rect(rect.x + 28f, rect.y, UnityMathf.Max(0f, rect.width - 28f), rect.height),
                placeholder, color: Muted, anchor: TextAnchor.MiddleLeft);
        }

        return widget.filter.Text;
    }

    /// <summary>页内标题：左侧强调条、中号标题与可选的说明文字；<paramref name="y"/> 推进到标题区之后。</summary>
    internal static void Heading(ref float y, float width, string title, string? subtitle = null)
    {
        Fill(new Rect(0f, y + 5f, 3f, 24f), Accent);
        Label(new Rect(13f, y, UnityMathf.Max(0f, width - 13f), 36f), title, GameFont.Medium);
        y += 40f;
        if (!string.IsNullOrEmpty(subtitle))
        {
            float height = UnityMathf.Max(38f, Text.CalcHeight(subtitle, width));
            Label(new Rect(0f, y, width, height), subtitle, color: Muted);
            y += height + 12f;
        }
    }

    /// <summary>提示框；<paramref name="warning"/> 为 true 时用加粗的左标与右上角的警示点。</summary>
    internal static void Notice(Rect rect, string text, bool warning = false)
    {
        Fill(rect, warning ? Gray(0.14f) : Gray(0.10f));
        Outline(rect, warning ? Gray(0.43f) : Border);
        Fill(new Rect(rect.x, rect.y, warning ? 4f : 2f, rect.height), warning ? Ink : Gray(0.60f));
        if (warning)
        {
            int dots = Math.Min(8, UnityMathf.FloorToInt(rect.width / 16f));
            for (int i = 0; i < dots; i++)
            {
                Fill(new Rect(rect.xMax - 7f - i * 8f, rect.y + 3f, 3f, 3f), Gray(0.68f, 0.55f));
            }
        }

        Label(rect.ContractedBy(12f, 10f), text, color: warning ? Ink : Gray(0.82f));
    }
}
