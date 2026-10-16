using System.Globalization;

namespace Mugirl.Features.Corporate;

/// <summary>
/// 巨企界面的选择对话框：把一组 <see cref="FloatMenuOption"/> 显示为可滚动的编号按钮，选中后关闭窗口再执行动作。
/// 只使用选项的标签、可用状态与动作。
/// </summary>
internal sealed class Dialog_CorporateChoice : CorporateAnimatedWindow
{
    private readonly List<FloatMenuOption> options;
    private readonly string[] numbers;
    private readonly float[] heights;
    private readonly string title;
    private readonly string subtitle;
    private readonly string cancelLabel;
    private readonly string emptyLabel;
    private UVector2 scroll;
    private float measuredWidth = -1f;
    private float contentHeight;

    internal Dialog_CorporateChoice(List<FloatMenuOption>? options)
    {
        this.options = options is null ? [] : options.FindAll(static option => option is not null);
        numbers = new string[this.options.Count];
        heights = new float[this.options.Count];
        for (int i = 0; i < numbers.Length; i++)
        {
            numbers[i] = (i + 1).ToString("00", CultureInfo.CurrentCulture);
        }

        closeOnClickedOutside = true;
        title = "Mugirl.CorporateUI.Dialog.ChooseTitle".Translate();
        subtitle = "Mugirl.CorporateUI.Dialog.AvailableOptions".Translate();
        cancelLabel = "Cancel".Translate();
        emptyLabel = "Mugirl.CorporateUI.Dialog.NoOptions".Translate();
    }

    public override UVector2 InitialSize
    {
        get
        {
            float width = UnityMathf.Min(540f, UI.screenWidth - 32f);
            float total = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                total += OptionHeight(options[i], width - 68f) + 8f;
            }

            return new UVector2(width, UnityMathf.Min(UnityMathf.Clamp(total + 188f, 285f, 620f), UI.screenHeight - 32f));
        }
    }

    private static float OptionHeight(FloatMenuOption option, float width)
        => UnityMathf.Max(46f, MeasureText(option.Label, width - 56f) + 20f);

    protected override void DrawContents(Rect inRect)
    {
        DrawDialogFrame(inRect, title, subtitle);
        Rect body = new(24f, 102f, inRect.width - 48f, UnityMathf.Max(30f, inRect.height - 180f));
        float width = body.width - 20f;
        if (measuredWidth != width)
        {
            measuredWidth = width;
            contentHeight = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                heights[i] = OptionHeight(options[i], width);
                contentHeight += heights[i] + 8f;
            }
        }

        Rect view = new(0f, 0f, width, UnityMathf.Max(body.height, contentHeight));
        CorporateUI.BeginScrollView(body, ref scroll, view);
        try
        {
            float y = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                FloatMenuOption option = options[i];
                Rect row = new(0f, y, width, heights[i]);
                bool available = !option.Disabled && option.action is not null;
                if (CorporateUI.Button(row, string.Empty, available, id: "option", index: i) && option.action is not null)
                {
                    CloseThen(option.action);
                }

                CorporateUI.Label(new Rect(row.x + 12f, row.y, 28f, row.height), numbers[i],
                    GameFont.Tiny, CorporateUI.Muted, TextAnchor.MiddleLeft);
                CorporateUI.Label(new Rect(row.x + 44f, row.y + 7f, row.width - 56f, row.height - 14f), option.Label,
                    color: available ? CorporateUI.Ink : CorporateUI.Muted, anchor: TextAnchor.MiddleLeft);
                y = row.yMax + 8f;
            }

            if (options.Count == 0)
            {
                CorporateUI.Label(new Rect(0f, 8f, width, 50f), emptyLabel, color: CorporateUI.Muted);
            }
        }
        finally
        {
            CorporateUI.EndScrollView();
        }

        CorporateUI.Rule(new Rect(24f, inRect.height - 69f, inRect.width - 48f, 1f));
        if (CorporateUI.Button(new Rect(inRect.width - 174f, inRect.height - 54f, 150f, 34f), cancelLabel, id: "cancel"))
        {
            Close();
        }
    }
}
