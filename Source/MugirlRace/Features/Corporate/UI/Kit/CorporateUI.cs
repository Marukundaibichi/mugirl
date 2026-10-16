using Mugirl.Platform.Ui;

namespace Mugirl.Features.Corporate;

/// <summary>
/// 巨企终端的即时模式界面工具：主题色、帧作用域（透明度与可交互性）、基本绘制与带动画的控件。
/// 全部绘制经 Verse（Widgets、TextBlock）或 <c>Mugirl.Platform.Ui</c> 完成，不直接调用 Unity 兼容层。
/// </summary>
/// <remarks>
/// <para>
/// 当前作用域保存在静态栈顶：GUI 只在主线程运行，作用域由 <c>using</c> 严格嵌套，最外层结束后栈为空。
/// 交互动画状态属于窗口的 <see cref="CorporateMotion"/>，本类不持有任何跨帧的控件状态。
/// </para>
/// <para>
/// 不可交互的作用域（过渡动画期间）中，模组控件不响应输入；输入框与搜索框只在 Repaint 中绘制、不处理输入事件，
/// 对应 1.6 在非 Repaint 事件中设置 <c>GUI.enabled = false</c>（1.7 的控件不再读取 <c>GUI.enabled</c>）。
/// </para>
/// </remarks>
internal static partial class CorporateUI
{
    /// <summary>不带序号的控件标识（只用 id 字符串）。</summary>
    internal const int NoIndex = int.MinValue;

    internal static readonly Color Background = Gray(0.032f);
    internal static readonly Color Surface = Gray(0.070f);
    internal static readonly Color Raised = Gray(0.135f);
    internal static readonly Color Ink = Gray(0.950f);
    internal static readonly Color Muted = Gray(0.650f);
    internal static readonly Color Accent = Gray(0.980f);
    internal static readonly Color Border = Gray(0.220f);
    internal static readonly Color Danger = Gray(0.850f);

    private const string ButtonKind = "button";
    private const string RowKind = "row";
    private const string InputKind = "input";
    private const string ScrollKind = "scroll";

    // StaticCacheLifecycle: 当前帧作用域的栈顶，只在 GUI 回调中由 FrameScope 成对压入与弹出；离开最外层作用域后为 null，不含游戏状态。
    private static FrameScope? current;

    /// <summary>一个绘制作用域：叠乘透明度、限制可交互性，并在结束时恢复字体、对齐、换行与 GUI 颜色。</summary>
    private sealed class FrameScope : IDisposable
    {
        internal readonly CorporateMotion? Motion;
        internal readonly float Opacity;
        internal readonly bool Interactive;
        internal readonly Stack<ScrollFrame> Scrolls = new();
        internal int Path;
        private readonly FrameScope? previous;
        // 不修改任何值的 TextBlock：只记录进入时的文本样式与 GUI 颜色，Dispose 时恢复。
        private readonly TextBlock saved;
        private bool disposed;

        internal FrameScope(CorporateMotion? motion, string scope, float opacity, bool interactive)
        {
            previous = current;
            Motion = motion;
            Path = Hash(previous?.Path ?? 17, scope);
            Opacity = UnityMathf.Clamp01(opacity) * (previous?.Opacity ?? 1f);
            Interactive = interactive && (previous is null || previous.Interactive);
            saved = new TextBlock(newFont: null, newAnchor: null, newWordWrap: null, newColor: null);
            current = this;
            Motion?.BeginFrame();
            if (!Interactive)
            {
                Motion?.ReleaseInput();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            while (Scrolls.Count > 0)
            {
                EndScrollView();
            }

            saved.Dispose();
            current = previous;
        }
    }

    /// <summary>
    /// 开始一个绘制作用域。<paramref name="opacity"/> 与外层相乘；<paramref name="interactive"/> 为 false 或外层不可交互时，
    /// 作用域内的控件不响应输入。返回值必须用 <c>using</c> 结束。
    /// </summary>
    internal static IDisposable BeginFrame(CorporateMotion motion, string scope, float opacity = 1f, bool interactive = true)
        => new FrameScope(motion, scope, opacity, interactive);

    internal static Color Gray(float value, float alpha = 1f) => new(value, value, value, alpha);

    /// <summary>当前作用域可交互（没有作用域时视为可交互）。</summary>
    private static bool Interactive => current is null || current.Interactive;

