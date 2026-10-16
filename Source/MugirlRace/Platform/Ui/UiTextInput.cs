namespace Mugirl.Platform.Ui;

/// <summary>
/// 不画背景与边框、文字颜色和内边距自定的单行文本框，供自绘外框的界面使用。原版外观的输入框直接用 Verse
/// 的 <c>Widgets.TextField</c>、<c>Widgets.TextFieldNumeric</c> 或 <c>QuickSearchWidget</c>。
/// </summary>
/// <remarks>
/// <para>
/// Verse 只提供原版样式（<c>Text.CurTextFieldStyle</c>）的文本框；去掉背景、改文字颜色与内边距必须复制一份 <c>GUIStyle</c>
/// 并直接调用 <c>GUI.TextField</c>，这与 Verse 自己的 <c>Widgets.TextField</c> 走同一条 UnityStubs 输入路径
/// （焦点、光标、输入法组合输入都相同），只是样式不同。UnityStubs 在背景为空时不画底色与边框。
/// </para>
/// <para>
/// 1.7 的文本框不读取 <c>GUI.enabled</c>，禁用时不能靠它变成只读：不可交互时改画标签。不传最大长度：截断会打断输入法组合输入，
/// 长度检查放在提交时。给焦点命名时，先调用 <see cref="UiFocus.SetNextControlName"/> 再调用本方法。
/// </para>
/// </remarks>
public static class UiTextInput
{
    // StaticCacheLifecycle: 进程级样式缓存，键为字体、文字颜色、内边距与对齐；调用点固定，条目为个位数。
    // GUIStyle 只含外观数据，不含游戏状态，永不清理。
    private static readonly Dictionary<StyleKey, UnityEngine.GUIStyle> styles = [];

    /// <summary>
    /// 绘制并返回编辑后的文本。字体取当前 <c>Text.Font</c>；绘制期间 GUI 颜色为白色，文字颜色完全由 <paramref name="textColor"/> 决定
    /// （需要淡出时把透明度乘进 <paramref name="textColor"/>）。<paramref name="text"/> 为 null 时按空串处理。
    /// </summary>
    /// <param name="rect">输入区域（含内边距）。</param>
    /// <param name="text">当前文本。</param>
    /// <param name="textColor">各状态（普通、悬停、聚焦、按下）统一的文字颜色。</param>
    /// <param name="paddingX">左右内边距（像素）。</param>
    /// <param name="paddingY">上下内边距（像素）。</param>
    /// <param name="alignment">文字对齐。</param>
    public static string Chromeless(Rect rect, string? text, Color textColor, int paddingX = 9, int paddingY = 4, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        UnityEngine.GUIStyle style = StyleFor(new StyleKey(Text.Font, textColor.r, textColor.g, textColor.b, textColor.a, paddingX, paddingY, alignment));
        using (new TextBlock(UnityColor.White))
        {
            // Verse 没有接受自定义 GUIStyle 的文本框；调用与 Widgets.TextField 相同的 UnityStubs 入口，只换样式。
#pragma warning disable RS0030
            return UnityEngine.GUI.TextField(rect, text ?? string.Empty, style);
#pragma warning restore RS0030
        }
    }

    private static UnityEngine.GUIStyle StyleFor(StyleKey key)
    {
        if (styles.TryGetValue(key, out UnityEngine.GUIStyle? cached))
        {
            return cached;
        }

        Color color = new(key.R, key.G, key.B, key.A);
        UnityEngine.GUIStyle style = new(Text.textFieldStyles[(int)key.Font])
        {
            alignment = key.Alignment,
            padding = new UnityEngine.RectOffset(key.PaddingX, key.PaddingX, key.PaddingY, key.PaddingY),
        };
        foreach (UnityEngine.GUIStyleState state in (ReadOnlySpan<UnityEngine.GUIStyleState>)[style.normal, style.hover, style.focused, style.active])
        {
            state.textColor = color;
            state.background = null;
        }

        styles.Add(key, style);
        return style;
    }

    // 颜色按分量入键：Color 的 Equals/GetHashCode 属于 UnityStubs 逻辑，不能用作字典键。
    private readonly record struct StyleKey(GameFont Font, float R, float G, float B, float A, int PaddingX, int PaddingY, TextAnchor Alignment);
}
