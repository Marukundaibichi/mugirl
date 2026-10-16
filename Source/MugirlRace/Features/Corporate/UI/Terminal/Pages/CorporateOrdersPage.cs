using System.Linq;

namespace Mugirl.Features.Corporate;

/// <summary>订购页：供应链目录（搜索、分类、排序与分时建立的索引）、定制订单详情与“我的订单”回执。</summary>
internal sealed class CorporateOrdersPage : CorporateCatalogPage
{
    private const float HeaderHeight = 116f;
    private const int MaxReceipts = 100;
    private const float ReceiptStride = 126f;
    private const float ReceiptHeight = 118f;

    private readonly CorporateOrderCatalog catalog = new();
    private readonly QuickSearchWidget searchWidget = new() { maxSearchTextLength = int.MaxValue };
    // 卡片上的备货说明按定义缓存（文本只取决于定义与服务数据），缓存版本变化时清空。
    private readonly Dictionary<ThingDef, string> leadLabels = [];
    private readonly List<CorporateOrder> receipts = [];
    private UVector2 pageScroll;
    private bool showReceipts;
    private Thing? preview;
    private ThingDef? orderDef;
    private ThingDef? orderStuff;
    private QualityCategory orderQuality = QualityCategory.Normal;
    private CorporateOrderSpeed orderSpeed = CorporateOrderSpeed.Standard;
    private int quantity = 1;
    private string quantityText = "1";
    private int labelsVersion = -1;
    private int receiptsVersion = -1;
    private int receiptsCount = -1;
    private string unavailable = string.Empty;
    private string quotePending = string.Empty;
    private string restrictedShort = string.Empty;

    internal CorporateOrdersPage(Dialog_CorporateTerminal terminal)
        : base(terminal, "orders")
    {
    }

    /// <summary>窗口每帧调用：页面正在显示或即将显示目录时推进索引与筛选。</summary>
    internal void UpdateCatalog(CorporateNetwork network, bool visible)
    {
        if (!visible || showReceipts)
        {
            return;
        }

        catalog.Update(network, Search, Category, Sort);
    }

    protected override void OnCatalogChanged()
    {
        base.OnCatalogChanged();
        catalog.RequestRefresh();
    }

    protected override string DrawSearchField(Rect rect, string current, string placeholder)
        => CorporateUI.Search(rect, current, searchWidget, placeholder);

    internal override void Draw(Rect rect, CorporateNetwork network)
    {
        float minimumHeight = rect.width - 18f >= 720f ? 718f : 928f;
        if (showReceipts || rect.height >= minimumHeight)
        {
            DrawHeader(rect.width);
            if (showReceipts)
            {
                DrawReceipts(new Rect(0f, 43f, rect.width, rect.height - 43f), network);
            }
            else
            {
                DrawCatalog(new Rect(0f, HeaderHeight, rect.width, rect.height - HeaderHeight), network);
            }

            return;
        }

        // 搜索栏与页签固定在滚动容器外；滚动只移动目录和订单详情，输入法组合串、候选位置和字段坐标不随滚动裁剪变化。
        DrawHeader(rect.width);
        Rect body = new(0f, HeaderHeight, rect.width, rect.height - HeaderHeight);
        Rect view = new(0f, 0f, rect.width - 18f, minimumHeight - HeaderHeight);
        CorporateUI.BeginScrollView(body, ref pageScroll, view, "orders/page-scroll");
        try
        {
            DrawCatalog(view, network);
        }
        finally
        {
            CorporateUI.EndScrollView();
        }
    }

    private void DrawHeader(float width)
    {
        Selection.Advance();
        if (CorporateUI.Button(new Rect(0f, 0f, width / 2f - 4f, 34f), "Mugirl.Corporate.OrderCatalog".Translate(), primary: !showReceipts, id: "orders/catalog") && showReceipts)
        {
            Terminal.RequestContentTransition(() => ShowReceipts(false));
        }

        if (CorporateUI.Button(new Rect(width / 2f + 4f, 0f, width / 2f - 4f, 34f), "Mugirl.Corporate.MyOrders".Translate(), primary: showReceipts, id: "orders/receipts") && !showReceipts)
        {
            Terminal.RequestContentTransition(() => ShowReceipts(true));
        }

        if (showReceipts)
        {
            return;
        }

        DrawToolbar(new Rect(0f, 46f, width, 32f));
        DrawSummary(new Rect(0f, 83f, width, 24f), catalog.View.Count,
            CorporateBrowserText.Get(catalog.Busy ? CorporateBrowserText.UpdatingCatalog : CorporateBrowserText.ProcurementDesk));
    }

