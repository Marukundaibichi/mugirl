namespace Mugirl.Features.Incidents;

// 运货员日记的五页阅读窗口。
public class Dialog_CourierDiary : Window
{
    private const int PageCount = 5;

    private readonly string title;
    private int currentPage;

    public Dialog_CourierDiary(string title)
    {
        this.title = title;
        forcePause = true;
        absorbInputAroundWindow = true;
        closeOnAccept = false;
        doCloseX = true;
        doCloseButton = false;
        draggable = true;
        resizeable = false;
    }

    public override UVector2 InitialSize => new UVector2(600f, 520f);

    public override void DoWindowContents(Rect inRect)
    {
        float y = 0f;

        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, y, inRect.width, 40f), title);
        y += 50f;

        Text.Font = GameFont.Small;
        Widgets.Label(new Rect(0f, y, inRect.width, 25f), "Mugirl.CourierDiary.PageCounter".Translate(currentPage + 1, PageCount));
        y += 30f;

        Rect contentRect = new Rect(0f, y, inRect.width - 20f, inRect.height - y - 60f);
        // 翻译键写成字面量，离线校验才能确认每一页在中英文语言文件中都存在。
        string pageKey = currentPage switch
        {
            0 => "Mugirl.CourierDiary.Page1",
            1 => "Mugirl.CourierDiary.Page2",
            2 => "Mugirl.CourierDiary.Page3",
            3 => "Mugirl.CourierDiary.Page4",
            _ => "Mugirl.CourierDiary.Page5",
        };
        Widgets.Label(contentRect, pageKey.Translate());

        y = inRect.height - 55f;

        if (currentPage > 0 && Widgets.ButtonText(new Rect(0f, y, 100f, 35f), "Mugirl.CourierDiary.PreviousPage".Translate()))
        {
            currentPage--;
        }

        if (currentPage < PageCount - 1 && Widgets.ButtonText(new Rect(inRect.width - 120f, y, 100f, 35f), "Mugirl.CourierDiary.NextPage".Translate()))
        {
            currentPage++;
        }

        if (Widgets.ButtonText(new Rect((inRect.width - 100f) / 2f, y, 100f, 35f), "Mugirl.CourierDiary.Close".Translate()))
        {
            Close();
        }
    }
}
