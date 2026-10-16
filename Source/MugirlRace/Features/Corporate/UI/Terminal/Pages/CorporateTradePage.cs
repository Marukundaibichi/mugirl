using System.Linq;

namespace Mugirl.Features.Corporate;

/// <summary>交易页：每周货架（向巨企购买）与产品收购（向巨企出售）两个页签，共用目录工具栏、商品卡片与详情面板。</summary>
internal sealed class CorporateTradePage : CorporateCatalogPage
{
    private const float HeaderHeight = 116f;

    // 筛选排序结果：搜索词、分类、排序、买卖模式或缓存版本改变时重建，替代每个 GUI 事件对全部候选计算报价与排序。
    private readonly List<TradeRow> rows = [];
    private readonly HashSet<Thing> rowThings = [];
    private readonly HashSet<CorporateStockOffer> rowOffers = [];
    private UVector2 pageScroll;
    private bool selling;
    private int viewVersion = -1;
    private bool viewSelling;
    private string viewSearch = string.Empty;
    private int viewCategory = -1;
    private int viewSort = -1;
    private CorporateStockOffer? selectedStock;
    private Thing? selectedProduct;
    private int quantity = 1;
    private string quantityText = "1";

    internal CorporateTradePage(Dialog_CorporateTerminal terminal)
        : base(terminal, "trade")
    {
    }

    // 一行商品：物品、（每周货架时的）货位，以及只为可见行按需计算的单价与文字。
    private sealed class TradeRow(Thing thing, CorporateStockOffer? offer)
    {
        internal readonly Thing Thing = thing;
        internal readonly CorporateStockOffer? Offer = offer;
        internal bool HasPrice;
        internal int Price;
        internal string? PriceText;
        internal string? StatusText;
    }

    protected override string DrawSearchField(Rect rect, string current, string placeholder)
        => CorporateUI.Input(rect, current, placeholder, "trade/search");

    internal override void Draw(Rect rect, CorporateNetwork network)
    {
        float minimumHeight = rect.width - 18f >= 720f ? 480f : 700f;
        if (rect.height >= minimumHeight)
        {
            DrawBody(rect, network);
            return;
        }

        Rect view = new(0f, 0f, rect.width - 18f, minimumHeight);
        CorporateUI.BeginScrollView(rect, ref pageScroll, view, "trade/page-scroll");
        try
        {
            DrawBody(view, network);
        }
        finally
        {
            CorporateUI.EndScrollView();
        }
    }

    private void DrawBody(Rect rect, CorporateNetwork network)
    {
        Selection.Advance();
        float w = rect.width;
        if (CorporateUI.Button(new Rect(0f, 0f, w / 2f - 4f, 34f), "Mugirl.Corporate.WeeklyStock".Translate(), primary: !selling, id: "trade/buy") && selling)
        {
            Terminal.RequestContentTransition(() => SelectMode(false));
        }

        Rect productTab = new(w / 2f + 4f, 0f, w / 2f - 4f, 34f);
        if (CorporateUI.Button(productTab, "Mugirl.Corporate.ProductPurchase".Translate(), primary: selling, id: "trade/sell") && !selling)
        {
            Terminal.RequestContentTransition(() => SelectMode(true));
        }

        TooltipHandler.TipRegionByKey(productTab, "Mugirl.Corporate.ProductHint");
        DrawToolbar(new Rect(0f, 46f, w, 32f));
        EnsureRows(network);
        int count = rows.Count;
        DrawSummary(new Rect(0f, 83f, w, 24f), count, selling
            ? CorporateBrowserText.Get(CorporateBrowserText.PurchaseDesk)
            : CorporateBrowserText.Get(CorporateBrowserText.RefreshIn, UnityMathf.Max(0f, (network.NextRefreshTick - CorporateNetwork.Now) / 60000f).ToString("0.0")));
        CorporateLayout.CatalogSplit(new Rect(0f, HeaderHeight, w, rect.height - HeaderHeight), false, out Rect listing, out Rect detail);
        bool listMode = ListView || listing.width < 360f;
        CorporateLayout.CatalogGrid(listing, count, listMode, out int columns, out float cardWidth, out float stride, out Rect view);
        using (CorporateUI.BeginFrame(Terminal.Motion, "trade/results", ResultOpacity))
        {
            CorporateUI.BeginScrollView(listing, ref listScroll, view, "trade/item-scroll");
            try
            {
                // 只遍历可能可见的行（上下各多一行），再按卡片边界裁剪，绘制集合与逐项检查完全相同。
                CorporateLayout.VisibleRange(count, columns, stride, listScroll.y, listing.height, out int first, out int last);
                for (int i = first; i < last; i++)
                {
                    Rect card = new(i % columns * (cardWidth + 10f), i / columns * stride, cardWidth, stride - 8f);
                    if (card.yMax < listScroll.y || card.y > listScroll.y + listing.height)
                    {
                        continue;
                    }

                    DrawRow(card, rows[i], listMode, network);
                }

                if (count == 0)
                {
                    DrawEmpty(view);
                }
            }
            finally
            {
                CorporateUI.EndScrollView();
            }
        }

        DrawDetailFrame(detail, network);
    }