    private void ShowReceipts(bool receiptsVisible)
    {
        showReceipts = receiptsVisible;
        listScroll = default;
    }

    private void DrawCatalog(Rect rect, CorporateNetwork network)
    {
        EnsureLabels();
        IReadOnlyList<CorporateOrderCatalog.Entry> entries = catalog.View;
        CorporateLayout.CatalogSplit(rect, true, out Rect listing, out Rect detail);
        bool listMode = ListView || listing.width < 360f;
        CorporateLayout.CatalogGrid(listing, entries.Count, listMode, out int columns, out float cardWidth, out float stride, out Rect view);
        using (CorporateUI.BeginFrame(Terminal.Motion, "orders/results", ResultOpacity))
        {
            CorporateUI.BeginScrollView(listing, ref listScroll, view, "orders/catalog-scroll");
            try
            {
                CorporateLayout.VisibleRange(entries.Count, columns, stride, listScroll.y, listing.height, out int firstVisible, out int lastVisible);
                catalog.SetVisibleRange(firstVisible, lastVisible);
                for (int i = firstVisible; i < lastVisible; i++)
                {
                    Rect card = new(i % columns * (cardWidth + 10f), i / columns * stride, cardWidth, stride - 8f);
                    DrawEntry(card, entries[i], listMode, network);
                }

                if (entries.Count == 0)
                {
                    if (catalog.Busy)
                    {
                        CorporateUI.Label(new Rect(10f, 16f, view.width - 20f, 65f), CorporateBrowserText.Get(CorporateBrowserText.UpdatingCatalog),
                            color: CorporateUI.Muted, anchor: TextAnchor.MiddleCenter);
                    }
                    else
                    {
                        DrawEmpty(view);
                    }
                }
            }
            finally
            {
                CorporateUI.EndScrollView();
            }
        }

        DrawDetailFrame(detail, network);
    }

    private void EnsureLabels()
    {
        if (labelsVersion == Terminal.CacheVersion)
        {
            return;
        }

        labelsVersion = Terminal.CacheVersion;
        leadLabels.Clear();
        unavailable = CorporateBrowserText.Get(CorporateBrowserText.Unavailable);
        quotePending = CorporateBrowserText.Get(CorporateBrowserText.QuotePending);
        restrictedShort = "Mugirl.CorporatePricing.RestrictedShort".Translate().ToString();
    }

    private void DrawEntry(Rect card, in CorporateOrderCatalog.Entry entry, bool listMode, CorporateNetwork network)
    {
        ThingDef def = entry.Def;
        string price = catalog.TryGetQuote(def, out int quoted) ? quoted > 0 ? CorporateUI.Money(quoted) : unavailable : quotePending;
        if (DrawItem(card, def, null, entry.Label, price, LeadLabel(entry, network), orderDef == def, listMode, "orders/item", def.index) && orderDef != def)
        {
            Selection.Request(() => SelectProduct(def));
            UI.UnfocusCurrentControl();
        }
    }

    private string LeadLabel(in CorporateOrderCatalog.Entry entry, CorporateNetwork network)
    {
        ThingDef def = entry.Def;
        if (!leadLabels.TryGetValue(def, out string? label))
        {
            IntRange lead = entry.Lead;
            label = network.CanAutomaticallySupply(def)
                ? CorporateBrowserText.Get(entry.Special ? CorporateBrowserText.SpecialLead : CorporateBrowserText.Lead, lead.min, lead.max)
                : restrictedShort;
            leadLabels.Add(def, label);
        }

        return label;
    }

    private void SelectProduct(ThingDef def)
    {
        orderDef = def;
        orderStuff = GenStuff.DefaultStuffFor(def);
        orderQuality = QualityCategory.Normal;
        orderSpeed = CorporateOrderSpeed.Standard;
        quantity = 1;
        quantityText = "1";
        RefreshPreview();
    }

    protected override void DrawDetails(Rect rect, CorporateNetwork network)
    {
        string search = Search.Trim();
        ThingDef? def = orderDef;
        if (def is not null && !network.CanAutomaticallySupply(def) && catalog.IsListed(def, search, Category))
        {
            DrawIcon(new Rect(0f, 0f, 65f, 65f), def);
            FittedText(new Rect(77f, 0f, rect.width - 77f, 68f), def.LabelCap, GameFont.Small);
            CorporateUI.Label(new Rect(0f, 90f, rect.width, 110f), "Mugirl.CorporatePricing.Restricted".Translate());
            return;
        }

        Thing? product = preview;
        if (def is null || product is null || product.Destroyed || !catalog.IsListed(def, search, Category))
        {
            DrawDetailEmpty(rect);
            return;
        }

        DrawProductDetails(rect, network, def, product);
    }

