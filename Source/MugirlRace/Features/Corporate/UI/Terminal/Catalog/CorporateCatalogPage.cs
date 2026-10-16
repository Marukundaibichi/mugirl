namespace Mugirl.Features.Corporate;

/// <summary>
/// 交易页与订购页共用的目录界面：搜索、分类、排序与视图切换工具栏，商品卡片，详情面板外框与文字截断。
/// 两个页面的筛选状态各自独立。
/// </summary>
internal abstract class CorporateCatalogPage : CorporateTerminalPage
{
    private const float ResultsFadeDuration = 0.18f;

    private readonly string searchId;
    private readonly string categoryId;
    private readonly string sortId;
    private readonly string gridId;
    private readonly string listId;
    private readonly string resetId;
    private readonly string detailScope;
    private double resultsStarted = CorporateClock.Never;
    private string[]? categoryLabels;
    private string[]? sortLabels;

    /// <summary>列表滚动位置（字段，以便按引用传给滚动视图）。</summary>
    protected UVector2 listScroll;

    protected CorporateCatalogPage(Dialog_CorporateTerminal terminal, string scopeName)
        : base(terminal)
    {
        searchId = scopeName + "/search";
        categoryId = scopeName + "/category";
        sortId = scopeName + "/sort";
        gridId = scopeName + "/grid";
        listId = scopeName + "/list";
        resetId = scopeName + "/reset";
        detailScope = scopeName + "/detail";
    }

    protected string Search { get; private set; } = string.Empty;

    /// <summary>分类序号（<see cref="CorporateBrowserText.CategoryKeys"/> 的下标，0 为全部）。</summary>
    protected int Category { get; private set; }

    /// <summary>排序方式：0 名称、1 价格升序、2 价格降序。</summary>
    protected int Sort { get; private set; }

    protected bool ListView { get; private set; }

    /// <summary>选中商品的切换动画；详情面板按它淡出淡入。</summary>
    protected CorporateCatalogTransition Selection { get; } = new();

    /// <summary>筛选结果的淡入不透明度：条件改变后从 0.32 在 0.18 秒内恢复。</summary>
    protected float ResultOpacity
        => UnityMathf.Lerp(0.32f, 1f, CorporateEasing.EaseOutCubic(CorporateClock.Since(resultsStarted) / ResultsFadeDuration));

    /// <summary>搜索框；交易页用自绘输入框，订购页用原版搜索控件。</summary>
    protected abstract string DrawSearchField(Rect rect, string current, string placeholder);

    /// <summary>详情面板内容（局部坐标）。</summary>
    protected abstract void DrawDetails(Rect rect, CorporateNetwork network);

    /// <summary>筛选条件或视图改变：回到列表顶部、播放结果淡入并取消待定的选择。</summary>
    protected virtual void OnCatalogChanged()
    {
        listScroll = default;
        resultsStarted = CorporateClock.Now;
        Selection.Clear();
    }

    /// <summary>切换买卖模式时：回到列表顶部、分类回到全部并取消选择（保留搜索词）。</summary>
    protected void ResetView()
    {
        listScroll = default;
        Category = 0;
        Selection.Clear();
    }

    protected void DrawToolbar(Rect rect)
    {
        float categoryWidth = UnityMathf.Clamp(rect.width * 0.23f, 102f, 155f);
        float sortWidth = UnityMathf.Clamp(rect.width * 0.23f, 100f, 147f);
        float searchWidth = rect.width - categoryWidth - sortWidth - 92f;
        if (searchWidth < 76f)
        {
            float reduction = (76f - searchWidth) * 0.5f;
            categoryWidth = UnityMathf.Max(64f, categoryWidth - reduction);
            sortWidth = UnityMathf.Max(64f, sortWidth - reduction);
            searchWidth = UnityMathf.Max(40f, rect.width - categoryWidth - sortWidth - 92f);
        }

        Rect search = new(rect.x, rect.y, searchWidth, rect.height);
        string newSearch = DrawSearchField(search, Search, "Mugirl.Corporate.SearchGoods".Translate());
        if (newSearch != Search)
        {
            Search = newSearch;
            OnCatalogChanged();
        }

        categoryLabels ??= MenuLabels(CorporateBrowserText.CategoryKeys);
        sortLabels ??= MenuLabels(CorporateBrowserText.SortKeys);
        Rect select = new(search.xMax + 8f, rect.y, categoryWidth, rect.height);
        if (CorporateUI.Button(select, categoryLabels[Category], id: categoryId))
        {
            Terminal.ShowMenu(MenuOptions(CorporateBrowserText.CategoryKeys, index =>
            {
                Category = index;
                OnCatalogChanged();
            }));
        }

        Rect sortRect = new(select.xMax + 8f, rect.y, sortWidth, rect.height);
        if (CorporateUI.Button(sortRect, sortLabels[Sort], id: sortId))
        {
            Terminal.ShowMenu(MenuOptions(CorporateBrowserText.SortKeys, index =>
            {
                Sort = index;
                OnCatalogChanged();
            }));
        }

        for (int i = 0; i < 2; i++)
        {
            bool list = i == 1;
            Rect button = new(sortRect.xMax + 8f + i * 34f, rect.y, 30f, rect.height);
            bool selected = list == ListView;
            if (CorporateUI.Button(button, string.Empty, primary: selected, id: list ? listId : gridId) && !selected)
            {
                ListView = list;
                OnCatalogChanged();
            }

            DrawViewIcon(button, list, selected ? CorporateUI.Background : CorporateUI.Ink);
            TooltipHandler.TipRegionByKey(button, list ? CorporateBrowserText.ListView : CorporateBrowserText.GridView);
        }
    }

