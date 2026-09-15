using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public partial class Window_CorporateComms : Window
    {
        private readonly CorporateTradeContext context;
        private CorporateNetwork network => CorporateNetwork.Current;
        private int selectedPage;
        private Vector2 overviewScroll;
        private Vector2 introductionScroll;
        private Vector2 historyScroll;

        private static readonly string[] PageKeys =
        {
            "Mugirl.CorporateUI.Page.Overview", "Mugirl.CorporateUI.Page.Trade", "Mugirl.CorporateUI.Page.Orders",
            "Mugirl.CorporateUI.Page.People", "Mugirl.CorporateUI.Page.Finance", "Mugirl.CorporateUI.Page.Missions",
            "Mugirl.CorporateUI.Page.Introduction", "Mugirl.CorporateUI.Page.History"
        };

        // StaticCacheLifecycle: immutable translation keys only; available for the process lifetime.
        private static readonly string[] SummaryKeys =
        {
            string.Empty, "Mugirl.CorporateUI.Summary.Trade", "Mugirl.CorporateUI.Summary.Orders",
            "Mugirl.CorporateUI.Summary.People", "Mugirl.CorporateUI.Summary.Finance", "Mugirl.CorporateUI.Summary.Missions"
        };

        public override Vector2 InitialSize => new Vector2(Mathf.Min(1120f, UI.screenWidth - 32f), Mathf.Min(760f, UI.screenHeight - 32f));
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
            drawShadow = true;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            network?.EnsureWeeklyOffers();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Color previous = GUI.color;
            GameFont font = Text.Font;
            TextAnchor anchor = Text.Anchor;
            try
            {
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.DrawBoxSolid(inRect, CorporateUI.Background);
                Rect client = inRect.ContractedBy(18f);
                DrawHeader(client);
                Rect body = new Rect(client.x, client.y + 88f, client.width, client.height - 132f);
                DrawNavigation(ref body);
                CorporateUI.Panel(body);
                Rect page = body.ContractedBy(16f);
                GUI.BeginGroup(page);
                try
                {
                    Rect local = new Rect(0f, 0f, page.width, page.height);
                    if (network == null || context == null || !context.IsValid)
                        CorporateUI.Notice(local, "Mugirl.CorporateUI.ConnectionLost".Translate(), true);
                    else if (CorporateIntroduction.Current?.Completed != true)
                        CorporateUI.Notice(local, "Mugirl.CorporateUI.Locked".Translate(), true);
                    else
                    {
                        switch (selectedPage)
                        {
                            case 1: DrawTrade(local); break;
                            case 2: DrawOrders(local); break;
                            case 3: DrawPeople(local); break;
                            case 4: DrawFinance(local); break;
                            case 5: DrawMissions(local); break;
                            case 6: DrawIntroduction(local); break;
                            case 7: DrawHistory(local); break;
                            default: DrawOverview(local); break;
                        }
                    }
                }
                finally { GUI.EndGroup(); }
                CorporateUI.Rule(new Rect(client.x, client.yMax - 33f, client.width, 1f));
                CorporateUI.Label(new Rect(client.x, client.yMax - 26f, client.width, 25f),
                    "Mugirl.CorporateUI.Context".Translate(context?.Label ?? "-"), GameFont.Tiny, CorporateUI.Muted);
            }
            finally { GUI.color = previous; Text.Font = font; Text.Anchor = anchor; }
        }

        private void DrawHeader(Rect client)
        {
            Widgets.DrawBoxSolid(new Rect(client.x, client.y + 4f, 5f, 54f), CorporateUI.Accent);
            float available = client.width - 68f;
            CorporateUI.Label(new Rect(client.x + 17f, client.y, available - 20f, 36f),
                "Mugirl.CorporateUI.Title".Translate(), GameFont.Medium);
            int refresh = network == null ? 0 : Mathf.Max(0, network.NextRefreshTick - CorporateNetwork.Now);
            CorporateUI.Label(new Rect(client.x + 17f, client.y + 40f, available - 20f, 26f),
                "Mugirl.CorporateUI.HeaderStatus".Translate(CorporateUI.Money(context?.SilverCount ?? 0), (refresh / 60000f).ToString("0.0")),
                GameFont.Tiny, CorporateUI.Muted);
            if (CorporateUI.Button(new Rect(client.xMax - 38f, client.y, 38f, 34f), "×")) Close();
            CorporateUI.Rule(new Rect(client.x, client.y + 75f, client.width, 1f));
        }

        private void DrawNavigation(ref Rect body)
        {
            if (body.width < 780f || body.height < 450f)
            {
                // 大比例 UI 或低分辨率下使用单个菜单，保留可读的业务正文。
                Rect selector = new Rect(body.x, body.y, body.width, 34f);
                Widgets.DrawBoxSolid(selector, CorporateUI.Raised);
                CorporateUI.Label(selector.ContractedBy(5f), PageKeys[selectedPage].Translate() + "  ▾", anchor: TextAnchor.MiddleLeft);
                if (Widgets.ButtonInvisible(selector))
                {
                    var options = new System.Collections.Generic.List<FloatMenuOption>();
                    for (int i = 0; i < PageKeys.Length; i++)
                    {
                        int pageIndex = i;
                        options.Add(new FloatMenuOption(PageKeys[i].Translate(), () => selectedPage = pageIndex));
                    }
                    MugirlGameUtility.Windows.Add(new FloatMenu(options));
                }
                body.yMin += 46f;
                return;
            }

            float navigationWidth = 166f;
            for (int i = 0; i < PageKeys.Length; i++)
            {
                Rect row = new Rect(body.x, body.y + i * 49f, navigationWidth, 41f);
                if (CorporateUI.Button(row, PageKeys[i].Translate(), primary: i == selectedPage)) selectedPage = i;
            }
            CorporateUI.Label(new Rect(body.x, body.yMax - 52f, navigationWidth, 50f), "Mugirl.CorporateUI.Motto".Translate(), GameFont.Tiny, CorporateUI.Muted);
            body.xMin += navigationWidth + 16f;
        }

        private void DrawOverview(Rect rect)
        {
            float width = rect.width - 18f;
            bool hostile = network.CorporateFaction?.HostileTo(MugirlWildSlaveUtility.PlayerFaction) == true;
            string welcome = "Mugirl.CorporateUI.WelcomeText".Translate();
            string notice = (hostile ? "Mugirl.CorporateUI.Hostile" : "Mugirl.CorporateUI.AccountReady").Translate();
            float noticeHeight = Mathf.Max(66f, Text.CalcHeight(notice, width - 20f) + 20f);
            float height = 40f + Mathf.Max(38f, Text.CalcHeight(welcome, width)) + 12f + noticeHeight + 16f;
            for (int i = 1; i <= 5; i++) height += Mathf.Max(48f, Text.CalcHeight(SummaryKeys[i].Translate(), width - 135f)) + 17f;
            Rect view = new Rect(0f, 0f, width, Mathf.Max(rect.height, height));
            Widgets.BeginScrollView(rect, ref overviewScroll, view);
            try
            {
                float y = 0f;
                CorporateUI.Heading(ref y, view.width, "Mugirl.CorporateUI.Welcome".Translate(), welcome);
                CorporateUI.Notice(new Rect(0f, y, view.width, noticeHeight), notice, hostile);
                y += noticeHeight + 16f;
                for (int i = 1; i <= 5; i++)
                {
                    string summary = SummaryKeys[i].Translate();
                    float rowHeight = Mathf.Max(48f, Text.CalcHeight(summary, view.width - 135f));
                    CorporateUI.Label(new Rect(0f, y, view.width - 135f, rowHeight), summary, color: CorporateUI.Muted);
                    if (CorporateUI.Button(new Rect(view.width - 122f, y, 122f, 37f), PageKeys[i].Translate())) selectedPage = i;
                    y += rowHeight + 17f;
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        private void DrawIntroduction(Rect rect)
        {
            string body = "Mugirl.CorporateIntro.ChapterCompleted".Translate() + "\n\n" + "Mugirl.CorporateIntro.ComingSoon".Translate();
            Rect view = new Rect(0f, 0f, rect.width - 18f, Mathf.Max(rect.height, Text.CalcHeight(body, rect.width - 18f) + 115f));
            Widgets.BeginScrollView(rect, ref introductionScroll, view);
            try
            {
                float y = 0f;
                CorporateUI.Heading(ref y, view.width, "Mugirl.CorporateIntro.Title".Translate(), "Mugirl.CorporateIntro.CompletedStatus".Translate());
                CorporateUI.Label(new Rect(0f, y, view.width, view.height - y), body);
            }
            finally { Widgets.EndScrollView(); }
        }

        private void DrawHistory(Rect rect)
        {
            float y = 0f;
            CorporateUI.Heading(ref y, rect.width, "Mugirl.CorporateUI.Page.History".Translate(), "Mugirl.CorporateUI.HistoryHint".Translate());
            Rect viewport = new Rect(0f, y, rect.width, Mathf.Max(1f, rect.height - y));
            float width = rect.width - 18f;
            float total = 0f;
            foreach (CorporateRecord record in network.Records)
                total += Mathf.Max(28f, Text.CalcHeight(RecordTitle(record), width)) + Mathf.Max(25f, Text.CalcHeight(record.detail ?? "", width)) + 23f;
            Rect view = new Rect(0f, 0f, width, Mathf.Max(viewport.height, total));
            Widgets.BeginScrollView(viewport, ref historyScroll, view);
            try
            {
                if (network.Records.Count == 0) CorporateUI.Label(new Rect(0f, 0f, view.width, 42f), "Mugirl.CorporateUI.NoHistory".Translate(), color: CorporateUI.Muted);
                float top = 0f;
                for (int i = network.Records.Count - 1; i >= 0; i--)
                {
                    CorporateRecord record = network.Records[i];
                    string title = RecordTitle(record);
                    float titleHeight = Mathf.Max(28f, Text.CalcHeight(title, width));
                    float detailHeight = Mathf.Max(25f, Text.CalcHeight(record.detail ?? "", width));
                    CorporateUI.Label(new Rect(0f, top, width, titleHeight), title);
                    CorporateUI.Label(new Rect(0f, top + titleHeight + 5f, width, detailHeight), record.detail, color: CorporateUI.Muted);
                    top += titleHeight + detailHeight + 23f;
                    CorporateUI.Rule(new Rect(0f, top - 6f, width, 1f));
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        private static string RecordTitle(CorporateRecord record)
        {
            return "Mugirl.CorporateUI.RecordTitle".Translate((record.tick / 60000f).ToString("0.0"),
                record.key.Translate(), CorporateUI.Money(record.amount));
        }
    }
}
