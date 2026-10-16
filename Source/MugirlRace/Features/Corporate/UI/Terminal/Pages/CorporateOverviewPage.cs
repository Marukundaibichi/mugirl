namespace Mugirl.Features.Corporate;

/// <summary>总览页：欢迎区、三项指标、账户状态与通往各服务页的卡片。</summary>
internal sealed class CorporateOverviewPage(Dialog_CorporateTerminal terminal) : CorporateTerminalPage(terminal)
{
    // StaticCacheLifecycle: 不可变的翻译键与页面序号，不含游戏对象。
    private static readonly int[] ServicePages =
    [
        Dialog_CorporateTerminal.TradePage, Dialog_CorporateTerminal.OrdersPage, Dialog_CorporateTerminal.PeoplePage,
        Dialog_CorporateTerminal.FinancePage, Dialog_CorporateTerminal.MissionsPage, Dialog_CorporateTerminal.ServicesPage,
    ];

    private static readonly string[] SummaryKeys =
    [
        "Mugirl.CorporateUI.Summary.Trade", "Mugirl.CorporateUI.Summary.Orders", "Mugirl.CorporateUI.Summary.People",
        "Mugirl.CorporateUI.Summary.Finance", "Mugirl.CorporateUI.Summary.Missions", "Mugirl.CorporateServices.Summary",
    ];

    private readonly string[] summaries = new string[ServicePages.Length];
    private UVector2 scroll;
    private bool translated;
    private string welcomeTitle = string.Empty;
    private string welcome = string.Empty;
    private string openService = string.Empty;
    private string stockLabel = string.Empty;
    private string contractsLabel = string.Empty;
    private string recordsLabel = string.Empty;
    // 布局按（宽度、敌对状态、三项指标）重算。研究对白等其他窗口也能改变它们而不经过终端的版本号，所以每个事件比较原值。
    private float layoutWidth = -1f;
    private bool hostile;
    private int stockCount = -1;
    private int contractCount = -1;
    private int recordCount = -1;
    private string notice = string.Empty;
    private string stock = string.Empty;
    private string contracts = string.Empty;
    private string records = string.Empty;
    private float welcomeHeight;
    private float noticeHeight;
    private float cardHeight;
    private float cardWidth;
    private int columns;

    internal override void Draw(Rect rect, CorporateNetwork network)
    {
        float width = rect.width - 18f;
        EnsureLayout(width, network);
        float heroHeight = welcomeHeight + 60f;
        float contentHeight = heroHeight + 18f + 78f + 18f + noticeHeight + 22f
            + UnityMathf.Ceil((float)ServicePages.Length / columns) * (cardHeight + 12f);
        Rect view = new(0f, 0f, width, UnityMathf.Max(rect.height, contentHeight));
        CorporateUI.BeginScrollView(rect, ref scroll, view, "overview");
        try
        {
            CorporateUI.DrawGeometry(new Rect(width * 0.7f, 0f, width * 0.3f, heroHeight), 0.16f);
            CorporateUI.Label(new Rect(15f, 13f, width - 30f, 35f), welcomeTitle, GameFont.Medium);
            CorporateUI.Label(new Rect(15f, 51f, width - 30f, welcomeHeight), welcome, color: CorporateUI.Muted);
            float y = heroHeight + 18f;
            float metricWidth = (width - 24f) / 3f;
            DrawMetric(new Rect(0f, y, metricWidth, 78f), stockLabel, stock);
            DrawMetric(new Rect(metricWidth + 12f, y, metricWidth, 78f), contractsLabel, contracts);
            DrawMetric(new Rect((metricWidth + 12f) * 2f, y, metricWidth, 78f), recordsLabel, records);
            y += 96f;
            CorporateUI.Notice(new Rect(0f, y, width, noticeHeight), notice, hostile);
            y += noticeHeight + 22f;
            for (int index = 0; index < ServicePages.Length; index++)
            {
                int page = ServicePages[index];
                Rect card = new((index % columns) * (cardWidth + 12f), y + (index / columns) * (cardHeight + 12f), cardWidth, cardHeight);
                if (CorporateUI.Row(card, false, "service", page))
                {
                    Terminal.RequestPage(page);
                }

                CorporateUI.Label(new Rect(card.x + 14f, card.y + 11f, card.width - 28f, 27f), Terminal.PageBreadcrumb(page));
                CorporateUI.Label(new Rect(card.x + 14f, card.y + 47f, card.width - 28f, cardHeight - 83f), summaries[index], color: CorporateUI.Muted);
                CorporateUI.Label(new Rect(card.x + 14f, card.yMax - 28f, card.width - 28f, 22f),
                    openService, GameFont.Tiny, CorporateUI.Ink, TextAnchor.MiddleRight);
            }
        }
        finally
        {
            CorporateUI.EndScrollView();
        }
    }

    private void EnsureLayout(float width, CorporateNetwork network)
    {
        if (!translated)
        {
            translated = true;
            welcomeTitle = "Mugirl.CorporateUI.Welcome".Translate();
            welcome = "Mugirl.CorporateUI.WelcomeText".Translate();
            openService = "Mugirl.CorporateUI.OpenService".Translate() + "  →";
            stockLabel = "Mugirl.CorporateUI.MetricStock".Translate();
            contractsLabel = "Mugirl.CorporateUI.MetricContracts".Translate();
            recordsLabel = "Mugirl.CorporateUI.MetricRecords".Translate();
            for (int i = 0; i < SummaryKeys.Length; i++)
            {
                summaries[i] = SummaryKeys[i].Translate();
            }
        }

        Faction? player = Faction.OfPlayerSilentFail;
        Faction? corporation = network.CorporateFaction;
        bool isHostile = corporation is not null && player is not null && corporation.HostileTo(player);
        int currentStock = network.Stock.Count;
        int currentContracts = network.ActiveMissionCount;
        int currentRecords = network.Records.Count;
        if (currentStock != stockCount || currentContracts != contractCount || currentRecords != recordCount)
        {
            stockCount = currentStock;
            contractCount = currentContracts;
            recordCount = currentRecords;
            stock = currentStock.ToString("00");
            contracts = currentContracts.ToString("00");
            records = currentRecords.ToString("00");
        }

        if (layoutWidth == width && isHostile == hostile && notice.Length > 0)
        {
            return;
        }

        layoutWidth = width;
        hostile = isHostile;
        notice = (hostile ? "Mugirl.CorporateUI.Hostile" : "Mugirl.CorporateUI.AccountReady").Translate();
        welcomeHeight = UnityMathf.Max(24f, Text.CalcHeight(welcome, width - 30f));
        noticeHeight = UnityMathf.Max(56f, Text.CalcHeight(notice, width - 20f) + 22f);
        columns = width >= 630f ? 2 : 1;
        cardWidth = (width - (columns - 1) * 12f) / columns;
        cardHeight = 116f;
        for (int i = 0; i < summaries.Length; i++)
        {
            cardHeight = UnityMathf.Max(cardHeight, Text.CalcHeight(summaries[i], cardWidth - 28f) + 82f);
        }
    }

    private static void DrawMetric(Rect rect, string label, string value)
    {
        CorporateUI.Rule(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f));
        CorporateUI.Label(new Rect(rect.x + 12f, rect.y + 7f, rect.width - 24f, 22f), label, GameFont.Tiny, CorporateUI.Muted);
        CorporateUI.Label(new Rect(rect.x + 12f, rect.y + 31f, rect.width - 24f, 37f), value, GameFont.Medium);
    }
}