    private void DrawRow(Rect card, TradeRow row, bool listMode, CorporateNetwork network)
    {
        Thing thing = row.Thing;
        CorporateStockOffer? offer = row.Offer;
        if (!row.HasPrice)
        {
            row.Price = UnitPrice(network, row);
            row.HasPrice = true;
        }

        row.PriceText ??= CorporateUI.Money(row.Price);
        if (row.StatusText is null)
        {
            int available = offer?.count ?? thing.stackCount;
            row.StatusText = CorporateBrowserText.Get(available > 0 ? CorporateBrowserText.Available : CorporateBrowserText.SoldOut, available);
        }

        bool selected = offer is null ? thing == selectedProduct : offer == selectedStock;
        if (DrawItem(card, thing.def, thing, thing.LabelCap, row.PriceText, row.StatusText, selected, listMode, "trade/item", thing.thingIDNumber) && !selected)
        {
            Selection.Request(() =>
            {
                if (selling)
                {
                    selectedProduct = thing;
                }
                else
                {
                    selectedStock = offer;
                }

                quantity = 1;
                quantityText = "1";
            });
            UI.UnfocusCurrentControl();
        }
    }

    private int UnitPrice(CorporateNetwork network, TradeRow row)
        => row.Offer is null ? network.ProductQuote(row.Thing, 1) : network.StockUnitPrice(row.Offer);

    private void SelectMode(bool sell)
    {
        selling = sell;
        ResetView();
        selectedProduct = null;
        selectedStock = null;
    }

    private void EnsureRows(CorporateNetwork network)
    {
        if (viewVersion == Terminal.CacheVersion && viewSelling == selling && viewSearch == Search
            && viewCategory == Category && viewSort == Sort)
        {
            return;
        }

        viewVersion = Terminal.CacheVersion;
        viewSelling = selling;
        viewSearch = Search;
        viewCategory = Category;
        viewSort = Sort;
        List<TradeRow> matched = [];
        if (selling)
        {
            foreach (Thing thing in Context.AvailableContractThings)
            {
                if (network.ProductFactor(thing) > 0f && Matches(network, thing.def, thing.Label))
                {
                    matched.Add(new TradeRow(thing, null));
                }
            }
        }
        else
        {
            foreach (CorporateStockOffer stock in network.Stock)
            {
                Thing? sample = stock.sample;
                if (sample is not null && !sample.Destroyed && network.CanAutomaticallySupply(sample.def) && Matches(network, sample.def, sample.Label))
                {
                    matched.Add(new TradeRow(sample, stock));
                }
            }
        }

        rows.Clear();
        rows.AddRange(SortRows(matched, network));
        rowThings.Clear();
        rowOffers.Clear();
        foreach (TradeRow row in rows)
        {
            rowThings.Add(row.Thing);
            if (row.Offer is not null)
            {
                rowOffers.Add(row.Offer);
            }
        }
    }

    // 与 1.6 相同的稳定排序：按名称（当前区域性），或先按单价再按名称。
    private IEnumerable<TradeRow> SortRows(List<TradeRow> matched, CorporateNetwork network)
    {
        if (Sort == 1 || Sort == 2)
        {
            foreach (TradeRow row in matched)
            {
                row.Price = UnitPrice(network, row);
                row.HasPrice = true;
            }

            IOrderedEnumerable<TradeRow> byPrice = Sort == 1
                ? matched.OrderBy(static row => row.Price)
                : matched.OrderByDescending(static row => row.Price);
            return byPrice.ThenBy(static row => row.Thing.Label, StringComparer.CurrentCulture);
        }

        return matched.OrderBy(static row => row.Thing.Label, StringComparer.CurrentCulture);
    }

