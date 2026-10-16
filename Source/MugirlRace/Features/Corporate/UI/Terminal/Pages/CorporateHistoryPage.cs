namespace Mugirl.Features.Corporate;

/// <summary>历史页：交易与合同记录，从新到旧。</summary>
internal sealed class CorporateHistoryPage(Dialog_CorporateTerminal terminal) : CorporateTerminalPage(terminal)
{
    // 布局缓存：记录本身不可变，标题与测量高度按（版本、宽度、记录集合）缓存，绘制循环再做视口裁剪，
    // 避免 200 条记录每个 GUI 事件重复 Translate 与 CalcHeight。
    private readonly List<string> titles = [];
    private readonly List<float> titleHeights = [];
    private readonly List<float> detailHeights = [];
    private UVector2 scroll;
    private int layoutVersion = -1;
    private float layoutWidth = -1f;
    private int layoutCount = -1;
    private CorporateHistoryEntry? layoutFirst;
    private CorporateHistoryEntry? layoutLast;
    private string heading = string.Empty;
    private string hint = string.Empty;
    private string empty = string.Empty;

    internal override void Draw(Rect rect, CorporateNetwork network)
    {
        if (heading.Length == 0)
        {
            heading = "Mugirl.CorporateUI.Page.History".Translate();
            hint = "Mugirl.CorporateUI.HistoryHint".Translate();
            empty = "Mugirl.CorporateUI.NoHistory".Translate();
        }

        float y = 0f;
        CorporateUI.Heading(ref y, rect.width, heading, hint);
        Rect viewport = new(0f, y, rect.width, UnityMathf.Max(1f, rect.height - y));
        float width = rect.width - 18f;
        IReadOnlyList<CorporateHistoryEntry> records = network.Records;
        EnsureLayout(records, width);

        float total = 0f;
        for (int i = 0; i < titleHeights.Count; i++)
        {
            total += titleHeights[i] + detailHeights[i] + 34f;
        }

        Rect view = new(0f, 0f, width, UnityMathf.Max(viewport.height, total));
        CorporateUI.BeginScrollView(viewport, ref scroll, view, "history");
        try
        {
            if (records.Count == 0)
            {
                CorporateUI.Notice(new Rect(0f, 0f, view.width, 76f), empty);
            }

            float top = 0f;
            float viewBottom = scroll.y + viewport.height;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                float titleHeight = titleHeights[i];
                float detailHeight = detailHeights[i];
                float cardHeight = titleHeight + detailHeight + 34f;
                if (top + cardHeight >= scroll.y && top <= viewBottom)
                {
                    CorporateUI.Panel(new Rect(0f, top, width, titleHeight + detailHeight + 24f));
                    CorporateUI.Label(new Rect(14f, top + 9f, width - 28f, titleHeight), titles[i]);
                    CorporateUI.Label(new Rect(14f, top + titleHeight + 14f, width - 28f, detailHeight), records[i].detail, color: CorporateUI.Muted);
                }

                top += cardHeight;
            }
        }
        finally
        {
            CorporateUI.EndScrollView();
        }
    }

    // 记录也会在其他窗口（例如研究对白）中追加而不经过终端的版本号；1.6 只按版本与宽度判断，记录数变多后按旧缓存取下标越界。
    // 这里同时比较条数与首尾记录（超过上限时从头部删除）。
    private void EnsureLayout(IReadOnlyList<CorporateHistoryEntry> records, float width)
    {
        int count = records.Count;
        CorporateHistoryEntry? first = count > 0 ? records[0] : null;
        CorporateHistoryEntry? last = count > 0 ? records[count - 1] : null;
        if (layoutVersion == Terminal.CacheVersion && UnityMathf.Approximately(layoutWidth, width)
            && layoutCount == count && ReferenceEquals(layoutFirst, first) && ReferenceEquals(layoutLast, last))
        {
            return;
        }

        layoutVersion = Terminal.CacheVersion;
        layoutWidth = width;
        layoutCount = count;
        layoutFirst = first;
        layoutLast = last;
        titles.Clear();
        titleHeights.Clear();
        detailHeights.Clear();
        for (int i = 0; i < count; i++)
        {
            CorporateHistoryEntry record = records[i];
            string title = RecordTitle(record);
            titles.Add(title);
            titleHeights.Add(UnityMathf.Max(28f, Text.CalcHeight(title, width - 28f)));
            detailHeights.Add(UnityMathf.Max(25f, Text.CalcHeight(record.detail ?? string.Empty, width - 28f)));
        }
    }

    private static string RecordTitle(CorporateHistoryEntry record)
    {
        // 记录键保存在存档中，旧存档的历史文本依赖这些 Keyed 键名。
        return "Mugirl.CorporateUI.RecordTitle".Translate((record.tick / 60000f).ToString("0.0"),
            (record.key ?? string.Empty).Translate(), CorporateUI.Money(record.amount));
    }
}
