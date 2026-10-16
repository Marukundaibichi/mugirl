namespace Mugirl.Features.Corporate;

/// <summary>介绍章节页：章节完成说明与后续预告。任务页的“主线”页签也绘制本页。</summary>
internal sealed class CorporateIntroductionPage(Dialog_CorporateTerminal terminal) : CorporateTerminalPage(terminal)
{
    private UVector2 scroll;
    private bool? independentContact;
    private string body = string.Empty;
    private string title = string.Empty;
    private string status = string.Empty;
    private float measuredWidth = -1f;
    private float bodyHeight;

    internal override void Draw(Rect rect, CorporateNetwork network) => Draw(rect);

    internal void Draw(Rect rect)
    {
        bool independent = CorporateIntroduction.Current?.IndependentContact == true;
        if (independentContact != independent)
        {
            independentContact = independent;
            body = (independent ? "Mugirl.CorporateIntro.Independent.ChapterCompleted" : "Mugirl.CorporateIntro.ChapterCompleted").Translate()
                + "\n\n" + "Mugirl.CorporateIntro.ComingSoon".Translate();
            title = "Mugirl.CorporateIntro.Title".Translate();
            status = "Mugirl.CorporateIntro.CompletedStatus".Translate();
            measuredWidth = -1f;
        }

        float width = rect.width - 18f;
        if (measuredWidth != width)
        {
            measuredWidth = width;
            bodyHeight = Text.CalcHeight(body, width);
        }

        Rect view = new(0f, 0f, width, UnityMathf.Max(rect.height, bodyHeight + 140f));
        CorporateUI.BeginScrollView(rect, ref scroll, view, "introduction");
        try
        {
            float y = 0f;
            CorporateUI.Heading(ref y, view.width, title, status);
            CorporateUI.Label(new Rect(0f, y, view.width, view.height - y), body);
        }
        finally
        {
            CorporateUI.EndScrollView();
        }
    }
}