    private bool Matches(CorporateNetwork network, ThingDef def, string label)
    {
        if (!string.IsNullOrWhiteSpace(Search) && !CorporateSearchMatcher.Contains(label, Search.Trim()))
        {
            return false;
        }

        return Category switch
        {
            1 => def.IsStuff,
            2 => def.IsNutritionGivingIngestible && !def.IsCorpse,
            3 => def.IsMedicine,
            4 => def.IsWeapon,
            5 => def.IsApparel,
            6 => network.IsRareOrder(def) || network.IsSpecialOrder(def),
            7 => !def.IsStuff && !def.IsNutritionGivingIngestible && !def.IsMedicine && !def.IsWeapon && !def.IsApparel,
            _ => true,
        };
    }

    protected override void DrawDetails(Rect rect, CorporateNetwork network)
    {
        CorporateStockOffer? stock = selling ? null : selectedStock;
        Thing? selectedThing = selling ? selectedProduct : stock?.sample;
        bool valid = selectedThing is not null && !selectedThing.Destroyed
            && (selling ? rowThings.Contains(selectedThing) : stock is not null && rowOffers.Contains(stock));
        if (!valid || selectedThing is null)
        {
            DrawDetailEmpty(rect);
            return;
        }

        float w = rect.width;
        int silver = Terminal.SilverCount;
        int stocked = stock?.count ?? selectedThing.stackCount;
        int price = stock is null ? network.ProductQuote(selectedThing, 1) : network.StockUnitPrice(stock);
        int available = stock is null ? stocked : network.AffordableStockQuantity(stock, silver);
        quantity = UnityMathf.Clamp(quantity, 1, Math.Max(1, available));
        DrawProductHeading(rect, selectedThing, network, "trade/info");
        DrawMetric(new Rect(0f, 94f, w * 0.58f, 51f),
            CorporateBrowserText.Get(selling ? CorporateBrowserText.PurchasePrice : CorporateBrowserText.UnitPrice), CorporateUI.Money(price));
        DrawMetric(new Rect(w * 0.58f + 8f, 94f, w * 0.42f - 8f, 51f), CorporateBrowserText.Get(CorporateBrowserText.Stock), stocked.ToString("N0"));
        float quantityY = rect.height - 161f;
        CorporateUI.Label(new Rect(0f, quantityY - 23f, w, 21f), CorporateBrowserText.Get(CorporateBrowserText.Quantity), GameFont.Tiny, CorporateUI.Muted);
        DrawQuantity(new Rect(0f, quantityY, w, 32f), ref quantity, ref quantityText, available, "trade/quantity");
        int total = stock is null ? network.ProductQuote(selectedThing, quantity) : network.StockQuote(stock, quantity);
        CorporateUI.Label(new Rect(0f, quantityY + 39f, w * 0.25f, 28f), CorporateBrowserText.Get(CorporateBrowserText.Total), GameFont.Tiny, CorporateUI.Muted, TextAnchor.MiddleLeft);
        CorporateUI.Label(new Rect(w * 0.25f, quantityY + 39f, w * 0.75f, 28f), CorporateUI.Money(total), anchor: TextAnchor.MiddleRight);
        bool permitted = network.CanTrade(Context, out string? permissionReason);
        if (CorporateUI.Button(new Rect(0f, quantityY + 74f, w, 36f),
            (selling ? "Mugirl.Corporate.ConfirmSale" : "Mugirl.Corporate.ConfirmPurchase").Translate(),
            permitted && available > 0 && total > 0, true, "trade/confirm"))
        {
            string? reason;
            bool ok = stock is null
                ? network.SellProduct(Context, selectedThing, quantity, out reason)
                : network.BuyStock(Context, stock, quantity, out reason);
            Terminal.ShowTradeResult(ok, reason);
        }

        string status = permissionReason ?? (stock is not null && available == 0
            ? CorporateBrowserText.Get(stocked == 0 ? CorporateBrowserText.SoldOut : CorporateBrowserText.InsufficientFunds)
            : Context.Caravan != null
                ? CorporateBrowserText.Get(CorporateBrowserText.Mass, Context.IncomingMass(selectedThing, quantity).ToString("0.0"))
                : CorporateBrowserText.Get(CorporateBrowserText.DeliveryTo, Context.Label));
        Caption(new Rect(0f, quantityY + 117f, w, 38f), status);
    }

    internal override void PostClose()
    {
        Selection.Clear();
        rows.Clear();
        rowThings.Clear();
        rowOffers.Clear();
    }
}