    /// <summary>
    /// 本次事件会登记 <paramref name="rect"/> 的提示：与 <c>TooltipHandler.TipRegion</c> 的条件相同（Repaint，且鼠标在区域内或开着提示框调试）。
    /// 用来在拼接提示文本之前跳过不会显示的提示。
    /// </summary>
    internal static bool WantsTooltip(Rect rect)
        => GuiPhase.IsRepaint && (Mouse.IsOver(rect) || DebugViewSettings.drawTooltipEdges);

    private static Color Tint(Color color)
    {
        color.a *= UiColor.Current.a * (current?.Opacity ?? 1f);
        return color;
    }

    /// <summary>以当前作用域透明度填充矩形；只在 Repaint 中绘制。</summary>
    internal static void Fill(Rect rect, Color color)
    {
        if (!GuiPhase.IsRepaint || rect.width <= 0f || rect.height <= 0f)
        {
            return;
        }

        UiDraw.Fill(rect, Tint(color));
    }

    /// <summary>以当前作用域透明度绘制标签；字体、对齐与颜色在返回前恢复。</summary>
    internal static void Label(Rect rect, string? text, GameFont font = GameFont.Small,
        Color? color = null, TextAnchor anchor = TextAnchor.UpperLeft)
    {
        UiDraw.Label(rect, text, Tint(color ?? Ink), font, anchor);
    }

    /// <summary>物品图标，透明度经 Verse 的 alpha 参数传入（Widgets.ThingIcon 会覆盖 GUI 颜色，1.6 中图标因此从不淡入淡出）。</summary>
    internal static void ThingIcon(Rect rect, Thing? thing)
    {
        if (thing is null || !GuiPhase.IsRepaint)
        {
            return;
        }

        float alpha = Tint(UnityColor.White).a;
        // Widgets.ThingIcon(Thing) 绘制后把 GUI 颜色设为白色；不改值的 TextBlock 在结束时恢复进入前的颜色。
        using (new TextBlock(newFont: null, newAnchor: null, newWordWrap: null, newColor: null))
        {
            Widgets.ThingIcon(rect, thing, alpha);
        }
    }

    /// <summary>物品定义图标，透明度经 Verse 的 alpha 参数传入。</summary>
    internal static void ThingIcon(Rect rect, ThingDef? def)
    {
        if (def is null || !GuiPhase.IsRepaint)
        {
            return;
        }

        Widgets.ThingIcon(rect, def, alpha: Tint(UnityColor.White).a);
    }

    /// <summary>以当前作用域透明度拉伸绘制纹理（例如肖像）。</summary>
    internal static void Texture(Rect rect, Texture? texture)
    {
        if (texture is null)
        {
            return;
        }

        UiDraw.Texture(rect, texture, Tint(UnityColor.White));
    }

    internal static void Panel(Rect rect)
    {
        Fill(new Rect(rect.x + 2f, rect.y + 3f, rect.width, rect.height), Gray(0f, 0.20f));
        Fill(rect, Surface);
        Outline(rect, Border);
    }

    internal static void Rule(Rect rect) => Fill(rect, Border);

    private static void Outline(Rect rect, Color color, float thickness = 1f)
    {
        Fill(new Rect(rect.x, rect.y, rect.width, thickness), color);
        Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        Fill(new Rect(rect.x, rect.y, thickness, rect.height), color);
        Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    private static int Hash(int seed, string value)
    {
        unchecked
        {
            for (int i = 0; i < value.Length; i++)
            {
                seed = seed * 31 + value[i];
            }

            return seed;
        }
    }

    /// <summary>
    /// 控件键：作用域路径 + 种类 + id（+ 序号）。没有 id 时退回按矩形取键，布局变化会让动画状态重新开始。
    /// 序号让列表项不必为每个事件拼接 "prefix/" + id 字符串。
    /// </summary>
    private static int ControlKey(Rect rect, string kind, string? id, int index = NoIndex)
    {
        int key = Hash(current?.Path ?? 17, kind);
        if (id is not null)
        {
            key = Hash(key, id);
            return index == NoIndex ? key : unchecked(key * 31 + index);
        }

        unchecked
        {
            key = key * 31 + UnityMathf.RoundToInt(rect.x * 2f);
            key = key * 31 + UnityMathf.RoundToInt(rect.y * 2f);
            key = key * 31 + UnityMathf.RoundToInt(rect.width * 2f);
            return key * 31 + UnityMathf.RoundToInt(rect.height * 2f);
        }
    }
}