    private void DrawProductDetails(Rect rect, CorporateNetwork network, ThingDef def, Thing product)
    {
        float w = rect.width;
        DrawProductHeading(rect, product, network, "orders/info");
        IntRange ticks = network.OrderLeadTicks(def, orderSpeed);
        string minDays = (ticks.min / 60000m).ToString("0.##");
        string maxDays = (ticks.max / 60000m).ToString("0.##");
        DrawMetric(new Rect(0f, 94f, w * 0.58f, 51f), CorporateBrowserText.Get(CorporateBrowserText.UnitPrice), CorporateUI.Money(network.OrderQuote(product, 1)));
        DrawMetric(new Rect(w * 0.58f + 8f, 94f, w * 0.42f - 8f, 51f), CorporateBrowserText.Get(CorporateBrowserText.Preparation),
            CorporateBrowserText.Get(CorporateBrowserText.Days, minDays, maxDays));
        float y = 157f;
        float half = (w - 8f) / 2f;
        CorporateUI.Label(new Rect(0f, y, half, 20f), CorporateBrowserText.Get(CorporateBrowserText.Material), GameFont.Tiny, CorporateUI.Muted);
        CorporateUI.Label(new Rect(half + 8f, y, half, 20f), CorporateBrowserText.Get(CorporateBrowserText.Quality), GameFont.Tiny, CorporateUI.Muted);
        y += 23f;
        if (CorporateUI.Button(new Rect(0f, y, half, 30f), orderStuff?.LabelCap.ToString() ?? "Mugirl.Corporate.NoStuff".Translate(), def.MadeFromStuff, id: "orders/stuff"))
        {
            ShowStuffMenu(def);
        }

        if (CorporateUI.Button(new Rect(half + 8f, y, half, 30f), orderQuality.GetLabel(), product.TryGetComp<CompQuality>() is not null, id: "orders/quality"))
        {
            ShowQualityMenu();
        }

        CorporateUI.Label(new Rect(0f, 224f, w, 20f), "Mugirl.CorporatePricing.SpeedLabel".Translate(), GameFont.Tiny, CorporateUI.Muted);
        if (CorporateUI.Button(new Rect(0f, 247f, w, 30f), SpeedLabel(orderSpeed), id: "orders/speed"))
        {
            ShowSpeedMenu();
        }

        int maximum = network.MaxOrderQuantity(product);
        if (quantity > maximum)
        {
            quantity = maximum;
            quantityText = quantity.ToString();
        }

        int standardCost = network.OrderQuote(product, quantity);
        int cost = network.OrderQuote(product, quantity, orderSpeed);
        CorporateUI.Label(new Rect(0f, 288f, w, 46f), "Mugirl.CorporatePricing.Breakdown".Translate(
            CorporateUI.Money(standardCost), CorporateUI.Money(Math.Max(0, cost - standardCost))), GameFont.Tiny);
        y = rect.height - 169f;
        Rect limitReason = new(0f, y - 67f, w, 40f);
        CorporateUI.Label(limitReason, network.OrderQuantityLimitReason(product), GameFont.Tiny, CorporateUI.Muted);
        TooltipHandler.TipRegionByKey(limitReason, "Mugirl.CorporatePricing.QuantityLimitTerms");
        CorporateUI.Label(new Rect(0f, y - 23f, w, 20f), CorporateBrowserText.Get(CorporateBrowserText.Quantity), GameFont.Tiny, CorporateUI.Muted);
        DrawQuantity(new Rect(0f, y, w, 32f), ref quantity, ref quantityText, maximum, "orders/quantity");
        // 数量控件可能在本次事件里改变数量，提交前同步重算。
        cost = network.OrderQuote(product, quantity, orderSpeed);
        CorporateUI.Label(new Rect(0f, y + 39f, w * 0.25f, 28f), CorporateBrowserText.Get(CorporateBrowserText.Total), GameFont.Tiny, CorporateUI.Muted, TextAnchor.MiddleLeft);
        CorporateUI.Label(new Rect(w * 0.25f, y + 39f, w * 0.75f, 28f), CorporateUI.Money(cost), anchor: TextAnchor.MiddleRight);
        bool canTrade = network.CanTrade(Context, out string? reason);
        Rect submit = new(0f, y + 74f, w, 36f);
        int refundPercent = UnityMathf.RoundToInt(network.TradeSettings.cancelRefundFactor * 100f);
        if (CorporateUI.WantsTooltip(submit))
        {
            TooltipHandler.TipRegion(submit, "Mugirl.Corporate.OrderTerms".Translate(minDays, maxDays, refundPercent));
        }

        if (CorporateUI.Button(submit, "Mugirl.Corporate.PlaceOrder".Translate(), canTrade && cost > 0 && Terminal.SilverCount >= cost, true, "orders/place"))
        {
            bool success = network.PlaceOrder(Context, def, orderStuff, orderQuality, quantity, out reason, orderSpeed);
            Terminal.ShowTradeResult(success, reason);
            if (success)
            {
                Terminal.RequestContentTransition(() => ShowReceipts(true));
            }
        }

        string terms = reason ?? (Terminal.SilverCount < cost ? CorporateBrowserText.Get(CorporateBrowserText.InsufficientFunds)
            : CorporateBrowserText.Get(CorporateBrowserText.Cancellation, network.TradeSettings.cancelWindowDays.ToString("0.#"), refundPercent));
        Caption(new Rect(0f, y + 117f, w, 46f), terms);
    }

