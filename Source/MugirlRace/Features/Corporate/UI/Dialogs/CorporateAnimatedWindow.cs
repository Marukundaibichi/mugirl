using Mugirl.Platform.Ui;

namespace Mugirl.Features.Corporate;

/// <summary>
/// 巨企界面的模态窗口基类：打开时淡入上移，关闭时先播放淡出，结束后才真正移出窗口栈并执行排队的动作。
/// </summary>
/// <remarks>
/// WindowStack 在移除窗口前询问 <see cref="OnCloseRequest"/>。淡出期间窗口留在栈中，焦点与暂停不会漏到下层；
/// 动作在 <see cref="PostClose"/>（真正移除之后）执行，重复点击、Esc 或点击窗外都不会让动作执行两次或被替换。
/// </remarks>
public abstract class CorporateAnimatedWindow : Window
{
    private const float OpenDuration = 0.32f;
    private const float CloseDuration = 0.22f;
    internal readonly CorporateMotion motion = new();
    private double openedAt = CorporateClock.Never;
    private double closedAt;
    private float closingVisibility;
    private bool closing;
    private bool committingClose;
    private bool closeSound = true;
    private Action? afterClose;

    protected CorporateAnimatedWindow()
    {
        forcePause = true;
        absorbInputAroundWindow = true;
        preventCameraMotion = true;
        closeOnAccept = false;
        closeOnCancel = true;
        doCloseX = false;
        doCloseButton = false;
        doWindowBackground = false;
        drawShadow = false;
    }

    internal bool IsClosing => closing;

    protected bool Closing => closing;

    /// <summary>窗口内容可响应输入：未在关闭且淡入已超过 12%。</summary>
    protected bool Interactive => !closing && Visibility > 0.12f;

    /// <summary>窗口当前的可见度（0–1），打开时三次缓出，关闭时从关闭瞬间的值平滑降到 0。</summary>
    protected internal float Visibility
    {
        get
        {
            if (closing)
            {
                return closingVisibility * (1f - CorporateEasing.SmoothStep01(CorporateClock.Since(closedAt) / CloseDuration));
            }

            return CorporateEasing.EaseOutCubic(CorporateClock.Since(openedAt) / OpenDuration);
        }
    }

    protected override float Margin => 0f;

    public override void PreOpen()
    {
        base.PreOpen();
        openedAt = CorporateClock.Now;
        closing = false;
        committingClose = false;
        afterClose = null;
    }

    /// <summary>开始淡出；淡出结束后在 <see cref="WindowUpdate"/> 中真正关闭。</summary>
    public override void Close(bool doCloseSound = true)
    {
        if (closing)
        {
            return;
        }

        motion.ReleaseInput();
        closingVisibility = Visibility;
        closedAt = CorporateClock.Now;
        closeSound = doCloseSound;
        closing = true;
    }

    public override bool OnCloseRequest()
    {
        if (committingClose)
        {
            return true;
        }

        Close();
        return false;
    }

    public override void WindowUpdate()
    {
        base.WindowUpdate();
        if (!closing || committingClose || CorporateClock.Since(closedAt) < CloseDuration)
        {
            return;
        }

        committingClose = true;
        base.Close(closeSound);
    }

    /// <summary>登记关闭后要执行的动作并开始关闭；已在关闭时忽略（动作不会被替换或执行两次）。</summary>
    protected internal void CloseThen(Action action)
    {
        if (closing)
        {
            return;
        }

        afterClose = action;
        Close();
    }

    public override void PostClose()
    {
        motion.ReleaseInput();
        base.PostClose();
        Action? action = afterClose;
        afterClose = null;
        if (action is null)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception exception)
        {
            MugirlLog.WarningOnce("Corporate.DialogAction." + GetType().Name, "Corporate dialog action failed: " + exception);
        }
    }

    public sealed override void DoWindowContents(Rect inRect)
    {
        float visibility = Visibility;
        // 只保留外层 GUI 颜色的透明度：CorporateUI 自己把窗口可见度乘进各元素，嵌套窗口不会重复相乘。
        // TextBlock 结束时恢复进入前的字体、对齐、换行与 GUI 颜色。
        Color incoming = UiColor.Current;
        using (new TextBlock(GameFont.Small, TextAnchor.UpperLeft, true, new Color(1f, 1f, 1f, incoming.a)))
        {
            Rect shifted = new(inRect.x, inRect.y + (1f - visibility) * 14f, inRect.width, inRect.height);
            Widgets.BeginGroup(shifted);
            try
            {
                using (CorporateUI.BeginFrame(motion, "window", visibility, Interactive))
                {
                    DrawContents(new Rect(0f, 0f, shifted.width, shifted.height));
                }
            }
            finally
            {
                Widgets.EndGroup();
            }
        }
    }

    protected abstract void DrawContents(Rect inRect);

    /// <summary>对话框的公共外框：底板、右上角装饰、标题区与分隔线。</summary>
    protected static void DrawDialogFrame(Rect rect, string title, string subtitle)
    {
        CorporateUI.Fill(rect, CorporateUI.Background);
        CorporateUI.Panel(rect.ContractedBy(1f));
        CorporateUI.Fill(new Rect(1f, 1f, rect.width - 2f, 84f), CorporateUI.Background);
        CorporateUI.DrawGeometry(new Rect(rect.xMax - 144f, 4f, 140f, 77f), 0.32f);
        CorporateUI.Fill(new Rect(22f, 24f, 3f, 40f), CorporateUI.Accent);
        CorporateUI.Label(new Rect(37f, 17f, rect.width - 70f, 23f), subtitle, GameFont.Tiny, CorporateUI.Muted);
        CorporateUI.Label(new Rect(36f, 39f, rect.width - 70f, 34f), title, GameFont.Medium);
        CorporateUI.Rule(new Rect(22f, 85f, rect.width - 44f, 1f));
    }

    /// <summary>小号字体、自动换行时 <paramref name="text"/> 的高度（宽度至少 40）。</summary>
    protected static float MeasureText(string? text, float width)
    {
        using (new TextBlock(GameFont.Small, null, true))
        {
            return Text.CalcHeight(text ?? string.Empty, UnityMathf.Max(40f, width));
        }
    }
}
