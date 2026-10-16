namespace Mugirl.Features.Corporate;

/// <summary>巨企界面的确认对话框：可滚动的说明文字、取消与确认；确认动作在窗口真正关闭后执行。</summary>
internal sealed class Dialog_CorporateConfirm : CorporateAnimatedWindow
{
    private readonly string text;
    private readonly Action? confirmed;
    private readonly bool destructive;
    private readonly string title;
    private readonly string subtitle;
    private readonly string cancelLabel;
    private readonly string confirmLabel;
    private UVector2 scroll;
    private float measuredWidth = -1f;
    private float measuredHeight;

    internal Dialog_CorporateConfirm(string? text, Action? confirmed, bool destructive = false)
    {
        this.text = text ?? string.Empty;
        this.confirmed = confirmed;
        this.destructive = destructive;
        closeOnClickedOutside = false;
        title = "Mugirl.CorporateUI.Dialog.ConfirmTitle".Translate();
        subtitle = (destructive ? "Mugirl.CorporateUI.Dialog.ReviewRequired" : "Mugirl.CorporateUI.Dialog.Authorization").Translate();
        cancelLabel = "Cancel".Translate();
        confirmLabel = "Confirm".Translate();
    }

    public override UVector2 InitialSize
    {
        get
        {
            float width = UnityMathf.Min(590f, UI.screenWidth - 32f);
            float height = UnityMathf.Clamp(MeasureText(text, width - 76f) + 213f, 325f, 590f);
            return new UVector2(width, UnityMathf.Min(height, UI.screenHeight - 32f));
        }
    }

    protected override void DrawContents(Rect inRect)
    {
        DrawDialogFrame(inRect, title, subtitle);
        Rect body = new(24f, 105f, inRect.width - 48f, UnityMathf.Max(30f, inRect.height - 197f));
        float textWidth = body.width - 20f;
        if (measuredWidth != textWidth)
        {
            measuredWidth = textWidth;
            measuredHeight = MeasureText(text, textWidth);
        }

        Rect view = new(0f, 0f, textWidth, UnityMathf.Max(body.height, measuredHeight + 6f));
        CorporateUI.BeginScrollView(body, ref scroll, view);
        try
        {
            CorporateUI.Label(new Rect(0f, 0f, view.width, view.height), text);
        }
        finally
        {
            CorporateUI.EndScrollView();
        }

        CorporateUI.Rule(new Rect(24f, inRect.height - 74f, inRect.width - 48f, 1f));
        float buttonWidth = (inRect.width - 60f) * 0.5f;
        Rect cancel = new(24f, inRect.height - 57f, buttonWidth, 36f);
        Rect accept = new(cancel.xMax + 12f, cancel.y, buttonWidth, 36f);
        if (CorporateUI.Button(cancel, cancelLabel, id: "cancel"))
        {
            Close();
        }

        if (CorporateUI.Button(accept, confirmLabel, enabled: confirmed is not null, primary: true, id: "confirm") && confirmed is not null)
        {
            CloseThen(confirmed);
        }
    }
}