    private void ShowStuffMenu(ThingDef def)
    {
        List<FloatMenuOption> options = [];
        foreach (ThingDef stuff in GenStuff.AllowedStuffsFor(def))
        {
            ThingDef choice = stuff;
            options.Add(new FloatMenuOption(choice.LabelCap, () => Selection.Request(() =>
            {
                orderStuff = choice;
                RefreshPreview();
            })));
        }

        Terminal.ShowMenu(options);
    }

    private void ShowQualityMenu()
    {
        List<FloatMenuOption> options = [];
        for (int q = 0; q <= (int)QualityCategory.Excellent; q++)
        {
            QualityCategory quality = (QualityCategory)q;
            options.Add(new FloatMenuOption(quality.GetLabel(), () => Selection.Request(() =>
            {
                orderQuality = quality;
                RefreshPreview();
            })));
        }

        Terminal.ShowMenu(options);
    }

    private void ShowSpeedMenu()
    {
        List<FloatMenuOption> options = [];
        foreach (CorporateOrderSpeed speed in Enum.GetValues<CorporateOrderSpeed>())
        {
            CorporateOrderSpeed choice = speed;
            options.Add(new FloatMenuOption(SpeedLabel(choice), () => orderSpeed = choice));
        }

        Terminal.ShowMenu(options);
    }

    private void RefreshPreview()
    {
        DiscardPreview();
        ThingDef? def = orderDef;
        CorporateNetwork? network = CorporateNetwork.Current;
        if (def is null || network is null || !network.CanAutomaticallySupply(def))
        {
            return;
        }

        using (Rand.Block(Gen.HashCombineInt(def.shortHash, (orderStuff?.shortHash ?? 0) * 7 + (int)orderQuality)))
        {
            try
            {
                preview = CorporateNetwork.MakeOrderProduct(def, orderStuff, orderQuality);
            }
            catch (Exception exception)
            {
                Terminal.ShowTradeResult(false, "Mugirl.Corporate.GenerationError".Translate(def.LabelCap, exception.Message));
            }
        }
    }

    private void DiscardPreview()
    {
        if (preview is not null)
        {
            CorporateNetwork.DiscardUnspawnedProduct(preview);
        }

        preview = null;
    }

    private void DrawReceipts(Rect rect, CorporateNetwork network)
    {
        EnsureReceipts(network);
        Rect view = new(0f, 0f, rect.width - 18f, UnityMathf.Max(rect.height, receipts.Count * ReceiptStride));
        CorporateUI.BeginScrollView(rect, ref listScroll, view, "orders/receipts-scroll");
        try
        {
            for (int i = 0; i < receipts.Count; i++)
            {
                float y = i * ReceiptStride;
                if (y + ReceiptHeight < listScroll.y || y > listScroll.y + rect.height)
                {
                    continue;
                }

                DrawReceipt(receipts[i], y, view.width, network);
            }

            if (receipts.Count == 0)
            {
                CorporateUI.Label(new Rect(8f, 8f, view.width - 16f, 80f), "Mugirl.Corporate.NoOrders".Translate(), color: CorporateUI.Muted);
            }
        }
        finally
        {
            CorporateUI.EndScrollView();
        }
    }

