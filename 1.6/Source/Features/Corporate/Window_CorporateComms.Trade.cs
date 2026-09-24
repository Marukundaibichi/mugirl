using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public partial class Window_CorporateComms
    {
        private bool productSelling;
        private bool showExistingOrders;
        private string tradeSearch = "";
        private string orderSearch = "";
        private string tradeQuantityText = "1";
        private string orderQuantityText = "1";
        private int tradeQuantity = 1;
        private int orderQuantity = 1;
        // 周购/收购列表的筛选排序结果缓存：输入搜索词、切分类/排序或操作（版本号）后重建，
        // 替代每 GUI 事件对全部候选跑 ProductFactor 与 OrderBy。
        private readonly List<Thing> tradeProductsView = new List<Thing>();
        private readonly List<CorporateStock> tradeOffersView = new List<CorporateStock>();
        private int tradeViewVersion = -1;
        private bool tradeViewSelling;
        private string tradeViewSearch;
        private int tradeViewCategory = -1;
        private int tradeViewSort = -1;
        private Vector2 tradeScroll;
        private Vector2 orderScroll;
        private Vector2 tradePageScroll;
        private Vector2 orderPageScroll;
        private CorporateStock selectedStock;
        private Thing selectedProduct;
        private Thing orderPreview;
        private ThingDef orderDef;
        private ThingDef orderStuff;
        private QualityCategory orderQuality = QualityCategory.Normal;
        private int tradeCategory;
        private int orderCategory;
        private int tradeSort;
        private int orderSort;
        private bool tradeListView;
        private bool orderListView;
        private readonly CatalogTransition tradeSelectionMotion = new CatalogTransition();
        private readonly CatalogTransition orderSelectionMotion = new CatalogTransition();
        private float tradeResultsStarted = -10f;
        private float orderResultsStarted = -10f;
        private readonly Dictionary<ThingDef, int> catalogPrices = new Dictionary<ThingDef, int>();
        private readonly Dictionary<ThingDef, float> catalogMarketValues = new Dictionary<ThingDef, float>();
        private readonly List<CatalogEntry> orderEntries = new List<CatalogEntry>();
        private readonly Dictionary<ThingDef, CatalogEntry> orderEntryByDef = new Dictionary<ThingDef, CatalogEntry>();
        private List<CatalogEntry> orderView = new List<CatalogEntry>();
        private IReadOnlyList<ThingDef> orderCatalogSource;
        private CatalogViewTask orderViewTask;
        private int orderIndexPosition;
        private bool orderIndexComplete;
        private int orderViewVersion;
        private int orderViewCommittedVersion = -1;
        private int catalogPriceServiceLevel = -1;
        private int orderVisibleQuoteStart;
        private int orderVisibleQuoteEnd;
        private int orderVisibleQuoteCursor;
        private const double CatalogWorkBudgetMilliseconds = 0.75;
        private static readonly string[] CatalogCategoryKeys = { "All", "Materials", "Food", "Medicine", "Weapons", "Apparel", "Rare", "Other" };
        private static readonly string[] CatalogSortKeys = { "SortName", "SortPriceLow", "SortPriceHigh" };

        private struct CatalogEntry
        {
            internal ThingDef def;
            internal string label;
            internal int categoryMask;
            internal IntRange lead;
            internal bool special;
        }

        private sealed class CatalogViewTask : IDisposable
        {
            internal int version;
            internal string search;
            internal int category;
            internal int sort;
            internal int scanIndex;
            internal readonly List<CatalogEntry> results = new List<CatalogEntry>();
            internal SortedDictionary<int, List<CatalogEntry>> priceBuckets;
            internal IEnumerator<KeyValuePair<int, List<CatalogEntry>>> bucketEnumerator;
            internal List<CatalogEntry> currentBucket;
            internal int currentBucketIndex;
            internal bool flattening;

            public void Dispose()
            {
                bucketEnumerator?.Dispose();
                bucketEnumerator = null;
            }
        }

        // Only the selected product fades. The search field keeps its focus and the
        // catalogue remains usable when the player rapidly compares several items.
        private sealed class CatalogTransition
        {
            private Action pending;
            private float started = -10f;
            private float outgoing = 1f;
            private bool committed = true;
            internal bool Active => Time.realtimeSinceStartup - started < 0.25f;
            internal float Opacity
            {
                get
                {
                    float elapsed = Time.realtimeSinceStartup - started;
                    return committed ? CorporateMotion.Ease((elapsed - 0.08f) / 0.17f)
                        : outgoing * (1f - CorporateMotion.Ease(elapsed / 0.08f));
                }
            }
            internal void Request(Action apply)
            {
                outgoing = Opacity;
                pending = apply;
                started = Time.realtimeSinceStartup;
                committed = false;
            }
            internal void Advance()
            {
                if (committed || Time.realtimeSinceStartup - started < 0.08f) return;
                Action apply = pending; pending = null; committed = true;
                apply?.Invoke();
            }
            internal void Clear()
            {
                pending = null; committed = true; started = -10f;
            }
        }

        // 大型模组列表可产生数万条目录记录。所有全量工作都在 WindowUpdate 中按时间预算推进；
        // DrawContents 只读取已提交快照和可见区报价，绝不在 GUI 事件中同步扫描完整目录。
        private void UpdateOrderCatalogWork()
        {
            if (network == null || showExistingOrders || selectedPage != 2 && requestedPage != 2) return;

            int serviceLevel = network.ServiceLevel;
            if (catalogPriceServiceLevel < 0) catalogPriceServiceLevel = serviceLevel;
            else if (catalogPriceServiceLevel != serviceLevel)
            {
                catalogPriceServiceLevel = serviceLevel;
                catalogPrices.Clear();
                RequestOrderViewRefresh();
            }

            if (orderCatalogSource == null)
            {
                orderCatalogSource = network.OrderCatalog;
            }

            long started = Stopwatch.GetTimestamp();
            long budget = Math.Max(1L, (long)(Stopwatch.Frequency * CatalogWorkBudgetMilliseconds / 1000d));
            bool didAny = false;
            while (!didAny || Stopwatch.GetTimestamp() - started < budget)
            {
                bool progressed = false;
                if (!orderIndexComplete)
                {
                    BuildNextCatalogEntry();
                    progressed = true;
                    didAny = true;
                }

                if (orderViewTask == null && orderViewCommittedVersion != orderViewVersion)
                    StartOrderViewTask();

                if (orderViewTask != null)
                {
                    progressed |= AdvanceOrderViewTask();
                    didAny = true;
                }
                else if (orderViewCommittedVersion == orderViewVersion)
                {
                    progressed |= WarmNextVisibleCatalogQuote();
                    didAny |= progressed;
                }

                if (!progressed) break;
            }
        }

        private void BuildNextCatalogEntry()
        {
            if (orderCatalogSource == null || orderIndexPosition >= orderCatalogSource.Count)
            {
                orderIndexComplete = true;
                return;
            }

            ThingDef def = orderCatalogSource[orderIndexPosition++];
            var entry = new CatalogEntry
            {
                def = def,
                label = def.LabelCap.ToString(),
                categoryMask = CatalogCategoryMask(def),
                lead = network.OrderLeadTime(def),
                special = network.IsSpecialOrder(def)
            };
            orderEntries.Add(entry);
            orderEntryByDef[def] = entry;
            if (orderIndexPosition >= orderCatalogSource.Count) orderIndexComplete = true;
        }

        private int CatalogCategoryMask(ThingDef def)
        {
            int mask = 1;
            if (def.IsStuff) mask |= 1 << 1;
            if (def.IsNutritionGivingIngestible && !def.IsCorpse) mask |= 1 << 2;
            if (def.IsMedicine) mask |= 1 << 3;
            if (def.IsWeapon) mask |= 1 << 4;
            if (def.IsApparel) mask |= 1 << 5;
            if (network.IsRareOrder(def) || network.IsSpecialOrder(def)) mask |= 1 << 6;
            if (!def.IsStuff && !def.IsNutritionGivingIngestible && !def.IsMedicine && !def.IsWeapon && !def.IsApparel)
                mask |= 1 << 7;
            return mask;
        }

        private void StartOrderViewTask()
        {
            orderViewTask?.Dispose();
            orderViewTask = new CatalogViewTask
            {
                version = orderViewVersion,
                search = (orderSearch ?? string.Empty).Trim(),
                category = orderCategory,
                sort = orderSort,
                priceBuckets = orderSort == 0 ? null : new SortedDictionary<int, List<CatalogEntry>>()
            };
        }

        private bool AdvanceOrderViewTask()
        {
            CatalogViewTask task = orderViewTask;
            if (task == null) return false;
            if (task.version != orderViewVersion)
            {
                task.Dispose();
                orderViewTask = null;
                return true;
            }

            if (task.scanIndex < orderEntries.Count)
            {
                CatalogEntry entry = orderEntries[task.scanIndex++];
                if (MatchesOrderCatalog(entry, task.search, task.category))
                {
                    if (task.sort == 0) task.results.Add(entry);
                    else
                    {
                        int price = CatalogUnitPrice(entry.def);
                        if (!task.priceBuckets.TryGetValue(price, out List<CatalogEntry> bucket))
                        {
                            bucket = new List<CatalogEntry>();
                            task.priceBuckets.Add(price, bucket);
                        }
                        // orderEntries 已按名称排序，因此同价桶天然保持 ThenBy(name) 的原有语义。
                        bucket.Add(entry);
                    }
                }
                return true;
            }

            if (!orderIndexComplete) return false;
            if (task.sort == 0)
            {
                CommitOrderView(task);
                return true;
            }

            if (!task.flattening)
            {
                IEnumerable<KeyValuePair<int, List<CatalogEntry>>> buckets = task.sort == 1
                    ? task.priceBuckets : task.priceBuckets.Reverse();
                task.bucketEnumerator = buckets.GetEnumerator();
                task.flattening = true;
                return true;
            }

            if (task.currentBucket != null && task.currentBucketIndex < task.currentBucket.Count)
            {
                task.results.Add(task.currentBucket[task.currentBucketIndex++]);
                return true;
            }

            while (task.bucketEnumerator.MoveNext())
            {
                task.currentBucket = task.bucketEnumerator.Current.Value;
                task.currentBucketIndex = 0;
                if (task.currentBucket.Count == 0) continue;
                task.results.Add(task.currentBucket[task.currentBucketIndex++]);
                return true;
            }

            CommitOrderView(task);
            return true;
        }

        private void CommitOrderView(CatalogViewTask task)
        {
            if (task != orderViewTask || task.version != orderViewVersion) return;
            task.Dispose();
            orderView = task.results;
            orderViewCommittedVersion = task.version;
            orderViewTask = null;
            orderVisibleQuoteCursor = orderVisibleQuoteStart;
        }

        private void RequestOrderViewRefresh()
        {
            orderViewVersion++;
            orderViewTask?.Dispose();
            orderViewTask = null;
            orderVisibleQuoteCursor = orderVisibleQuoteStart;
        }

        private bool WarmNextVisibleCatalogQuote()
        {
            int end = Math.Min(orderVisibleQuoteEnd, orderView.Count);
            if (orderVisibleQuoteCursor < orderVisibleQuoteStart || orderVisibleQuoteCursor >= end)
                orderVisibleQuoteCursor = orderVisibleQuoteStart;
            while (orderVisibleQuoteCursor < end)
            {
                CatalogEntry entry = orderView[orderVisibleQuoteCursor++];
                if (catalogPrices.ContainsKey(entry.def)) continue;
                CatalogUnitPrice(entry.def);
                return true;
            }
            return false;
        }

        private bool OrderCatalogBusy => !orderIndexComplete || orderViewTask != null
            || orderViewCommittedVersion != orderViewVersion;

        private static string CorporateInput(Rect rect, string value, string id, string placeholder = null)
        {
            return CorporateUI.Input(rect, value, placeholder, id);
        }

        private static void CorporateNumeric(Rect rect, ref int value, ref string buffer, int minimum, int maximum, string id)
        {
            maximum = Math.Max(minimum, maximum);
            value = Mathf.Clamp(value, minimum, maximum);
            if (!CorporateUI.InputFocused(rect, id)) buffer = value.ToString();
            buffer = CorporateInput(rect, buffer, id);
            if (int.TryParse(buffer, out int parsed)) value = Mathf.Clamp(parsed, minimum, maximum);
            bool commit = !CorporateUI.InputFocused(rect, id)
                || Event.current.rawType == EventType.MouseDown && !rect.Contains(Event.current.mousePosition)
                || Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (commit) buffer = value.ToString();
        }

        private void CorporateQuantity(Rect rect, ref int quantity, ref string buffer, int maximum, string id)
        {
            const float gap = 4f;
            float stepWidth = Mathf.Clamp(rect.width * 0.13f, 22f, 30f);
            float maxWidth = Mathf.Clamp(rect.width * 0.26f, 46f, 70f);
            float inputWidth = Mathf.Max(32f, rect.width - 2f * stepWidth - maxWidth - 3f * gap);
            if (CorporateUI.Button(new Rect(rect.x, rect.y, stepWidth, rect.height), "−", quantity > 1, id: id + "/minus"))
            { quantity--; buffer = quantity.ToString(); }
            CorporateNumeric(new Rect(rect.x + stepWidth + gap, rect.y, inputWidth, rect.height),
                ref quantity, ref buffer, 1, Math.Max(1, maximum), id + "/value");
            if (CorporateUI.Button(new Rect(rect.xMax - maxWidth - gap - stepWidth, rect.y, stepWidth, rect.height), "+", quantity < maximum, id: id + "/plus"))
            { quantity++; buffer = quantity.ToString(); }
            if (CorporateUI.Button(new Rect(rect.xMax - maxWidth, rect.y, maxWidth, rect.height), "Mugirl.Corporate.Maximum".Translate(), maximum > 0, id: id + "/maximum"))
            { quantity = maximum; buffer = quantity.ToString(); }
        }

        private void CorporateFeedback(bool success, string reason)
        {
            ShowFeedback(success, success ? "Mugirl.Corporate.Success".Translate().ToString()
                : reason ?? "Mugirl.Corporate.GoodsChanged".Translate().ToString());
        }

        private void DrawTrade(Rect rect)
        {
            float minimumHeight = rect.width - 18f >= 720f ? 480f : 700f;
            if (rect.height >= minimumHeight) { DrawTradeBody(rect); return; }
            Rect view = new Rect(0f, 0f, rect.width - 18f, minimumHeight);
            CorporateUI.BeginScrollView(rect, ref tradePageScroll, view, "trade/page-scroll");
            try { DrawTradeBody(view); }
            finally { CorporateUI.EndScrollView(); }
        }

        private void DrawTradeBody(Rect rect)
        {
            tradeSelectionMotion.Advance();
            float w = rect.width;
            if (CorporateUI.Button(new Rect(0f, 0f, w / 2f - 4f, 34f), "Mugirl.Corporate.WeeklyStock".Translate(), primary: !productSelling, id: "trade/buy") && productSelling)
                RequestContentTransition(() => { productSelling = false; ResetTradeCatalog(); }, "trade/buy");
            Rect productTab = new Rect(w / 2f + 4f, 0f, w / 2f - 4f, 34f);
            if (CorporateUI.Button(productTab, "Mugirl.Corporate.ProductPurchase".Translate(), primary: productSelling, id: "trade/sell") && !productSelling)
                RequestContentTransition(() => { productSelling = true; ResetTradeCatalog(); }, "trade/sell");
            if (Mouse.IsOver(productTab))
                TooltipHandler.TipRegion(productTab, "Mugirl.Corporate.ProductHint".Translate());
            DrawCatalogToolbar(new Rect(0f, 46f, w, 32f), false);
            if (tradeViewVersion != frameCacheVersion || tradeViewSelling != productSelling
                || tradeViewSearch != tradeSearch || tradeViewCategory != tradeCategory || tradeViewSort != tradeSort)
            {
                tradeViewVersion = frameCacheVersion;
                tradeViewSelling = productSelling;
                tradeViewSearch = tradeSearch;
                tradeViewCategory = tradeCategory;
                tradeViewSort = tradeSort;
                tradeProductsView.Clear();
                tradeOffersView.Clear();
                if (productSelling)
                {
                    tradeProductsView.AddRange(SortCatalog(
                        context.AvailableContractThings.Where(t => network.ProductFactor(t) > 0f
                            && MatchesCatalog(t.def, t.Label, tradeSearch, tradeCategory)).ToList(),
                        t => t.Label, t => network.ProductQuote(t, 1), tradeSort));
                }
                else
                {
                    tradeOffersView.AddRange(SortCatalog(
                        network.Stock.Where(s => s.sample != null && !s.sample.Destroyed
                            && network.IsOrderable(s.sample.def) && !network.IsSpecialOrder(s.sample.def)
                            && MatchesCatalog(s.sample.def, s.sample.Label, tradeSearch, tradeCategory)).ToList(),
                        s => s.sample.Label, s => network.StockUnitPrice(s), tradeSort));
                }
            }
            List<Thing> products = productSelling ? tradeProductsView : null;
            List<CorporateStock> offers = productSelling ? null : tradeOffersView;
            int count = productSelling ? products.Count : offers.Count;
            DrawCatalogSummary(new Rect(0f, 83f, w, 24f), count, productSelling
                ? BrowserText("PurchaseDesk") : BrowserText("RefreshIn", Mathf.Max(0f, (network.NextRefreshTick - CorporateNetwork.Now) / 60000f).ToString("0.0")));
            CatalogLayout(new Rect(0f, 116f, w, rect.height - 116f), out Rect listing, out Rect detail);
            bool listMode = tradeListView || listing.width < 360f;
            CatalogGrid(listing, count, listMode, out int columns, out float cardWidth, out float stride, out Rect view);
            using (CorporateUI.BeginFrame(motion, "trade/results", ResultOpacity(tradeResultsStarted)))
            {
                CorporateUI.BeginScrollView(listing, ref tradeScroll, view, "trade/item-scroll");
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        Rect card = new Rect(i % columns * (cardWidth + 10f), i / columns * stride, cardWidth, stride - 8f);
                        if (card.yMax < tradeScroll.y || card.y > tradeScroll.y + listing.height) continue;
                        Thing thing = productSelling ? products[i] : offers[i].sample;
                        CorporateStock offer = productSelling ? null : offers[i];
                        int quantity = productSelling ? thing.stackCount : offer.count;
                        int price = productSelling ? network.ProductQuote(thing, 1) : network.StockUnitPrice(offer);
                        bool selected = productSelling ? thing == selectedProduct : offer == selectedStock;
                        if (DrawCatalogItem(card, thing.def, thing, thing.LabelCap, CorporateUI.Money(price),
                            BrowserText(quantity > 0 ? "Available" : "SoldOut", quantity), selected, listMode, "trade/item/" + thing.thingIDNumber))
                        {
                            if (selected) continue;
                            tradeSelectionMotion.Request(() =>
                            {
                                if (productSelling) selectedProduct = thing; else selectedStock = offer;
                                tradeQuantity = 1; tradeQuantityText = "1";
                            });
                            GUI.FocusControl(null);
                        }
                    }
                    if (count == 0) DrawCatalogEmpty(view, false);
                }
                finally { CorporateUI.EndScrollView(); }
            }
            DrawCatalogDetailFrame(detail, tradeSelectionMotion, "trade/detail", r => DrawTradeDetails(r, products, offers));
        }

        private void DrawTradeDetails(Rect rect, List<Thing> products, List<CorporateStock> offers)
        {
            Thing selectedThing = productSelling ? selectedProduct : selectedStock?.sample;
            bool valid = selectedThing != null && !selectedThing.Destroyed && (productSelling
                ? products.Contains(selectedThing) : selectedStock != null && offers.Contains(selectedStock));
            if (!valid) { DrawDetailEmpty(rect); return; }
            float w = rect.width;
            int stocked = productSelling ? selectedThing.stackCount : selectedStock.count;
            int price = productSelling ? network.ProductQuote(selectedThing, 1) : network.StockUnitPrice(selectedStock);
            int available = productSelling ? stocked : Math.Min(stocked, context.SilverCount / Math.Max(1, price));
            tradeQuantity = Mathf.Clamp(tradeQuantity, 1, Math.Max(1, available));
            DrawProductHeading(rect, selectedThing, "trade/info");
            DrawCatalogMetric(new Rect(0f, 94f, w * 0.58f, 51f), BrowserText(productSelling ? "PurchasePrice" : "UnitPrice"), CorporateUI.Money(price));
            DrawCatalogMetric(new Rect(w * 0.58f + 8f, 94f, w * 0.42f - 8f, 51f), BrowserText("Stock"), stocked.ToString("N0"));
            float quantityY = rect.height - 161f;
            CorporateUI.Label(new Rect(0f, quantityY - 23f, w, 21f), BrowserText("Quantity"), GameFont.Tiny, CorporateUI.Muted);
            CorporateQuantity(new Rect(0f, quantityY, w, 32f), ref tradeQuantity, ref tradeQuantityText, available, "trade/quantity");
            int total = productSelling ? network.ProductQuote(selectedThing, tradeQuantity) : CorporateNetwork.Price(price, tradeQuantity);
            CorporateUI.Label(new Rect(0f, quantityY + 39f, w * 0.25f, 28f), BrowserText("Total"), GameFont.Tiny, CorporateUI.Muted, TextAnchor.MiddleLeft);
            CorporateUI.Label(new Rect(w * 0.25f, quantityY + 39f, w * 0.75f, 28f), CorporateUI.Money(total), anchor: TextAnchor.MiddleRight);
            bool permitted = network.CanTrade(context, out string permissionReason);
            if (CorporateUI.Button(new Rect(0f, quantityY + 74f, w, 36f),
                (productSelling ? "Mugirl.Corporate.ConfirmSale" : "Mugirl.Corporate.ConfirmPurchase").Translate(),
                permitted && available > 0 && total > 0, true, "trade/confirm"))
            {
                string reason;
                bool ok = productSelling ? network.SellProduct(context, selectedThing, tradeQuantity, out reason)
                    : network.BuyStock(context, selectedStock, tradeQuantity, out reason);
                CorporateFeedback(ok, reason);
            }
            string status = permissionReason ?? (!productSelling && available == 0
                ? BrowserText(stocked == 0 ? "SoldOut" : "InsufficientFunds")
                : context.Caravan != null ? BrowserText("Mass", context.IncomingMass(selectedThing, tradeQuantity).ToString("0.0"))
                : BrowserText("DeliveryTo", context.Label));
            CatalogCaption(new Rect(0f, quantityY + 117f, w, 38f), status);
        }

        private void DrawOrders(Rect rect)
        {
            float minimumHeight = rect.width - 18f >= 720f ? 570f : 780f;
            if (showExistingOrders || rect.height >= minimumHeight) { DrawOrdersBody(rect); return; }
            Rect view = new Rect(0f, 0f, rect.width - 18f, minimumHeight);
            CorporateUI.BeginScrollView(rect, ref orderPageScroll, view, "orders/page-scroll");
            try { DrawOrdersBody(view); }
            finally { CorporateUI.EndScrollView(); }
        }

        private void DrawOrdersBody(Rect rect)
        {
            orderSelectionMotion.Advance();
            float w = rect.width;
            if (CorporateUI.Button(new Rect(0f, 0f, w / 2f - 4f, 34f), "Mugirl.Corporate.OrderCatalog".Translate(), primary: !showExistingOrders, id: "orders/catalog") && showExistingOrders)
                RequestContentTransition(() => { showExistingOrders = false; orderScroll = Vector2.zero; }, "orders/catalog");
            if (CorporateUI.Button(new Rect(w / 2f + 4f, 0f, w / 2f - 4f, 34f), "Mugirl.Corporate.MyOrders".Translate(), primary: showExistingOrders, id: "orders/receipts") && !showExistingOrders)
                RequestContentTransition(() => { showExistingOrders = true; orderScroll = Vector2.zero; }, "orders/receipts");
            if (showExistingOrders) { DrawOrderReceipts(new Rect(0f, 43f, w, rect.height - 43f)); return; }
            DrawCatalogToolbar(new Rect(0f, 46f, w, 32f), true);
            List<CatalogEntry> entries = orderView;
            DrawCatalogSummary(new Rect(0f, 83f, w, 24f), entries.Count,
                BrowserText(OrderCatalogBusy ? "UpdatingCatalog" : "ProcurementDesk"));
            CatalogLayout(new Rect(0f, 116f, w, rect.height - 116f), out Rect listing, out Rect detail, true);
            bool listMode = orderListView || listing.width < 360f;
            CatalogGrid(listing, entries.Count, listMode, out int columns, out float cardWidth, out float stride, out Rect view);
            using (CorporateUI.BeginFrame(motion, "orders/results", ResultOpacity(orderResultsStarted)))
            {
                CorporateUI.BeginScrollView(listing, ref orderScroll, view, "orders/catalog-scroll");
                try
                {
                    CatalogVisibleRange(entries.Count, columns, stride, orderScroll.y, listing.height, out int firstVisible, out int lastVisible);
                    if (firstVisible != orderVisibleQuoteStart || lastVisible != orderVisibleQuoteEnd)
                    {
                        orderVisibleQuoteStart = firstVisible;
                        orderVisibleQuoteEnd = lastVisible;
                        orderVisibleQuoteCursor = firstVisible;
                    }
                    for (int i = firstVisible; i < lastVisible; i++)
                    {
                        Rect card = new Rect(i % columns * (cardWidth + 10f), i / columns * stride, cardWidth, stride - 8f);
                        CatalogEntry entry = entries[i];
                        ThingDef def = entry.def;
                        IntRange lead = entry.lead;
                        string leadLabel = BrowserText(entry.special ? "SpecialLead" : "Lead", lead.min, lead.max);
                        bool quoteReady = catalogPrices.TryGetValue(def, out int quoted);
                        if (DrawCatalogItem(card, def, null, entry.label,
                            quoteReady ? quoted > 0 ? CorporateUI.Money(quoted) : BrowserText("Unavailable") : BrowserText("QuotePending"), leadLabel,
                            orderDef == def, listMode, "orders/item/" + def.defName) && orderDef != def)
                        {
                            orderSelectionMotion.Request(() =>
                            {
                                orderDef = def; orderStuff = GenStuff.DefaultStuffFor(orderDef);
                                orderQuality = QualityCategory.Normal;
                                orderQuantity = 1; orderQuantityText = "1";
                                RefreshOrderPreview();
                            });
                            GUI.FocusControl(null);
                        }
                    }
                    if (entries.Count == 0)
                    {
                        if (OrderCatalogBusy) DrawCatalogWorking(view);
                        else DrawCatalogEmpty(view, true);
                    }
                }
                finally { CorporateUI.EndScrollView(); }
            }
            DrawCatalogDetailFrame(detail, orderSelectionMotion, "orders/detail", DrawOrderDetails);
        }

        private void DrawOrderDetails(Rect rect)
        {
            string search = (orderSearch ?? string.Empty).Trim();
            if (orderPreview == null || orderPreview.Destroyed
                || !orderEntryByDef.TryGetValue(orderDef, out CatalogEntry selectedEntry)
                || !MatchesOrderCatalog(selectedEntry, search, orderCategory))
            { DrawDetailEmpty(rect); return; }
            float w = rect.width;
            DrawProductHeading(rect, orderPreview, "orders/info");
            IntRange days = network.OrderLeadTime(orderDef);
            DrawCatalogMetric(new Rect(0f, 94f, w * 0.58f, 51f), BrowserText("UnitPrice"), CorporateUI.Money(network.OrderQuote(orderPreview, 1)));
            DrawCatalogMetric(new Rect(w * 0.58f + 8f, 94f, w * 0.42f - 8f, 51f), BrowserText("Preparation"), BrowserText("Days", days.min, days.max));
            float y = 157f;
            float half = (w - 8f) / 2f;
            CorporateUI.Label(new Rect(0f, y, half, 20f), BrowserText("Material"), GameFont.Tiny, CorporateUI.Muted);
            CorporateUI.Label(new Rect(half + 8f, y, half, 20f), BrowserText("Quality"), GameFont.Tiny, CorporateUI.Muted);
            y += 23f;
            if (CorporateUI.Button(new Rect(0f, y, half, 30f), orderStuff?.LabelCap.ToString() ?? "Mugirl.Corporate.NoStuff".Translate(), orderDef.MadeFromStuff, id: "orders/stuff"))
            {
                ShowMenu(GenStuff.AllowedStuffsFor(orderDef).Select(s => new FloatMenuOption(s.LabelCap,
                    () => orderSelectionMotion.Request(() => { orderStuff = s; RefreshOrderPreview(); }))).ToList());
            }
            if (CorporateUI.Button(new Rect(half + 8f, y, half, 30f), orderQuality.GetLabel(), orderPreview.TryGetComp<CompQuality>() != null, id: "orders/quality"))
            {
                ShowMenu(Enumerable.Range(0, (int)QualityCategory.Excellent + 1).Select(q =>
                    new FloatMenuOption(((QualityCategory)q).GetLabel(), () => orderSelectionMotion.Request(() => { orderQuality = (QualityCategory)q; RefreshOrderPreview(); }))).ToList());
            }
            y = rect.height - 169f;
            CorporateUI.Label(new Rect(0f, y - 23f, w, 20f), BrowserText("Quantity"), GameFont.Tiny, CorporateUI.Muted);
            CorporateQuantity(new Rect(0f, y, w, 32f), ref orderQuantity, ref orderQuantityText, CorporateNetwork.MaxOrderQuantity(orderDef), "orders/quantity");
            int cost = network.OrderQuote(orderPreview, orderQuantity);
            CorporateUI.Label(new Rect(0f, y + 39f, w * 0.25f, 28f), BrowserText("Total"), GameFont.Tiny, CorporateUI.Muted, TextAnchor.MiddleLeft);
            CorporateUI.Label(new Rect(w * 0.25f, y + 39f, w * 0.75f, 28f), CorporateUI.Money(cost), anchor: TextAnchor.MiddleRight);
            bool canTrade = network.CanTrade(context, out string reason);
            Rect submit = new Rect(0f, y + 74f, w, 36f);
            TooltipHandler.TipRegion(submit, "Mugirl.Corporate.OrderTerms".Translate(days.min, days.max,
                Mathf.RoundToInt(network.TradeSettings.cancelRefundFactor * 100f)));
            if (CorporateUI.Button(submit, "Mugirl.Corporate.PlaceOrder".Translate(),
                canTrade && cost > 0 && context.SilverCount >= cost, true, "orders/place"))
            {
                bool success = network.PlaceOrder(context, orderDef, orderStuff, orderQuality, orderQuantity, out reason);
                CorporateFeedback(success, reason);
                if (success) RequestContentTransition(() => { showExistingOrders = true; orderScroll = Vector2.zero; }, "orders/receipts");
            }
            string terms = reason ?? (context.SilverCount < cost ? BrowserText("InsufficientFunds")
                : BrowserText("Cancellation", network.TradeSettings.cancelWindowDays.ToString("0.#"), Mathf.RoundToInt(network.TradeSettings.cancelRefundFactor * 100f)));
            CatalogCaption(new Rect(0f, y + 117f, w, 46f), terms);
        }

        private static string BrowserText(string key, params object[] args)
        {
            string fullKey = "Mugirl.CorporateTradeBrowser." + key;
            if (args.Length == 0) return fullKey.Translate();
            if (args.Length == 1) return fullKey.Translate(args[0].ToString());
            return fullKey.Translate(args[0].ToString(), args[1].ToString());
        }

        private void ResetTradeCatalog()
        {
            tradeScroll = Vector2.zero;
            tradeCategory = 0;
            selectedProduct = null; selectedStock = null;
            tradeSelectionMotion.Clear();
        }

        private void CatalogChanged(bool ordering)
        {
            if (ordering)
            {
                orderScroll = Vector2.zero;
                orderResultsStarted = Time.realtimeSinceStartup;
                orderSelectionMotion.Clear();
                RequestOrderViewRefresh();
            }
            else { tradeScroll = Vector2.zero; tradeResultsStarted = Time.realtimeSinceStartup; tradeSelectionMotion.Clear(); }
        }

        private static float ResultOpacity(float started)
        {
            return Mathf.Lerp(0.32f, 1f, CorporateMotion.Ease((Time.realtimeSinceStartup - started) / 0.18f));
        }

        private void DrawCatalogToolbar(Rect rect, bool ordering)
        {
            string scope = ordering ? "orders" : "trade";
            int category = ordering ? orderCategory : tradeCategory;
            int sort = ordering ? orderSort : tradeSort;
            bool listView = ordering ? orderListView : tradeListView;
            float categoryWidth = Mathf.Clamp(rect.width * 0.23f, 102f, 155f);
            float sortWidth = Mathf.Clamp(rect.width * 0.23f, 100f, 147f);
            float searchWidth = rect.width - categoryWidth - sortWidth - 92f;
            if (searchWidth < 76f)
            {
                float reduction = (76f - searchWidth) * 0.5f;
                categoryWidth = Mathf.Max(64f, categoryWidth - reduction);
                sortWidth = Mathf.Max(64f, sortWidth - reduction);
                searchWidth = Mathf.Max(40f, rect.width - categoryWidth - sortWidth - 92f);
            }
            Rect search = new Rect(rect.x, rect.y, searchWidth, rect.height);
            string oldSearch = ordering ? orderSearch : tradeSearch;
            string newSearch = CorporateInput(search, oldSearch, scope + "/search", "Mugirl.Corporate.SearchGoods".Translate());
            if (newSearch != oldSearch)
            {
                if (ordering) orderSearch = newSearch; else tradeSearch = newSearch;
                CatalogChanged(ordering);
            }
            Rect select = new Rect(search.xMax + 8f, rect.y, categoryWidth, rect.height);
            if (CorporateUI.Button(select, BrowserText(CatalogCategoryKeys[category]) + "  ▾", id: scope + "/category"))
            {
                ShowMenu(Enumerable.Range(0, CatalogCategoryKeys.Length).Select(i => new FloatMenuOption(BrowserText(CatalogCategoryKeys[i]), () =>
                {
                    if (ordering) orderCategory = i; else tradeCategory = i;
                    CatalogChanged(ordering);
                })).ToList());
            }
            Rect sortRect = new Rect(select.xMax + 8f, rect.y, sortWidth, rect.height);
            if (CorporateUI.Button(sortRect, BrowserText(CatalogSortKeys[sort]) + "  ▾", id: scope + "/sort"))
            {
                ShowMenu(Enumerable.Range(0, CatalogSortKeys.Length).Select(i => new FloatMenuOption(BrowserText(CatalogSortKeys[i]), () =>
                {
                    if (ordering) orderSort = i; else tradeSort = i;
                    CatalogChanged(ordering);
                })).ToList());
            }
            for (int i = 0; i < 2; i++)
            {
                bool list = i == 1;
                Rect button = new Rect(sortRect.xMax + 8f + i * 34f, rect.y, 30f, rect.height);
                bool selected = list == listView;
                if (CorporateUI.Button(button, "", primary: selected, id: scope + (list ? "/list" : "/grid")) && !selected)
                {
                    if (ordering) orderListView = list; else tradeListView = list;
                    CatalogChanged(ordering);
                }
                Color ink = selected ? CorporateUI.Background : CorporateUI.Ink;
                float x = button.center.x - 6f, y = button.center.y - 6f;
                if (list)
                    for (int row = 0; row < 3; row++) CorporateUI.Fill(new Rect(x, y + row * 5f, 12f, 2f), ink);
                else
                    for (int row = 0; row < 2; row++)
                        for (int col = 0; col < 2; col++) CorporateUI.Fill(new Rect(x + col * 7f, y + row * 7f, 5f, 5f), ink);
                TooltipHandler.TipRegion(button, BrowserText(list ? "ListView" : "GridView"));
            }
        }

        private void DrawCatalogSummary(Rect rect, int count, string desk)
        {
            CorporateUI.Label(new Rect(rect.x, rect.y, rect.width * 0.34f, rect.height), BrowserText("Results", count), GameFont.Tiny, CorporateUI.Muted);
            CatalogCaption(new Rect(rect.x + rect.width * 0.34f, rect.y, rect.width * 0.66f, rect.height), desk, TextAnchor.UpperRight);
        }

        private bool MatchesCatalog(ThingDef def, string label, string search, int category)
        {
            if (!string.IsNullOrWhiteSpace(search) && label.IndexOf(search.Trim(), StringComparison.CurrentCultureIgnoreCase) < 0) return false;
            switch (category)
            {
                case 1: return def.IsStuff;
                case 2: return def.IsNutritionGivingIngestible && !def.IsCorpse;
                case 3: return def.IsMedicine;
                case 4: return def.IsWeapon;
                case 5: return def.IsApparel;
                case 6: return network.IsRareOrder(def) || network.IsSpecialOrder(def);
                case 7: return !def.IsStuff && !def.IsNutritionGivingIngestible && !def.IsMedicine && !def.IsWeapon && !def.IsApparel;
                default: return true;
            }
        }

        private static bool MatchesOrderCatalog(CatalogEntry entry, string search, int category)
        {
            if (!string.IsNullOrEmpty(search)
                && entry.label.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) < 0) return false;
            return category <= 0 || category < CatalogCategoryKeys.Length
                && (entry.categoryMask & 1 << category) != 0;
        }

        private static List<T> SortCatalog<T>(List<T> items, Func<T, string> name, Func<T, int> price, int sort)
        {
            if (sort == 1) return items.OrderBy(price).ThenBy(name).ToList();
            if (sort == 2) return items.OrderByDescending(price).ThenBy(name).ToList();
            return items.OrderBy(name).ToList();
        }

        private int CatalogUnitPrice(ThingDef def)
        {
            if (!network.IsOrderable(def)) return 0;
            int serviceLevel = network.ServiceLevel;
            if (catalogPriceServiceLevel != serviceLevel)
            {
                catalogPriceServiceLevel = serviceLevel;
                catalogPrices.Clear();
            }
            if (catalogPrices.TryGetValue(def, out int price)) return price;
            if (catalogMarketValues.TryGetValue(def, out float marketValue))
                price = network.OrderQuote(def, marketValue, 1);
            else
            {
                Thing preview = null;
                // A catalogue quote must not advance the colony's random sequence.
                using (Rand.Block(def.shortHash))
                {
                    try
                    {
                        preview = CorporateNetwork.MakeProduct(def, GenStuff.DefaultStuffFor(def), QualityCategory.Normal);
                        marketValue = preview.MarketValue;
                        catalogMarketValues[def] = marketValue;
                        price = network.OrderQuote(def, marketValue, 1);
                    }
                    catch (Exception) { price = 0; }
                    finally { CorporateNetwork.DiscardUnspawnedProduct(preview); }
                }
            }
            catalogPrices[def] = price;
            return price;
        }

        private static void CatalogLayout(Rect rect, out Rect listing, out Rect detail, bool ordering = false)
        {
            if (rect.width >= 720f)
            {
                float detailWidth = Mathf.Clamp(rect.width * 0.34f, 272f, 340f);
                listing = new Rect(rect.x, rect.y, rect.width - detailWidth - 16f, rect.height);
                detail = new Rect(listing.xMax + 16f, rect.y, detailWidth, rect.height);
            }
            else
            {
                float detailHeight = ordering ? 454f : 364f;
                listing = new Rect(rect.x, rect.y, rect.width, Mathf.Max(132f, rect.height - detailHeight - 14f));
                detail = new Rect(rect.x, listing.yMax + 14f, rect.width, detailHeight);
            }
        }

        private static void CatalogGrid(Rect rect, int count, bool list, out int columns, out float width, out float stride, out Rect view)
        {
            float viewWidth = Mathf.Max(120f, rect.width - 16f);
            columns = list ? 1 : Mathf.Clamp(Mathf.FloorToInt((viewWidth + 10f) / 184f), 1, 4);
            width = (viewWidth - (columns - 1) * 10f) / columns;
            stride = list ? 80f : 173f;
            view = new Rect(0f, 0f, viewWidth, Mathf.Max(rect.height, Mathf.CeilToInt(count / (float)columns) * stride));
        }

        private static void CatalogVisibleRange(int count, int columns, float stride, float scrollY, float viewportHeight,
            out int first, out int last)
        {
            if (count <= 0)
            {
                first = last = 0;
                return;
            }
            int rows = Mathf.CeilToInt(count / (float)columns);
            float maximumScroll = Math.Max(0f, rows * stride - viewportHeight);
            scrollY = Mathf.Clamp(scrollY, 0f, maximumScroll);
            int firstRow = Math.Max(0, Mathf.FloorToInt(scrollY / stride) - 1);
            int lastRow = Math.Min(rows, Mathf.CeilToInt((scrollY + viewportHeight) / stride) + 1);
            first = Math.Min(count, firstRow * columns);
            last = Math.Min(count, Math.Max(first, lastRow * columns));
        }

        private bool DrawCatalogItem(Rect rect, ThingDef def, Thing thing, string label, string price, string status, bool selected, bool list, string id)
        {
            bool clicked = CorporateUI.Row(rect, selected, id);
            if (list)
            {
                DrawCatalogIcon(new Rect(rect.x + 10f, rect.y + 10f, 48f, 48f), def, thing);
                float priceWidth = Mathf.Clamp(rect.width * 0.34f, 110f, 170f);
                CatalogText(new Rect(rect.x + 69f, rect.y + 10f, rect.width - priceWidth - 83f, 48f), label, GameFont.Small);
                CorporateUI.Label(new Rect(rect.xMax - priceWidth - 10f, rect.y + 10f, priceWidth, 24f), price, anchor: TextAnchor.MiddleRight);
                CatalogCaption(new Rect(rect.xMax - priceWidth - 10f, rect.y + 40f, priceWidth, 24f), status, TextAnchor.MiddleRight);
            }
            else
            {
                DrawCatalogIcon(new Rect(rect.center.x - 29f, rect.y + 9f, 58f, 58f), def, thing);
                CatalogText(new Rect(rect.x + 12f, rect.y + 74f, rect.width - 24f, 42f), label, GameFont.Small, TextAnchor.UpperCenter);
                CorporateUI.Label(new Rect(rect.x + 10f, rect.y + 119f, rect.width - 20f, 22f), price, anchor: TextAnchor.MiddleCenter);
                CatalogCaption(new Rect(rect.x + 10f, rect.y + 143f, rect.width - 20f, 19f), status, TextAnchor.UpperCenter);
            }
            TooltipHandler.TipRegion(rect, label + "\n\n" + def.description);
            return clicked;
        }

        private static void DrawCatalogIcon(Rect rect, ThingDef def, Thing thing = null)
        {
            if (thing != null) { CorporateUI.ThingIcon(rect, thing); return; }
            if (def == null) return;
            CorporateUI.ThingIcon(rect, def);
        }

        private void DrawCatalogDetailFrame(Rect rect, CatalogTransition transition, string id, Action<Rect> draw)
        {
            CorporateUI.Rule(new Rect(rect.x - 8f, rect.y, 1f, rect.height));
            GUI.BeginGroup(rect.ContractedBy(12f));
            try
            {
                using (CorporateUI.BeginFrame(motion, id, transition.Opacity, !transition.Active))
                    draw(new Rect(0f, 0f, rect.width - 24f, rect.height - 24f));
            }
            finally { GUI.EndGroup(); }
        }

        private void DrawCatalogEmpty(Rect rect, bool ordering)
        {
            CorporateUI.Label(new Rect(10f, 16f, rect.width - 20f, 50f), BrowserText("NoResults"), color: CorporateUI.Muted, anchor: TextAnchor.MiddleCenter);
            if (CorporateUI.Button(new Rect(Mathf.Max(0f, rect.center.x - 65f), 81f, 130f, 32f), BrowserText("ResetFilters"), id: ordering ? "orders/reset" : "trade/reset"))
            {
                if (ordering) { orderSearch = ""; orderCategory = 0; } else { tradeSearch = ""; tradeCategory = 0; }
                CatalogChanged(ordering);
            }
        }

        private static void DrawCatalogWorking(Rect rect)
        {
            CorporateUI.Label(new Rect(10f, 16f, rect.width - 20f, 65f), BrowserText("UpdatingCatalog"),
                color: CorporateUI.Muted, anchor: TextAnchor.MiddleCenter);
        }

        private static void DrawDetailEmpty(Rect rect)
        {
            CorporateUI.Label(new Rect(0f, rect.height * 0.36f, rect.width, 65f), BrowserText("SelectProduct"), color: CorporateUI.Muted, anchor: TextAnchor.MiddleCenter);
        }

        private void DrawProductHeading(Rect rect, Thing thing, string id)
        {
            CorporateUI.ThingIcon(new Rect(0f, 0f, 65f, 65f), thing);
            CatalogText(new Rect(77f, 0f, rect.width - 113f, 68f), thing.LabelCap, GameFont.Small);
            Rect info = new Rect(rect.width - 29f, 0f, 29f, 29f);
            if (CorporateUI.Button(info, "i", id: id)) MugirlGameUtility.Windows.Add(new Dialog_InfoCard(thing));
            TooltipHandler.TipRegion(info, BrowserText("ProductDetails"));
            string classification = BrowserText(network.IsSpecialOrder(thing.def) ? "SpecialSourcing" : network.IsRareOrder(thing.def) ? "RareSourcing" : "StandardSourcing");
            CatalogCaption(new Rect(0f, 71f, rect.width, 20f), classification);
        }

        private static void DrawCatalogMetric(Rect rect, string label, string value)
        {
            CorporateUI.Label(new Rect(rect.x, rect.y, rect.width, 20f), label, GameFont.Tiny, CorporateUI.Muted);
            CatalogText(new Rect(rect.x, rect.y + 22f, rect.width, 26f), value, GameFont.Small);
        }

        private static void CatalogCaption(Rect rect, string text, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            CatalogText(rect, text, GameFont.Tiny, anchor, CorporateUI.Muted);
        }

        private static void CatalogText(Rect rect, string text, GameFont font, TextAnchor anchor = TextAnchor.UpperLeft, Color? color = null)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            string shown = text ?? "";
            GameFont previousFont = Text.Font;
            try
            {
                Text.Font = font;
                if (Text.CalcHeight(shown, rect.width) > rect.height)
                {
                    int length = shown.Length;
                    while (length > 0 && Text.CalcHeight(shown.Substring(0, length) + "…", rect.width) > rect.height) length--;
                    shown = shown.Substring(0, length) + "…";
                }
            }
            finally { Text.Font = previousFont; }
            CorporateUI.Label(rect, shown, font, color, anchor);
            if (shown != text) TooltipHandler.TipRegion(rect, text);
        }

        private void RefreshOrderPreview()
        {
            CorporateNetwork.DiscardUnspawnedProduct(orderPreview);
            orderPreview = null;
            if (!network.IsOrderable(orderDef)) return;
            using (Rand.Block(Gen.HashCombineInt(orderDef.shortHash, (orderStuff?.shortHash ?? 0) * 7 + (int)orderQuality)))
            {
                try { orderPreview = CorporateNetwork.MakeProduct(orderDef, orderStuff, orderQuality); }
                catch (Exception ex) { CorporateFeedback(false, "Mugirl.Corporate.GenerationError".Translate(orderDef.LabelCap, ex.Message)); }
            }
        }

        public override void PostClose()
        {
            CorporateNetwork.DiscardUnspawnedProduct(orderPreview);
            orderPreview = null;
            tradeSelectionMotion.Clear();
            orderSelectionMotion.Clear();
            orderViewTask?.Dispose();
            orderViewTask = null;
            catalogPrices.Clear();
            catalogMarketValues.Clear();
            orderEntries.Clear();
            orderEntryByDef.Clear();
            orderView.Clear();
            orderCatalogSource = null;
            base.PostClose();
        }

        private void DrawOrderReceipts(Rect rect)
        {
            List<CorporateOrder> receipts = network.Orders.Reverse().Take(100).ToList();
            Rect view = new Rect(0f, 0f, rect.width - 18f, Math.Max(rect.height, receipts.Count * 126f));
            CorporateUI.BeginScrollView(rect, ref orderScroll, view, "orders/receipts-scroll");
            try
            {
                for (int i = 0; i < receipts.Count; i++)
                {
                    float y = i * 126f;
                    if (y + 118f < orderScroll.y || y > orderScroll.y + rect.height) continue;
                    CorporateOrder order = receipts[i];
                    CorporateUI.Panel(new Rect(0f, y, view.width, 118f));
                    DrawCatalogIcon(new Rect(10f, y + 14f, 42f, 42f), order.def);
                    CatalogText(new Rect(63f, y + 10f, view.width - 203f, 45f),
                        "#" + order.id + "  " + (order.def?.LabelCap.ToString() ?? order.savedLabel) + " ×" + order.quantity, GameFont.Small);
                    string state = OrderStateKey(order.state).Translate();
                    string timing = order.state == CorporateOrderState.Preparing
                        ? BrowserText("RemainingDays", Mathf.Max(0f, (order.readyTick - CorporateNetwork.Now) / 60000f).ToString("0.0")) : "";
                    CatalogCaption(new Rect(10f, y + 66f, view.width - 150f, 20f), state + (timing.Length > 0 ? " · " + timing : ""));
                    CatalogCaption(new Rect(10f, y + 90f, view.width - 150f, 20f), BrowserText("Paid", CorporateUI.Money(order.paid)));
                    if (order.state == CorporateOrderState.Preparing)
                    {
                        float progress = Mathf.Clamp01((CorporateNetwork.Now - order.createdTick) / (float)Math.Max(1, order.readyTick - order.createdTick));
                        CorporateUI.Fill(new Rect(10f, y + 58f, view.width - 150f, 2f), CorporateUI.Border);
                        CorporateUI.Fill(new Rect(10f, y + 58f, (view.width - 150f) * progress, 2f), CorporateUI.Muted);
                    }
                    if (CorporateUI.Button(new Rect(view.width - 122f, y + 12f, 112f, 32f), "Mugirl.Corporate.Claim".Translate(),
                        order.state == CorporateOrderState.Ready || order.state == CorporateOrderState.RefundDue, true, "orders/claim/" + order.id))
                    { bool ok = network.ClaimOrder(context, order, out string why); CorporateFeedback(ok, why); }
                    if (order.state == CorporateOrderState.Preparing && CorporateUI.Button(new Rect(view.width - 122f, y + 68f, 112f, 32f),
                        "Mugirl.Corporate.CancelOrder".Translate(), CorporateNetwork.Now - order.createdTick <= network.TradeSettings.cancelWindowDays * CorporateNetwork.DayTicks, id: "orders/cancel/" + order.id))
                    {
                        Confirm(BrowserText("ConfirmCancellation", order.id, Mathf.RoundToInt(network.TradeSettings.cancelRefundFactor * 100f)), () =>
                        { bool ok = network.CancelOrder(context, order, out string why); CorporateFeedback(ok, why); }, true);
                    }
                }
                if (receipts.Count == 0) CorporateUI.Label(new Rect(8f, 8f, view.width - 16f, 80f), "Mugirl.Corporate.NoOrders".Translate(), color: CorporateUI.Muted);
            }
            finally { CorporateUI.EndScrollView(); }
        }

        private static string OrderStateKey(CorporateOrderState state)
        {
            switch (state)
            {
                case CorporateOrderState.Ready: return "Mugirl.Corporate.OrderState.Ready";
                case CorporateOrderState.Completed: return "Mugirl.Corporate.OrderState.Completed";
                case CorporateOrderState.Cancelled: return "Mugirl.Corporate.OrderState.Cancelled";
                case CorporateOrderState.RefundDue: return "Mugirl.Corporate.OrderState.RefundDue";
                case CorporateOrderState.Refunded: return "Mugirl.Corporate.OrderState.Refunded";
                default: return "Mugirl.Corporate.OrderState.Preparing";
            }
        }
    }
}
