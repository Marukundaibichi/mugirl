namespace Mugirl.Platform.Ui;

/// <summary>
/// 常用界面绘制，全部经 Verse 完成：颜色作为数据交给 Verse，或用 Verse 的 <c>TextBlock</c> 作用域临时设置并恢复 GUI 颜色。
/// 替代 <c>GUI.color = …; GUI.DrawTexture(…); GUI.color = Color.white;</c> 一类写法。
/// </summary>
/// <remarks>
/// 与 GUI 的原始绘制一样只在 Repaint 中产生图像，其他事件中调用没有副作用，不需要先判断 <see cref="GuiPhase.IsRepaint"/>。
/// 颜色作用域结束后恢复的是进入前的 GUI 颜色，而不是白色；1.6 中“绘制后设回 <c>Color.white</c>”的写法在外层颜色本来是白色时结果相同。
/// </remarks>
public static class UiDraw
{
    /// <summary>纯色填充矩形（<c>Widgets.DrawBoxSolid</c>）；替代 <c>GUI.color = c; GUI.DrawTexture(rect, BaseContent.WhiteTex)</c>。</summary>
    public static void Fill(Rect rect, Color color) => Widgets.DrawBoxSolid(rect, color);

    /// <summary>
    /// 沿矩形内侧描边，线宽 <paramref name="thickness"/>：上、下两条横边占满宽度，左、右两条竖边占满高度，四角各重叠一次
    /// （与 1.6 模组的四次填充写法相同；半透明颜色在四角更深）。Verse 的 <c>Widgets.DrawBox</c> 另按界面缩放取整，需要原版外观时用它。
    /// </summary>
    public static void Border(Rect rect, Color color, float thickness = 1f)
    {
        Fill(new Rect(rect.x, rect.y, rect.width, thickness), color);
        Fill(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), color);
        Fill(new Rect(rect.x, rect.y, thickness, rect.height), color);
        Fill(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), color);
    }

    /// <summary>
    /// 以当前 GUI 颜色拉伸绘制整张纹理，即 <c>GUI.DrawTexture(rect, texture)</c>（经 <c>GenUI.DrawTextureWithMaterial</c>，材质为空时
    /// Verse 走同一调用）。<paramref name="texture"/> 为 null 时不绘制。
    /// </summary>
    public static void Texture(Rect rect, Texture? texture)
    {
        if (texture is not null)
        {
            GenUI.DrawTextureWithMaterial(rect, texture, null);
        }
    }

    /// <summary>以 <paramref name="tint"/> 染色拉伸绘制整张纹理；替代 <c>GUI.color = tint; GUI.DrawTexture(rect, texture)</c>。</summary>
    public static void Texture(Rect rect, Texture? texture, Color tint)
    {
        if (texture is null)
        {
            return;
        }

        using (new TextBlock(tint))
        {
            GenUI.DrawTextureWithMaterial(rect, texture, null);
        }
    }

    /// <summary>
    /// 以 <paramref name="tint"/> 染色绘制纹理的一部分，即 <c>GUI.DrawTextureWithTexCoords(rect, texture, texCoords)</c>：
    /// <paramref name="texCoords"/> 是 Unity 纹理坐标（原点在左下）。注意 Verse 把全零的 <paramref name="texCoords"/> 当作整张纹理。
    /// 要用自上而下的坐标时改用 Verse 的 <c>Widgets.DrawTexturePart</c>（它会翻转 y）。
    /// </summary>
    public static void TexturePart(Rect rect, Texture? texture, Rect texCoords, Color tint)
    {
        if (texture is null)
        {
            return;
        }

        using (new TextBlock(tint))
        {
            GenUI.DrawTextureWithMaterial(rect, texture, null, texCoords);
        }
    }

    /// <summary>
    /// 以 <paramref name="color"/> 绘制标签（GUI 颜色与文字样式颜色相乘，与 1.6 设置 <c>GUI.color</c> 后调用 <c>Widgets.Label</c> 相同）；
    /// 可同时临时设置字体与对齐。三者都在返回前恢复。
    /// </summary>
    public static void Label(Rect rect, string? text, Color color, GameFont? font = null, TextAnchor? anchor = null)
    {
        using (new TextBlock(font, anchor, color))
        {
            Widgets.Label(rect, text ?? string.Empty);
        }
    }

    /// <summary>
    /// 用富文本颜色标签绘制（<c>text.Colorize(color)</c>，即 <c>&lt;color=#RRGGBBAA&gt;</c>）：标签颜色替换文字基础颜色，不依赖 GUI 颜色。
    /// 需要截断时先对纯文本调用 <c>Text.ClampTextWithEllipsis</c> 再调用本方法，否则省略号可能切断标签。
    /// </summary>
    public static void ColorizedLabel(Rect rect, string? text, Color color)
        => Widgets.Label(rect, (text ?? string.Empty).Colorize(color));
}