    // 回执取最近的 100 个订单（新的在前）；订单只在操作后变化，按（版本、订单数）缓存。
    private void EnsureReceipts(CorporateNetwork network)
    {
        IReadOnlyList<CorporateOrder> orders = network.Orders;
        if (receiptsVersion == Terminal.CacheVersion && receiptsCount == orders.Count)
        {
            return;
        }

        receiptsVersion = Terminal.CacheVersion;
        receiptsCount = orders.Count;
        receipts.Clear();
        for (int i = orders.Count - 1; i >= 0 && receipts.Count < MaxReceipts; i--)
        {
            receipts.Add(orders[i]);
        }
    }

    private void DrawReceipt(CorporateOrder order, float y, float width, CorporateNetwork network)
    {
        CorporateUI.Panel(new Rect(0f, y, width, ReceiptHeight));
        DrawIcon(new Rect(10f, y + 14f, 42f, 42f), order.def);
        FittedText(new Rect(63f, y + 10f, width - 203f, 45f),
            "#" + order.id + "  " + (order.def?.LabelCap.ToString() ?? order.savedLabel) + " ×" + order.quantity, GameFont.Small);
        string state = OrderStateKey(order.state).Translate();
        int now = CorporateNetwork.Now;
        string timing = order.state == CorporateOrderState.Preparing
            ? CorporateBrowserText.Get(CorporateBrowserText.RemainingDays, UnityMathf.Max(0f, (order.readyTick - now) / 60000f).ToString("0.0"))
            : string.Empty;
        Caption(new Rect(10f, y + 66f, width - 150f, 20f), state + (timing.Length > 0 ? " · " + timing : string.Empty));
        Rect paid = new(10f, y + 90f, width - 150f, 20f);
        Caption(paid, CorporateBrowserText.Get(CorporateBrowserText.Paid, CorporateUI.Money(order.paid)) + " · " + SpeedLabel(order.speed));
        if (CorporateUI.WantsTooltip(paid))
        {
            TooltipHandler.TipRegion(paid, "Mugirl.CorporatePricing.Breakdown".Translate(CorporateUI.Money(order.paid - order.expediteFee), CorporateUI.Money(order.expediteFee)));
        }

        if (order.state == CorporateOrderState.Preparing)
        {
            float progress = UnityMathf.Clamp01((now - order.createdTick) / (float)Math.Max(1, order.readyTick - order.createdTick));
            CorporateUI.Fill(new Rect(10f, y + 58f, width - 150f, 2f), CorporateUI.Border);
            CorporateUI.Fill(new Rect(10f, y + 58f, (width - 150f) * progress, 2f), CorporateUI.Muted);
        }

        if (CorporateUI.Button(new Rect(width - 122f, y + 12f, 112f, 32f), "Mugirl.Corporate.Claim".Translate(),
            order.state == CorporateOrderState.Ready || order.state == CorporateOrderState.RefundDue, true, "orders/claim", order.id))
        {
            bool ok = network.ClaimOrder(Context, order, out string? why);
            Terminal.ShowTradeResult(ok, why);
        }

        bool cancellable = now < order.readyTick && now - order.createdTick <= network.TradeSettings.cancelWindowDays * CorporateNetwork.DayTicks;
        if (order.state == CorporateOrderState.Preparing
            && CorporateUI.Button(new Rect(width - 122f, y + 68f, 112f, 32f), "Mugirl.Corporate.CancelOrder".Translate(), cancellable, id: "orders/cancel", index: order.id))
        {
            int refundPercent = UnityMathf.RoundToInt(network.TradeSettings.cancelRefundFactor * 100f);
            Terminal.Confirm(CorporateBrowserText.Get(CorporateBrowserText.ConfirmCancellation, order.id, refundPercent), () =>
            {
                bool ok = network.CancelOrder(Context, order, out string? why);
                Terminal.ShowTradeResult(ok, why);
            }, true);
        }
    }

    private static string OrderStateKey(CorporateOrderState state) => state switch
    {
        CorporateOrderState.Ready => "Mugirl.Corporate.OrderState.Ready",
        CorporateOrderState.Completed => "Mugirl.Corporate.OrderState.Completed",
        CorporateOrderState.Cancelled => "Mugirl.Corporate.OrderState.Cancelled",
        CorporateOrderState.RefundDue => "Mugirl.Corporate.OrderState.RefundDue",
        CorporateOrderState.Refunded => "Mugirl.Corporate.OrderState.Refunded",
        _ => "Mugirl.Corporate.OrderState.Preparing",
    };

    internal override void PostClose()
    {
        DiscardPreview();
        Selection.Clear();
        catalog.Clear();
        leadLabels.Clear();
        receipts.Clear();
    }
}
