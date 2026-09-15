using System;
using System.Collections.Generic;
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
        private GUIStyle tradeInputStyle;

        private string CorporateInput(Rect rect, string value, string placeholder = null)
        {
            if (tradeInputStyle == null)
            {
                tradeInputStyle = new GUIStyle(GUI.skin.textField);
                tradeInputStyle.normal.textColor = CorporateUI.Ink;
                tradeInputStyle.focused.textColor = CorporateUI.Ink;
                tradeInputStyle.hover.textColor = CorporateUI.Ink;
                tradeInputStyle.active.textColor = CorporateUI.Ink;
                tradeInputStyle.normal.background = Texture2D.whiteTexture;
                tradeInputStyle.focused.background = Texture2D.whiteTexture;
                tradeInputStyle.hover.background = Texture2D.whiteTexture;
                tradeInputStyle.active.background = Texture2D.whiteTexture;
                tradeInputStyle.padding = new RectOffset(8, 8, 5, 5);
            }
            CorporateUI.Panel(rect);
            if (placeholder != null) GUI.SetNextControlName("CorporateGoodsSearch");
            string result = GUI.TextField(rect.ContractedBy(1f), value, 100, tradeInputStyle);
            if (placeholder != null && result.Length == 0 && GUI.GetNameOfFocusedControl() != "CorporateGoodsSearch")
                CorporateUI.Label(rect.ContractedBy(8f, 3f), placeholder, color: CorporateUI.Muted, anchor: TextAnchor.MiddleLeft);
            return result;
        }

        private void CorporateQuantity(Rect rect, ref int quantity, ref string buffer, int maximum)
        {
            if (CorporateUI.Button(new Rect(rect.x, rect.y, 30f, rect.height), "−", quantity > 1))
            { quantity--; buffer = quantity.ToString(); }
            buffer = CorporateInput(new Rect(rect.x + 36f, rect.y, rect.width - 148f, rect.height), buffer);
            if (int.TryParse(buffer, out int parsed)) quantity = Mathf.Clamp(parsed, 1, Math.Max(1, maximum));
            if (CorporateUI.Button(new Rect(rect.xMax - 106f, rect.y, 30f, rect.height), "+", quantity < maximum))
            { quantity++; buffer = quantity.ToString(); }
            if (CorporateUI.Button(new Rect(rect.xMax - 70f, rect.y, 70f, rect.height), "Mugirl.Corporate.Maximum".Translate(), maximum > 0))
            { quantity = maximum; buffer = quantity.ToString(); }
        }

        private static void CorporateFeedback(bool success, string reason)
        {
            Messages.Message(success ? "Mugirl.Corporate.Success".Translate().ToString() : reason ?? "Mugirl.Corporate.GoodsChanged".Translate().ToString(),
                success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, historical: false);
        }

        private void DrawTrade(Rect rect)
        {
            if (rect.height >= 500f) { DrawTradeBody(rect); return; }
            Rect view = new Rect(0f, 0f, rect.width - 18f, 510f);
            Widgets.BeginScrollView(rect, ref tradePageScroll, view);
            DrawTradeBody(view);
            Widgets.EndScrollView();
        }

        private void DrawTradeBody(Rect rect)
        {
            float w = rect.width;
            if (CorporateUI.Button(new Rect(0f, 0f, w / 2f - 4f, 34f), "Mugirl.Corporate.WeeklyStock".Translate(), primary: !productSelling))
            { productSelling = false; tradeScroll = Vector2.zero; }
            if (CorporateUI.Button(new Rect(w / 2f + 4f, 0f, w / 2f - 4f, 34f), "Mugirl.Corporate.ProductPurchase".Translate(), primary: productSelling))
            { productSelling = true; tradeScroll = Vector2.zero; }
            CorporateUI.Label(new Rect(0f, 42f, w, 45f),
                (productSelling ? "Mugirl.Corporate.ProductHint" : "Mugirl.Corporate.StockHint").Translate(), color: CorporateUI.Muted);
            tradeSearch = CorporateInput(new Rect(0f, 92f, w, 31f), tradeSearch, "Mugirl.Corporate.SearchGoods".Translate());
            List<Thing> products = productSelling ? context.AvailableContractThings.Where(t => network.ProductFactor(t) > 0f
                && t.Label.IndexOf(tradeSearch, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList() : null;
            List<CorporateStock> offers = productSelling ? null : network.Stock.Where(s => s.sample != null && !s.sample.Destroyed
                && s.sample.Label.IndexOf(tradeSearch, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            int count = productSelling ? products.Count : offers.Count;
            Rect listRect = new Rect(0f, 132f, w, Math.Max(64f, rect.height - 290f));
            Rect view = new Rect(0f, 0f, w - 18f, Math.Max(listRect.height, count * 58f));
            Widgets.BeginScrollView(listRect, ref tradeScroll, view);
            for (int i = 0; i < count; i++)
            {
                if (i * 58f + 58f < tradeScroll.y || i * 58f > tradeScroll.y + listRect.height) continue;
                Thing thing = productSelling ? products[i] : offers[i].sample;
                int quantity = productSelling ? thing.stackCount : offers[i].count;
                int price = productSelling ? network.ProductQuote(thing, 1) : offers[i].unitPrice;
                Rect row = new Rect(0f, i * 58f, view.width, 52f);
                bool selected = productSelling ? thing == selectedProduct : offers[i] == selectedStock;
                Widgets.DrawBoxSolid(row, selected ? CorporateUI.Raised : CorporateUI.Surface);
                Widgets.ThingIcon(new Rect(7f, row.y + 5f, 38f, 38f), thing);
                CorporateUI.Label(new Rect(52f, row.y + 3f, view.width - 175f, 46f), thing.LabelCap);
                CorporateUI.Label(new Rect(view.width - 118f, row.y + 3f, 113f, 46f),
                    "Mugirl.Corporate.StockRow".Translate(price, quantity), anchor: TextAnchor.MiddleRight);
                TooltipHandler.TipRegion(row, thing.DescriptionDetailed);
                if (Widgets.ButtonInvisible(row))
                {
                    if (productSelling) selectedProduct = thing; else selectedStock = offers[i];
                    tradeQuantity = 1; tradeQuantityText = "1";
                }
            }
            if (count == 0) CorporateUI.Label(new Rect(8f, 8f, view.width - 16f, 80f), "Mugirl.Corporate.EmptyGoods".Translate(), color: CorporateUI.Muted);
            Widgets.EndScrollView();

            float y = listRect.yMax + 8f;
            Thing selectedThing = productSelling ? selectedProduct : selectedStock?.sample;
            bool valid = selectedThing != null && !selectedThing.Destroyed && (productSelling
                ? products.Contains(selectedThing) : selectedStock != null && network.Stock.Contains(selectedStock));
            if (!valid)
            {
                CorporateUI.Notice(new Rect(0f, y, w, Math.Max(70f, rect.height - y)), "Mugirl.Corporate.SelectGoods".Translate());
                return;
            }
            int available = productSelling ? selectedThing.stackCount : selectedStock.count;
            if (!productSelling) available = Math.Min(available, context.SilverCount / Math.Max(1, selectedStock.unitPrice));
            tradeQuantity = Mathf.Clamp(tradeQuantity, 1, Math.Max(1, available));
            CorporateUI.Label(new Rect(0f, y, w - 40f, 29f), selectedThing.LabelCap);
            if (CorporateUI.Button(new Rect(w - 34f, y, 34f, 27f), "i")) MugirlGameUtility.Windows.Add(new Dialog_InfoCard(selectedThing));
            y += 33f;
            CorporateQuantity(new Rect(0f, y, Mathf.Min(290f, w * 0.6f), 31f), ref tradeQuantity, ref tradeQuantityText, available);
            int total = productSelling ? network.ProductQuote(selectedThing, tradeQuantity) : CorporateNetwork.Price(selectedStock.unitPrice, tradeQuantity);
            CorporateUI.Label(new Rect(w * 0.61f, y, w * 0.39f, 32f), CorporateUI.Money(total), anchor: TextAnchor.MiddleRight);
            y += 38f;
            bool permitted = network.CanTrade(context, out string permissionReason);
            if (CorporateUI.Button(new Rect(w - 180f, y, 180f, 35f),
                (productSelling ? "Mugirl.Corporate.ConfirmSale" : "Mugirl.Corporate.ConfirmPurchase").Translate(),
                permitted && available > 0 && total > 0, true))
            {
                string reason;
                bool ok = productSelling ? network.SellProduct(context, selectedThing, tradeQuantity, out reason)
                    : network.BuyStock(context, selectedStock, tradeQuantity, out reason);
                CorporateFeedback(ok, reason);
            }
            CorporateUI.Label(new Rect(0f, y, w - 190f, 44f), permissionReason ??
                (context.Caravan != null ? "Mugirl.Corporate.CaravanWeight".Translate(context.IncomingMass(selectedThing, tradeQuantity).ToString("0.0"))
                    : "Mugirl.Corporate.BeaconHint".Translate()), GameFont.Tiny, CorporateUI.Muted);
        }

        private void DrawOrders(Rect rect)
        {
            if (rect.height >= 500f) { DrawOrdersBody(rect); return; }
            Rect view = new Rect(0f, 0f, rect.width - 18f, 510f);
            Widgets.BeginScrollView(rect, ref orderPageScroll, view);
            DrawOrdersBody(view);
            Widgets.EndScrollView();
        }

        private void DrawOrdersBody(Rect rect)
        {
            float w = rect.width;
            if (CorporateUI.Button(new Rect(0f, 0f, w / 2f - 4f, 34f), "Mugirl.Corporate.OrderCatalog".Translate(), primary: !showExistingOrders)) showExistingOrders = false;
            if (CorporateUI.Button(new Rect(w / 2f + 4f, 0f, w / 2f - 4f, 34f), "Mugirl.Corporate.MyOrders".Translate(), primary: showExistingOrders)) showExistingOrders = true;
            if (showExistingOrders) { DrawOrderReceipts(new Rect(0f, 43f, w, rect.height - 43f)); return; }
            CorporateUI.Label(new Rect(0f, 43f, w, 46f), "Mugirl.Corporate.OrderHint".Translate(), color: CorporateUI.Muted);
            orderSearch = CorporateInput(new Rect(0f, 94f, w, 31f), orderSearch, "Mugirl.Corporate.SearchGoods".Translate());
            List<ThingDef> defs = network.OrderCatalog.Where(d => d.LabelCap.ToString().IndexOf(orderSearch, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            Rect list = new Rect(0f, 134f, w, Math.Max(65f, rect.height - 345f));
            Rect view = new Rect(0f, 0f, w - 18f, Math.Max(list.height, defs.Count * 35f));
            Widgets.BeginScrollView(list, ref orderScroll, view);
            for (int i = 0; i < defs.Count; i++)
            {
                if (i * 35f + 35f < orderScroll.y || i * 35f > orderScroll.y + list.height) continue;
                if (CorporateUI.Button(new Rect(0f, i * 35f, view.width, 31f), defs[i].LabelCap, primary: orderDef == defs[i]))
                {
                    orderDef = defs[i]; orderStuff = GenStuff.DefaultStuffFor(orderDef);
                    orderQuantity = 1; orderQuantityText = "1";
                    RefreshOrderPreview();
                }
            }
            Widgets.EndScrollView();
            float y = list.yMax + 8f;
            if (orderPreview == null) { CorporateUI.Notice(new Rect(0f, y, w, 80f), "Mugirl.Corporate.SelectGoods".Translate()); return; }
            CorporateUI.Label(new Rect(0f, y, w - 38f, 30f), orderPreview.LabelCap);
            if (CorporateUI.Button(new Rect(w - 34f, y, 34f, 27f), "i")) MugirlGameUtility.Windows.Add(new Dialog_InfoCard(orderPreview));
            y += 33f;
            float half = (w - 8f) / 2f;
            if (CorporateUI.Button(new Rect(0f, y, half, 30f), orderStuff?.LabelCap.ToString() ?? "Mugirl.Corporate.NoStuff".Translate(), orderDef.MadeFromStuff))
            {
                MugirlGameUtility.Windows.Add(new FloatMenu(GenStuff.AllowedStuffsFor(orderDef).Select(s => new FloatMenuOption(s.LabelCap,
                    () => { orderStuff = s; RefreshOrderPreview(); })).ToList()));
            }
            if (CorporateUI.Button(new Rect(half + 8f, y, half, 30f), orderQuality.GetLabel(), orderPreview.TryGetComp<CompQuality>() != null))
            {
                MugirlGameUtility.Windows.Add(new FloatMenu(Enumerable.Range(0, (int)QualityCategory.Excellent + 1).Select(q =>
                    new FloatMenuOption(((QualityCategory)q).GetLabel(), () => { orderQuality = (QualityCategory)q; RefreshOrderPreview(); })).ToList()));
            }
            y += 37f;
            CorporateQuantity(new Rect(0f, y, Mathf.Min(290f, w * 0.6f), 31f), ref orderQuantity, ref orderQuantityText, CorporateNetwork.MaxOrderQuantity(orderDef));
            int cost = network.OrderQuote(orderPreview, orderQuantity);
            CorporateUI.Label(new Rect(w * 0.61f, y, w * 0.39f, 32f), CorporateUI.Money(cost), anchor: TextAnchor.MiddleRight);
            y += 38f;
            IntRange days = network.IsRareOrder(orderDef) ? network.TradeSettings.rareOrderDays : network.TradeSettings.orderDays;
            bool canTrade = network.CanTrade(context, out string reason);
            CorporateUI.Label(new Rect(0f, y, w - 187f, 70f), reason ?? "Mugirl.Corporate.OrderTerms".Translate(days.min, days.max,
                Mathf.RoundToInt(network.TradeSettings.cancelRefundFactor * 100f)), GameFont.Tiny, CorporateUI.Muted);
            if (CorporateUI.Button(new Rect(w - 180f, y, 180f, 36f), "Mugirl.Corporate.PlaceOrder".Translate(),
                canTrade && cost > 0 && context.SilverCount >= cost, true))
            {
                bool success = network.PlaceOrder(context, orderDef, orderStuff, orderQuality, orderQuantity, out reason);
                CorporateFeedback(success, reason);
                if (success) showExistingOrders = true;
            }
        }

        private void RefreshOrderPreview()
        {
            orderPreview?.Destroy();
            orderPreview = null;
            try { orderPreview = CorporateNetwork.MakeProduct(orderDef, orderStuff, orderQuality); }
            catch (Exception ex) { CorporateFeedback(false, "Mugirl.Corporate.GenerationError".Translate(orderDef.LabelCap, ex.Message)); }
        }

        public override void PostClose()
        {
            orderPreview?.Destroy();
            orderPreview = null;
            base.PostClose();
        }

        private void DrawOrderReceipts(Rect rect)
        {
            List<CorporateOrder> receipts = network.Orders.Reverse().Take(100).ToList();
            Rect view = new Rect(0f, 0f, rect.width - 18f, Math.Max(rect.height, receipts.Count * 122f));
            Widgets.BeginScrollView(rect, ref orderScroll, view);
            for (int i = 0; i < receipts.Count; i++)
            {
                CorporateOrder order = receipts[i];
                float y = i * 122f;
                CorporateUI.Panel(new Rect(0f, y, view.width, 114f));
                CorporateUI.Label(new Rect(10f, y + 8f, view.width - 140f, 30f), "#" + order.id + "  " + (order.def?.LabelCap.ToString() ?? order.savedLabel) + " ×" + order.quantity);
                string state = OrderStateKey(order.state).Translate();
                CorporateUI.Label(new Rect(10f, y + 42f, view.width - 140f, 60f), state + "\n" +
                    "Mugirl.Corporate.OrderReceipt".Translate(CorporateUI.Money(order.paid), Mathf.Max(0f, (order.readyTick - CorporateNetwork.Now) / 60000f).ToString("0.0")),
                    GameFont.Tiny, CorporateUI.Muted);
                if (CorporateUI.Button(new Rect(view.width - 122f, y + 10f, 112f, 32f), "Mugirl.Corporate.Claim".Translate(),
                    order.state == CorporateOrderState.Ready || order.state == CorporateOrderState.RefundDue, true))
                { bool ok = network.ClaimOrder(context, order, out string why); CorporateFeedback(ok, why); }
                if (order.state == CorporateOrderState.Preparing && CorporateUI.Button(new Rect(view.width - 122f, y + 57f, 112f, 32f),
                    "Mugirl.Corporate.CancelOrder".Translate(), CorporateNetwork.Now - order.createdTick <= network.TradeSettings.cancelWindowDays * CorporateNetwork.DayTicks))
                { bool ok = network.CancelOrder(context, order, out string why); CorporateFeedback(ok, why); }
            }
            if (receipts.Count == 0) CorporateUI.Label(new Rect(8f, 8f, view.width - 16f, 80f), "Mugirl.Corporate.NoOrders".Translate(), color: CorporateUI.Muted);
            Widgets.EndScrollView();
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