    private static string[] MenuLabels(string[] keys)
    {
        string[] labels = new string[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            labels[i] = CorporateBrowserText.Get(keys[i]) + "  ▾";
        }

        return labels;
    }

    private static List<FloatMenuOption> MenuOptions(string[] keys, Action<int> select)
    {
        List<FloatMenuOption> options = new(keys.Length);
        for (int i = 0; i < keys.Length; i++)
        {
            int index = i;
            options.Add(new FloatMenuOption(CorporateBrowserText.Get(keys[i]), () => select(index)));
        }

        return options;
    }

    private static void DrawViewIcon(Rect button, bool list, Color ink)
    {
        float x = button.center.x - 6f;
        float y = button.center.y - 6f;
        if (list)
        {
            for (int row = 0; row < 3; row++)
            {
                CorporateUI.Fill(new Rect(x, y + row * 5f, 12f, 2f), ink);
            }

            return;
        }

        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 2; col++)
            {
                CorporateUI.Fill(new Rect(x + col * 7f, y + row * 7f, 5f, 5f), ink);
            }
        }
    }

    protected void DrawSummary(Rect rect, int count, string desk)
    {
        CorporateUI.Label(new Rect(rect.x, rect.y, rect.width * 0.34f, rect.height), CorporateBrowserText.Get(CorporateBrowserText.Results, count),
            GameFont.Tiny, CorporateUI.Muted);
        Caption(new Rect(rect.x + rect.width * 0.34f, rect.y, rect.width * 0.66f, rect.height), desk, TextAnchor.UpperRight);
    }

    /// <summary>商品卡片（网格模式）或价目行（列表模式）；返回是否被点击。</summary>
    protected bool DrawItem(Rect rect, ThingDef def, Thing? thing, string label, string price, string status, bool selected, bool list, string id, int index)
    {
        bool clicked = CorporateUI.Row(rect, selected, id, index);
        if (list)
        {
            DrawIcon(new Rect(rect.x + 10f, rect.y + 10f, 48f, 48f), def, thing);
            float priceWidth = UnityMathf.Clamp(rect.width * 0.34f, 110f, 170f);
            FittedText(new Rect(rect.x + 69f, rect.y + 10f, rect.width - priceWidth - 83f, 48f), label, GameFont.Small);
            CorporateUI.Label(new Rect(rect.xMax - priceWidth - 10f, rect.y + 10f, priceWidth, 24f), price, anchor: TextAnchor.MiddleRight);
            Caption(new Rect(rect.xMax - priceWidth - 10f, rect.y + 40f, priceWidth, 24f), status, TextAnchor.MiddleRight);
        }
        else
        {
            DrawIcon(new Rect(rect.center.x - 29f, rect.y + 9f, 58f, 58f), def, thing);
            FittedText(new Rect(rect.x + 12f, rect.y + 74f, rect.width - 24f, 42f), label, GameFont.Small, TextAnchor.UpperCenter);
            CorporateUI.Label(new Rect(rect.x + 10f, rect.y + 119f, rect.width - 20f, 22f), price, anchor: TextAnchor.MiddleCenter);
            Caption(new Rect(rect.x + 10f, rect.y + 143f, rect.width - 20f, 19f), status, TextAnchor.UpperCenter);
        }

        if (CorporateUI.WantsTooltip(rect))
        {
            TooltipHandler.TipRegion(rect, label + "\n\n" + def.description);
        }

        return clicked;
    }

    protected static void DrawIcon(Rect rect, ThingDef? def, Thing? thing = null)
    {
        if (thing is not null)
        {
            CorporateUI.ThingIcon(rect, thing);
            return;
        }

        CorporateUI.ThingIcon(rect, def);
    }

    /// <summary>详情面板：左侧分隔线，内容在过渡期间淡出淡入且不响应输入。</summary>
    protected void DrawDetailFrame(Rect rect, CorporateNetwork network)
    {
        CorporateUI.Rule(new Rect(rect.x - 8f, rect.y, 1f, rect.height));
        Widgets.BeginGroup(rect.ContractedBy(12f));
        try
        {
            using (CorporateUI.BeginFrame(Terminal.Motion, detailScope, Selection.Opacity, !Selection.Active))
            {
                DrawDetails(new Rect(0f, 0f, rect.width - 24f, rect.height - 24f), network);
            }
        }
        finally
        {
            Widgets.EndGroup();
        }
    }

    protected void DrawEmpty(Rect rect)
    {
        CorporateUI.Label(new Rect(10f, 16f, rect.width - 20f, 50f), CorporateBrowserText.Get(CorporateBrowserText.NoResults),
            color: CorporateUI.Muted, anchor: TextAnchor.MiddleCenter);
        if (CorporateUI.Button(new Rect(UnityMathf.Max(0f, rect.center.x - 65f), 81f, 130f, 32f),
            CorporateBrowserText.Get(CorporateBrowserText.ResetFilters), id: resetId))
        {
            Search = string.Empty;
            Category = 0;
            OnCatalogChanged();
        }
    }

    protected static void DrawDetailEmpty(Rect rect)
    {
        CorporateUI.Label(new Rect(0f, rect.height * 0.36f, rect.width, 65f), CorporateBrowserText.Get(CorporateBrowserText.SelectProduct),
            color: CorporateUI.Muted, anchor: TextAnchor.MiddleCenter);
    }

    /// <summary>详情标题：图标、名称、物品信息卡按钮与定价分类说明。</summary>
    protected void DrawProductHeading(Rect rect, Thing thing, CorporateNetwork network, string id)
    {
        CorporateUI.ThingIcon(new Rect(0f, 0f, 65f, 65f), thing);
        FittedText(new Rect(77f, 0f, rect.width - 113f, 68f), thing.LabelCap, GameFont.Small);
        Rect info = new(rect.width - 29f, 0f, 29f, 29f);
        if (CorporateUI.Button(info, "i", id: id))
        {
            GameUtility.TryAddWindow(new Dialog_InfoCard(thing));
        }

        TooltipHandler.TipRegionByKey(info, CorporateBrowserText.ProductDetails);
        string classification = network.OrderPricingReason(thing.def);
        Rect reason = new(0f, 71f, rect.width, 20f);
        TooltipHandler.TipRegion(reason, classification);
        Caption(reason, classification);
    }

    protected void DrawMetric(Rect rect, string label, string value)
    {
        CorporateUI.Label(new Rect(rect.x, rect.y, rect.width, 20f), label, GameFont.Tiny, CorporateUI.Muted);
        FittedText(new Rect(rect.x, rect.y + 22f, rect.width, 26f), value, GameFont.Small);
    }

    protected void Caption(Rect rect, string text, TextAnchor anchor = TextAnchor.UpperLeft)
        => FittedText(rect, text, GameFont.Tiny, anchor, CorporateUI.Muted);

    /// <summary>放不下时截断为“前缀…”，截断后悬停显示全文。</summary>
    protected void FittedText(Rect rect, string? text, GameFont font, TextAnchor anchor = TextAnchor.UpperLeft, Color? color = null)
    {
        if (rect.width <= 0f || rect.height <= 0f)
        {
            return;
        }

        string full = text ?? string.Empty;
        string shown = Terminal.TextCache.FitHeight(full, rect.width, rect.height, font);
        CorporateUI.Label(rect, shown, font, color, anchor);
        if (shown != full && CorporateUI.WantsTooltip(rect))
        {
            TooltipHandler.TipRegion(rect, full);
        }
    }

    /// <summary>数量步进器（最大值按钮使用“最大”文案）。</summary>
    protected static void DrawQuantity(Rect rect, ref int quantity, ref string buffer, int maximum, string id)
        => CorporateUI.Quantity(rect, ref quantity, ref buffer, maximum, id, "Mugirl.Corporate.Maximum".Translate());

    /// <summary>
    /// 订购速度的显示名。速度只有三种，按枚举值选完整键；未定义的数值（只可能来自外部改写的存档）显示枚举数值本身，
    /// 不再像 1.6 那样拼出一个不存在的键。
    /// </summary>
    protected static string SpeedLabel(CorporateOrderSpeed speed) => speed switch
    {
        CorporateOrderSpeed.Standard => "Mugirl.CorporatePricing.Speed.Standard".Translate(),
        CorporateOrderSpeed.Express => "Mugirl.CorporatePricing.Speed.Express".Translate(),
        CorporateOrderSpeed.Priority => "Mugirl.CorporatePricing.Speed.Priority".Translate(),
        _ => speed.ToString(),
    };
}
