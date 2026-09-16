using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public partial class Window_CorporateComms : CorporateAnimatedWindow
    {
        private readonly CorporateTradeContext context;
        private CorporateNetwork network => CorporateNetwork.Current;
        private int selectedPage;
        private int requestedPage;
        private Vector2 overviewScroll;
        private Vector2 introductionScroll;
        private Vector2 historyScroll;
        private Action pendingContent;
        private float contentStarted = -1f;
        private bool contentCommitted;
        private float contentFrom = 1f;
        private float navigationMarker;
        private bool clearFocus;
        private string feedback;
        private bool feedbackSuccess;
        private float feedbackAt = -100f;
        private const float ContentOut = 0.12f;
        private const float ContentIn = 0.23f;

        // StaticCacheLifecycle: immutable translation keys, no game objects or animation state.
        private static readonly string[] PageKeys =
        {
            "Mugirl.CorporateUI.Page.Overview", "Mugirl.CorporateUI.Page.Trade", "Mugirl.CorporateUI.Page.Orders",
            "Mugirl.CorporateUI.Page.People", "Mugirl.CorporateUI.Page.Finance", "Mugirl.CorporateUI.Page.Missions",
            "Mugirl.CorporateUI.Page.Introduction", "Mugirl.CorporateUI.Page.History"
        };
        private static readonly string[] SummaryKeys =
        {
            string.Empty, "Mugirl.CorporateUI.Summary.Trade", "Mugirl.CorporateUI.Summary.Orders",
            "Mugirl.CorporateUI.Summary.People", "Mugirl.CorporateUI.Summary.Finance", "Mugirl.CorporateUI.Summary.Missions"
        };

        public override Vector2 InitialSize => new Vector2(Mathf.Min(1180f, UI.screenWidth - 32f), Mathf.Min(800f, UI.screenHeight - 32f));
        protected override float Margin => 0f;

        public Window_CorporateComms(CorporateTradeContext context)
        {
            this.context = context;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnCancel = true;
            doCloseX = false;
            draggable = true;
            doWindowBackground = false;
            drawShadow = false;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            network?.EnsureWeeklyOffers();
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            navigationMarker = CorporateMotion.Approach(navigationMarker, requestedPage * 47f, 17f, Time.unscaledDeltaTime);
            if (contentStarted < 0f || Closing) return;
            float elapsed = Time.realtimeSinceStartup - contentStarted;
            if (!contentCommitted && elapsed >= ContentOut)
            {
                contentCommitted = true;
                Action apply = pendingContent;
                pendingContent = null;
                apply?.Invoke();
                clearFocus = true;
            }
            if (elapsed >= ContentOut + ContentIn) contentStarted = -1f;
        }

        private static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        private float ContentVisibility
        {
            get
            {
                if (contentStarted < 0f) return 1f;
                float elapsed = Time.realtimeSinceStartup - contentStarted;
                return contentCommitted ? Ease((elapsed - ContentOut) / ContentIn) : contentFrom * (1f - Ease(elapsed / ContentOut));
            }
        }

        internal void RequestPage(int page)
        {
            page = Mathf.Clamp(page, 0, PageKeys.Length - 1);
            if (page == requestedPage && (pendingContent != null || selectedPage == page)) return;
            requestedPage = page;
            RequestContentTransition(() => selectedPage = page, "page");
        }

        private void RequestContentTransition(Action change, string scope)
        {
            if (Closing || change == null) return;
            pendingContent = change;
            if (contentStarted < 0f || contentCommitted)
            {
                contentFrom = ContentVisibility;
                contentStarted = Time.realtimeSinceStartup;
            }
            contentCommitted = false;
            clearFocus = true;
        }

        private void ShowMenu(List<FloatMenuOption> options)
        {
            MugirlGameUtility.Windows.Add(new CorporateChoiceWindow(options));
        }

        private void Confirm(string text, Action action, bool destructive = false)
        {
            MugirlGameUtility.Windows.Add(new CorporateConfirmWindow(text, action, destructive));
        }

        private void ShowFeedback(bool success, string text)
        {
            feedback = text ?? "Mugirl.Corporate.GoodsChanged".Translate().ToString();
            feedbackSuccess = success;
            feedbackAt = Time.realtimeSinceStartup;
        }

        protected override void DrawContents(Rect inRect)
        {
            if (clearFocus) { GUI.FocusControl(null); clearFocus = false; }
            CorporateUI.Fill(inRect, CorporateUI.Background);
            CorporateUI.DrawGeometry(inRect, 0.42f);
            CorporateUI.Rule(new Rect(0f, 0f, inRect.width, 1f));
            Rect client = inRect.ContractedBy(22f);
            DrawHeader(client);
            Rect body = new Rect(client.x, client.y + 97f, client.width, client.height - 135f);
            DrawNavigation(ref body);
            CorporateUI.Fill(body, CorporateUI.Surface);
            Rect breadcrumb = new Rect(body.x + 18f, body.y + 10f, body.width - 36f, 25f);
            CorporateUI.Label(breadcrumb, (selectedPage + 1).ToString("00") + "  /  " + PageKeys[selectedPage].Translate(), GameFont.Tiny, CorporateUI.Muted);
            CorporateUI.Rule(new Rect(body.x + 18f, body.y + 39f, body.width - 36f, 1f));
            Rect page = new Rect(body.x + 18f, body.y + 54f, body.width - 36f, body.height - 72f);
            float opacity = ContentVisibility;
            float slide = (1f - opacity) * (contentCommitted ? 13f : -9f);
            bool enabled = GUI.enabled;
            GUI.BeginGroup(page);
            try
            {
                GUI.BeginGroup(new Rect(0f, slide, page.width, page.height));
                try
                {
                    using (CorporateUI.BeginFrame(motion, "page/" + selectedPage, opacity, contentStarted < 0f))
                    {
                        Rect local = new Rect(0f, 0f, page.width, page.height);
                        if (network == null || context == null || !context.IsValid)
                            CorporateUI.Notice(local, "Mugirl.CorporateUI.ConnectionLost".Translate(), true);
                        else if (CorporateIntroduction.Current?.Completed != true)
                            CorporateUI.Notice(local, "Mugirl.CorporateUI.Locked".Translate(), true);
                        else DrawPage(local);
                    }
                }
                finally { GUI.EndGroup(); }
            }
            finally { GUI.EndGroup(); GUI.enabled = enabled; }
            CorporateUI.Rule(new Rect(client.x, client.yMax - 26f, client.width, 1f));
            CorporateUI.Label(new Rect(client.x, client.yMax - 20f, client.width - 150f, 22f),
                "Mugirl.CorporateUI.Context".Translate(context?.Label ?? "-"), GameFont.Tiny, CorporateUI.Muted);
            CorporateUI.Label(new Rect(client.xMax - 148f, client.yMax - 20f, 148f, 22f),
                "Mugirl.CorporateUI.PrivateChannel".Translate(), GameFont.Tiny, CorporateUI.Muted, TextAnchor.MiddleRight);
            DrawFeedback(inRect);
        }

        private void DrawPage(Rect rect)
        {
            switch (selectedPage)
            {
                case 1: DrawTrade(rect); break;
                case 2: DrawOrders(rect); break;
                case 3: DrawPeople(rect); break;
                case 4: DrawFinance(rect); break;
                case 5: DrawMissions(rect); break;
                case 6: DrawIntroduction(rect); break;
                case 7: DrawHistory(rect); break;
                default: DrawOverview(rect); break;
            }
        }

        private void DrawHeader(Rect client)
        {
            CorporateUI.Label(new Rect(client.x, client.y, client.width - 52f, 18f),
                "M E G A C O R P   /   N E T W O R K", GameFont.Tiny, CorporateUI.Muted);
            CorporateUI.Label(new Rect(client.x, client.y + 23f, client.width - 65f, 36f),
                "Mugirl.CorporateUI.Title".Translate(), GameFont.Medium);
            int refresh = network == null ? 0 : Mathf.Max(0, network.NextRefreshTick - CorporateNetwork.Now);
            CorporateUI.Label(new Rect(client.x, client.y + 62f, client.width - 52f, 25f),
                "Mugirl.CorporateUI.HeaderStatus".Translate(CorporateUI.Money(context?.SilverCount ?? 0), (refresh / 60000f).ToString("0.0")),
                GameFont.Tiny, CorporateUI.Muted);
            if (CorporateUI.Button(new Rect(client.xMax - 38f, client.y + 5f, 38f, 38f), "×", id: "close")) Close();
            TooltipHandler.TipRegion(new Rect(client.xMax - 38f, client.y + 5f, 38f, 38f), "Mugirl.CorporateUI.CloseHint".Translate());
        }

        private void DrawNavigation(ref Rect body)
        {
            if (body.width < 850f || body.height < 455f)
            {
                if (CorporateUI.Button(new Rect(body.x, body.y, body.width, 35f),
                    (requestedPage + 1).ToString("00") + "  /  " + PageKeys[requestedPage].Translate() + "  ▾", id: "navigation-menu"))
                {
                    var options = new List<FloatMenuOption>();
                    for (int i = 0; i < PageKeys.Length; i++)
                    {
                        int index = i;
                        options.Add(new FloatMenuOption(PageKeys[i].Translate(), () => RequestPage(index)));
                    }
                    ShowMenu(options);
                }
                body.yMin += 47f;
                return;
            }
            const float width = 180f;
            CorporateUI.Label(new Rect(body.x, body.y + 2f, width, 22f), "Mugirl.CorporateUI.Services".Translate(), GameFont.Tiny, CorporateUI.Muted);
            for (int i = 0; i < PageKeys.Length; i++)
            {
                Rect row = new Rect(body.x + 8f, body.y + 36f + i * 47f, width - 8f, 39f);
                if (CorporateUI.Button(row, (i + 1).ToString("00") + "   " + PageKeys[i].Translate(), primary: i == requestedPage, id: "nav/" + i)) RequestPage(i);
            }
            float marker = navigationMarker;
            CorporateUI.Fill(new Rect(body.x, body.y + 45f + marker, 2f, 21f), CorporateUI.Ink);
            CorporateUI.Label(new Rect(body.x + 8f, body.yMax - 56f, width - 10f, 52f), "Mugirl.CorporateUI.Motto".Translate(), GameFont.Tiny, CorporateUI.Muted);
            body.xMin += width + 20f;
        }

        private void DrawOverview(Rect rect)
        {
            float width = rect.width - 18f;
            bool hostile = network.CorporateFaction?.HostileTo(MugirlWildSlaveUtility.PlayerFaction) == true;
            string welcome = "Mugirl.CorporateUI.WelcomeText".Translate();
            string notice = (hostile ? "Mugirl.CorporateUI.Hostile" : "Mugirl.CorporateUI.AccountReady").Translate();
            float welcomeHeight = Mathf.Max(24f, Text.CalcHeight(welcome, width - 30f));
            float noticeHeight = Mathf.Max(56f, Text.CalcHeight(notice, width - 20f) + 22f);
            int columns = width >= 630f ? 2 : 1;
            float cardWidth = (width - (columns - 1) * 12f) / columns;
            float cardHeight = 116f;
            for (int i = 1; i <= 5; i++) cardHeight = Mathf.Max(cardHeight, Text.CalcHeight(SummaryKeys[i].Translate(), cardWidth - 28f) + 82f);
            float heroHeight = welcomeHeight + 60f;
            float contentHeight = heroHeight + 18f + 78f + 18f + noticeHeight + 22f + Mathf.Ceil(5f / columns) * (cardHeight + 12f);
            Rect view = new Rect(0f, 0f, width, Mathf.Max(rect.height, contentHeight));
            CorporateUI.BeginScrollView(rect, ref overviewScroll, view, "overview");
            try
            {
                CorporateUI.DrawGeometry(new Rect(width * 0.7f, 0f, width * 0.3f, heroHeight), 0.16f);
                CorporateUI.Label(new Rect(15f, 13f, width - 30f, 35f), "Mugirl.CorporateUI.Welcome".Translate(), GameFont.Medium);
                CorporateUI.Label(new Rect(15f, 51f, width - 30f, welcomeHeight), welcome, color: CorporateUI.Muted);
                float y = heroHeight + 18f;
                float metricWidth = (width - 24f) / 3f;
                DrawMetric(new Rect(0f, y, metricWidth, 78f), "Mugirl.CorporateUI.MetricStock".Translate(), network.Stock.Count.ToString("00"));
                DrawMetric(new Rect(metricWidth + 12f, y, metricWidth, 78f), "Mugirl.CorporateUI.MetricContracts".Translate(), network.ActiveMissionCount.ToString("00"));
                DrawMetric(new Rect((metricWidth + 12f) * 2f, y, metricWidth, 78f), "Mugirl.CorporateUI.MetricRecords".Translate(), network.Records.Count.ToString("00"));
                y += 96f;
                CorporateUI.Notice(new Rect(0f, y, width, noticeHeight), notice, hostile);
                y += noticeHeight + 22f;
                for (int i = 1; i <= 5; i++)
                {
                    int index = i - 1;
                    Rect card = new Rect((index % columns) * (cardWidth + 12f), y + (index / columns) * (cardHeight + 12f), cardWidth, cardHeight);
                    if (CorporateUI.Row(card, false, "service/" + i)) RequestPage(i);
                    CorporateUI.Label(new Rect(card.x + 14f, card.y + 11f, card.width - 28f, 27f),
                        (i + 1).ToString("00") + "  /  " + PageKeys[i].Translate());
                    CorporateUI.Label(new Rect(card.x + 14f, card.y + 47f, card.width - 28f, cardHeight - 83f), SummaryKeys[i].Translate(), color: CorporateUI.Muted);
                    CorporateUI.Label(new Rect(card.x + 14f, card.yMax - 28f, card.width - 28f, 22f),
                        "Mugirl.CorporateUI.OpenService".Translate() + "  →", GameFont.Tiny, CorporateUI.Ink, TextAnchor.MiddleRight);
                }
            }
            finally { CorporateUI.EndScrollView(); }
        }

        private static void DrawMetric(Rect rect, string label, string value)
        {
            CorporateUI.Rule(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f));
            CorporateUI.Label(new Rect(rect.x + 12f, rect.y + 7f, rect.width - 24f, 22f), label, GameFont.Tiny, CorporateUI.Muted);
            CorporateUI.Label(new Rect(rect.x + 12f, rect.y + 31f, rect.width - 24f, 37f), value, GameFont.Medium);
        }

        private void DrawFeedback(Rect rect)
        {
            float age = Time.realtimeSinceStartup - feedbackAt;
            if (feedback == null || age > 4.2f) return;
            float visibility = Ease(age / 0.2f) * (1f - Ease((age - 3.85f) / 0.35f));
            float width = Mathf.Min(560f, rect.width - 60f);
            float height = Mathf.Clamp(Text.CalcHeight(feedback, width - 58f) + 30f, 54f, 140f);
            Rect toast = new Rect((rect.width - width) / 2f, rect.height - height - 42f + (1f - visibility) * 14f, width, height);
            using (CorporateUI.BeginFrame(motion, "feedback", visibility))
            {
                CorporateUI.Panel(toast);
                CorporateUI.Label(new Rect(toast.x + 13f, toast.y + 14f, 22f, 24f), feedbackSuccess ? "+" : "!", color: CorporateUI.Ink);
                CorporateUI.Label(new Rect(toast.x + 40f, toast.y + 13f, toast.width - 56f, toast.height - 22f), feedback);
                CorporateUI.Rule(new Rect(toast.x, toast.yMax - 2f, toast.width * Mathf.Clamp01(1f - age / 4.2f), 2f));
            }
        }

        private void DrawIntroduction(Rect rect)
        {
            string body = "Mugirl.CorporateIntro.ChapterCompleted".Translate() + "\n\n" + "Mugirl.CorporateIntro.ComingSoon".Translate();
            Rect view = new Rect(0f, 0f, rect.width - 18f, Mathf.Max(rect.height, Text.CalcHeight(body, rect.width - 18f) + 140f));
            CorporateUI.BeginScrollView(rect, ref introductionScroll, view, "introduction");
            try
            {
                float y = 0f;
                CorporateUI.Heading(ref y, view.width, "Mugirl.CorporateIntro.Title".Translate(), "Mugirl.CorporateIntro.CompletedStatus".Translate());
                CorporateUI.Label(new Rect(0f, y, view.width, view.height - y), body);
            }
            finally { CorporateUI.EndScrollView(); }
        }

        private void DrawHistory(Rect rect)
        {
            float y = 0f;
            CorporateUI.Heading(ref y, rect.width, "Mugirl.CorporateUI.Page.History".Translate(), "Mugirl.CorporateUI.HistoryHint".Translate());
            Rect viewport = new Rect(0f, y, rect.width, Mathf.Max(1f, rect.height - y));
            float width = rect.width - 18f;
            float total = 0f;
            foreach (CorporateRecord record in network.Records)
                total += Mathf.Max(28f, Text.CalcHeight(RecordTitle(record), width - 28f)) + Mathf.Max(25f, Text.CalcHeight(record.detail ?? "", width - 28f)) + 34f;
            Rect view = new Rect(0f, 0f, width, Mathf.Max(viewport.height, total));
            CorporateUI.BeginScrollView(viewport, ref historyScroll, view, "history");
            try
            {
                if (network.Records.Count == 0) CorporateUI.Notice(new Rect(0f, 0f, view.width, 76f), "Mugirl.CorporateUI.NoHistory".Translate());
                float top = 0f;
                for (int i = network.Records.Count - 1; i >= 0; i--)
                {
                    CorporateRecord record = network.Records[i];
                    string title = RecordTitle(record);
                    float titleHeight = Mathf.Max(28f, Text.CalcHeight(title, width - 28f));
                    float detailHeight = Mathf.Max(25f, Text.CalcHeight(record.detail ?? "", width - 28f));
                    CorporateUI.Panel(new Rect(0f, top, width, titleHeight + detailHeight + 24f));
                    CorporateUI.Label(new Rect(14f, top + 9f, width - 28f, titleHeight), title);
                    CorporateUI.Label(new Rect(14f, top + titleHeight + 14f, width - 28f, detailHeight), record.detail, color: CorporateUI.Muted);
                    top += titleHeight + detailHeight + 34f;
                }
            }
            finally { CorporateUI.EndScrollView(); }
        }

        private static string RecordTitle(CorporateRecord record)
        {
            return "Mugirl.CorporateUI.RecordTitle".Translate((record.tick / 60000f).ToString("0.0"), record.key.Translate(), CorporateUI.Money(record.amount));
        }
    }
}
