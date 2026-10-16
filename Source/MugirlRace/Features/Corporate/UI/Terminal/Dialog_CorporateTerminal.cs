using Mugirl.Platform;
using Mugirl.Platform.Ui;

namespace Mugirl.Features.Corporate;

/// <summary>
/// 巨企终端窗口：页眉、页面导航、页面间的淡出淡入过渡与操作反馈提示。九个页面各自由 <see cref="CorporateTerminalPage"/> 绘制，
/// 窗口为它们提供共用服务与每帧快照（白银数、连接有效性、文本测量缓存）。
/// </summary>
/// <remarks>
/// 类型名、<c>RequestPage(int)</c>、<c>selectedPage</c> 与 <c>PageKeys</c> 由游戏内验证套件（UiSmokeSuite）按名称访问，不要改名。
/// </remarks>
public class Dialog_CorporateTerminal : CorporateAnimatedWindow
{
    internal const int OverviewPage = 0;
    internal const int TradePage = 1;
    internal const int OrdersPage = 2;
    internal const int PeoplePage = 3;
    internal const int FinancePage = 4;
    internal const int MissionsPage = 5;
    internal const int IntroductionPage = 6;
    internal const int HistoryPage = 7;
    internal const int ServicesPage = 8;

    private const float ContentOut = 0.12f;
    private const float ContentIn = 0.23f;
    private const float NavigationStride = 47f;
    private const float FeedbackDuration = 4.2f;
    private const string HeaderBrand = "M E G A C O R P   /   N E T W O R K";

    // StaticCacheLifecycle: 不可变的翻译键与作用域名，不含游戏对象或动画状态。
    private static readonly string[] PageKeys =
    [
        "Mugirl.CorporateUI.Page.Overview", "Mugirl.CorporateUI.Page.Trade", "Mugirl.CorporateUI.Page.Orders",
        "Mugirl.CorporateUI.Page.People", "Mugirl.CorporateUI.Page.Finance", "Mugirl.CorporateUI.Page.Missions",
        "Mugirl.CorporateUI.Page.Introduction", "Mugirl.CorporateUI.Page.History", "Mugirl.CorporateServices.Page",
    ];

    private static readonly string[] PageScopes =
        ["page/0", "page/1", "page/2", "page/3", "page/4", "page/5", "page/6", "page/7", "page/8"];

    private readonly CorporateTradeContext context;
    private readonly CorporateTerminalPage[] pages;
    private readonly CorporateOrdersPage orders;
    private readonly CorporateTextCache textCache = new();
    private int selectedPage;
    private int requestedPage;
    private Action? pendingContent;
    private bool contentTransitionActive;
    private double contentStarted;
    private bool contentCommitted;
    private float contentFrom = 1f;
    private float navigationMarker;
    private bool clearFocus;
    private string? feedback;
    private bool feedbackSuccess;
    private double feedbackAt = CorporateClock.Never;
    private int frameCacheVersion;
    private int silverFrame = -1;
    private int silverVersion = -1;
    private int silver;
    private int validFrame = -1;
    private int validVersion = -1;
    private bool valid;
    private PageLabels? labels;
    private int headerSilver = -1;
    private int headerRefresh = -1;
    private string headerStatus = string.Empty;

    public Dialog_CorporateTerminal(CorporateTradeContext context)
    {
        this.context = context;
        draggable = true;
        CorporateIntroductionPage introduction = new(this);
        orders = new CorporateOrdersPage(this);
        pages =
        [
            new CorporateOverviewPage(this),
            new CorporateTradePage(this),
            orders,
            new CorporatePeoplePage(this),
            new CorporateFinancePage(this),
            new CorporateMissionsPage(this, introduction),
            introduction,
            new CorporateHistoryPage(this),
            new CorporateServicesPage(this),
        ];
    }

    public override UVector2 InitialSize => new(UnityMathf.Min(1180f, UI.screenWidth - 32f), UnityMathf.Min(800f, UI.screenHeight - 32f));

    internal CorporateTradeContext Context => context;

    internal CorporateMotion Motion => motion;

    /// <summary>窗口级文本测量与截断缓存。</summary>
    internal CorporateTextCache TextCache => textCache;

    /// <summary>派生数据的版本号：每次操作结果（<see cref="ShowFeedback"/>）后加一，各页面按它重建缓存。</summary>
    internal int CacheVersion => frameCacheVersion;

    /// <summary>可用白银数，在同一引擎帧、同一缓存版本内只统计一次（统计要遍历全部可交易物品）。</summary>
    internal int SilverCount
    {
        get
        {
            int frame = EngineClock.FrameCount;
            if (frame != silverFrame || silverVersion != frameCacheVersion)
            {
                silverFrame = frame;
                silverVersion = frameCacheVersion;
                silver = context.SilverCount;
            }

            return silver;
        }
    }

    /// <summary>交易连接仍然有效（通讯台可用或商队仍在据点），在同一引擎帧、同一缓存版本内只检查一次（含寻路判断）。</summary>
    internal bool ContextValid
    {
        get
        {
            int frame = EngineClock.FrameCount;
            if (frame != validFrame || validVersion != frameCacheVersion)
            {
                validFrame = frame;
                validVersion = frameCacheVersion;
                valid = context.IsValid;
            }

            return valid;
        }
    }

    public override void PreOpen()
    {
        base.PreOpen();
        CorporateNetwork? network = CorporateNetwork.Current;
        network?.EnsureWeeklyOffers();
        // 窗口强制暂停，打开期间游戏时间不前进：打开时结算一次贷款利息即可，绘制中不再修改贷款状态。
        network?.UpdateLoanAccrual();
    }

    public override void WindowUpdate()
    {
        base.WindowUpdate();
        navigationMarker = CorporateEasing.Approach(navigationMarker, requestedPage * NavigationStride, 17f, EngineClock.DeltaTime);
        if (Closing)
        {
            return;
        }

        CorporateNetwork? network = CorporateNetwork.Current;
        if (network is not null)
        {
            orders.UpdateCatalog(network, selectedPage == OrdersPage || requestedPage == OrdersPage);
        }

        if (!contentTransitionActive)
        {
            return;
        }

        float elapsed = CorporateClock.Since(contentStarted);
        if (!contentCommitted && elapsed >= ContentOut)
        {
            contentCommitted = true;
            Action? apply = pendingContent;
            pendingContent = null;
            apply?.Invoke();
            clearFocus = true;
        }

        if (elapsed >= ContentOut + ContentIn)
        {
            contentTransitionActive = false;
        }
    }

    private float ContentVisibility
    {
        get
        {
            if (!contentTransitionActive)
            {
                return 1f;
            }

            float elapsed = CorporateClock.Since(contentStarted);
            return contentCommitted
                ? CorporateEasing.SmoothStep01((elapsed - ContentOut) / ContentIn)
                : contentFrom * (1f - CorporateEasing.SmoothStep01(elapsed / ContentOut));
        }
    }

    /// <summary>切换到 <paramref name="page"/>（超出范围时钳制），经内容过渡在淡出结束时生效。</summary>
    internal void RequestPage(int page)
    {
        page = UnityMathf.Clamp(page, 0, PageKeys.Length - 1);
        if (page == requestedPage && (pendingContent is not null || selectedPage == page))
        {
            return;
        }

        requestedPage = page;
        RequestContentTransition(() => selectedPage = page);
    }

    /// <summary>
    /// 页面内容的淡出淡入：<paramref name="change"/> 在淡出结束时执行，期间页面不可交互并清除键盘焦点。
    /// 连续请求时只保留最后一个改变。
    /// </summary>
    internal void RequestContentTransition(Action change)
    {
        if (Closing)
        {
            return;
        }

        pendingContent = change;
        if (!contentTransitionActive || contentCommitted)
        {
            contentFrom = ContentVisibility;
            contentStarted = CorporateClock.Now;
            contentTransitionActive = true;
        }

        contentCommitted = false;
        clearFocus = true;
    }

    internal void ShowMenu(List<FloatMenuOption> options) => GameUtility.TryAddWindow(new Dialog_CorporateChoice(options));

    internal void Confirm(string text, Action action, bool destructive = false)
        => GameUtility.TryAddWindow(new Dialog_CorporateConfirm(text, action, destructive));

    /// <summary>显示操作结果提示，并使各页面的派生数据缓存失效。<paramref name="text"/> 为 null 时显示“货物已变化”。</summary>
    internal void ShowFeedback(bool success, string? text)
    {
        feedback = text ?? "Mugirl.Corporate.GoodsChanged".Translate().ToString();
        feedbackSuccess = success;
        feedbackAt = CorporateClock.Now;
        frameCacheVersion++;
    }

    /// <summary>通用的交易结果提示：成功显示“操作成功”，失败显示原因（没有原因时显示“货物已变化”）。</summary>
    internal void ShowTradeResult(bool success, string? reason)
        => ShowFeedback(success, success ? "Mugirl.Corporate.Success".Translate().ToString() : reason);

    /// <summary>页面序号 + 标题，例如“02  /  交易”。</summary>
    internal string PageBreadcrumb(int page) => Labels.Breadcrumbs[page];

    protected override void DrawContents(Rect inRect)
    {
        if (clearFocus)
        {
            UI.UnfocusCurrentControl();
            clearFocus = false;
        }

        // 提示框画在页面之上，但输入按绘制顺序分发：先在提示框区域吸收点击，避免点穿到下方的按钮。
        if (TryGetFeedbackLayout(inRect, out Rect toast, out _, out _))
        {
            Widgets.ButtonInvisible(toast, doMouseoverSound: false);
        }

        CorporateNetwork? network = CorporateNetwork.Current;
        CorporateUI.Fill(inRect, CorporateUI.Background);
        CorporateUI.DrawGeometry(inRect, 0.42f);
        CorporateUI.Rule(new Rect(0f, 0f, inRect.width, 1f));
        Rect client = inRect.ContractedBy(22f);
        DrawHeader(client, network);
        Rect body = new(client.x, client.y + 97f, client.width, client.height - 135f);
        DrawNavigation(ref body);
        CorporateUI.Fill(body, CorporateUI.Surface);
        Rect breadcrumb = new(body.x + 18f, body.y + 10f, body.width - 36f, 25f);
        CorporateUI.Label(breadcrumb, Labels.Breadcrumbs[selectedPage], GameFont.Tiny, CorporateUI.Muted);
        CorporateUI.Rule(new Rect(body.x + 18f, body.y + 39f, body.width - 36f, 1f));
        Rect page = new(body.x + 18f, body.y + 54f, body.width - 36f, body.height - 72f);
        float opacity = ContentVisibility;
        float slide = (1f - opacity) * (contentCommitted ? 13f : -9f);
        Widgets.BeginGroup(page);
        try
        {
            Widgets.BeginGroup(new Rect(0f, slide, page.width, page.height));
            try
            {
                using (CorporateUI.BeginFrame(motion, PageScopes[selectedPage], opacity, !contentTransitionActive))
                {
                    DrawPage(new Rect(0f, 0f, page.width, page.height), network);
                }
            }
            finally
            {
                Widgets.EndGroup();
            }
        }
        finally
        {
            Widgets.EndGroup();
        }

        CorporateUI.Rule(new Rect(client.x, client.yMax - 26f, client.width, 1f));
        CorporateUI.Label(new Rect(client.x, client.yMax - 20f, client.width - 150f, 22f),
            "Mugirl.CorporateUI.Context".Translate(context.Label), GameFont.Tiny, CorporateUI.Muted);
        CorporateUI.Label(new Rect(client.xMax - 148f, client.yMax - 20f, 148f, 22f),
            Labels.PrivateChannel, GameFont.Tiny, CorporateUI.Muted, TextAnchor.MiddleRight);
        DrawFeedback(inRect);
    }

    private void DrawPage(Rect rect, CorporateNetwork? network)
    {
        if (network is null || !ContextValid)
        {
            CorporateUI.Notice(rect, "Mugirl.CorporateUI.ConnectionLost".Translate(), true);
        }
        else if (CorporateIntroduction.Current?.Completed != true)
        {
            CorporateUI.Notice(rect, "Mugirl.CorporateUI.Locked".Translate(), true);
        }
        else
        {
            pages[selectedPage].Draw(rect, network);
        }
    }

    private void DrawHeader(Rect client, CorporateNetwork? network)
    {
        CorporateUI.Label(new Rect(client.x, client.y, client.width - 52f, 18f), HeaderBrand, GameFont.Tiny, CorporateUI.Muted);
        CorporateUI.Label(new Rect(client.x, client.y + 23f, client.width - 65f, 36f), Labels.Title, GameFont.Medium);
        int refresh = network is null ? 0 : Math.Max(0, network.NextRefreshTick - CorporateNetwork.Now);
        int silverCount = SilverCount;
        if (silverCount != headerSilver || refresh != headerRefresh)
        {
            headerSilver = silverCount;
            headerRefresh = refresh;
            headerStatus = "Mugirl.CorporateUI.HeaderStatus".Translate(CorporateUI.Money(silverCount), (refresh / 60000f).ToString("0.0"));
        }

        CorporateUI.Label(new Rect(client.x, client.y + 62f, client.width - 52f, 25f), headerStatus, GameFont.Tiny, CorporateUI.Muted);
        Rect close = new(client.xMax - 38f, client.y + 5f, 38f, 38f);
        if (CorporateUI.Button(close, "×", id: "close"))
        {
            Close();
        }

        TooltipHandler.TipRegionByKey(close, "Mugirl.CorporateUI.CloseHint");
    }

    private void DrawNavigation(ref Rect body)
    {
        PageLabels text = Labels;
        if (body.width < 850f || body.height < 36f + PageKeys.Length * NavigationStride + 65f)
        {
            if (CorporateUI.Button(new Rect(body.x, body.y, body.width, 35f), text.MenuButtons[requestedPage], id: "navigation-menu"))
            {
                List<FloatMenuOption> options = new(PageKeys.Length);
                for (int i = 0; i < PageKeys.Length; i++)
                {
                    int index = i;
                    options.Add(new FloatMenuOption(text.Titles[i], () => RequestPage(index)));
                }

                ShowMenu(options);
            }

            body = WithMinY(body, body.y + NavigationStride);
            return;
        }

        const float width = 180f;
        CorporateUI.Label(new Rect(body.x, body.y + 2f, width, 22f), text.Services, GameFont.Tiny, CorporateUI.Muted);
        for (int i = 0; i < PageKeys.Length; i++)
        {
            Rect row = new(body.x + 8f, body.y + 36f + i * NavigationStride, width - 8f, 39f);
            if (CorporateUI.Button(row, text.Navigation[i], primary: i == requestedPage, id: "nav", index: i))
            {
                RequestPage(i);
            }
        }

        CorporateUI.Fill(new Rect(body.x, body.y + 45f + navigationMarker, 2f, 21f), CorporateUI.Ink);
        CorporateUI.Label(new Rect(body.x + 8f, body.yMax - 56f, width - 10f, 52f), text.Motto, GameFont.Tiny, CorporateUI.Muted);
        body = WithMinX(body, body.x + (width + 20f));
    }

    // 与 Unity Rect.xMin/yMin 的赋值相同（保持 xMax/yMax 不变），但只用构造函数：ADR-006 的边界白名单只允许 Rect 的构造与读取。
    private static Rect WithMinX(Rect rect, float xMin)
    {
        float xMax = rect.xMax;
        return new Rect(xMin, rect.y, xMax - xMin, rect.height);
    }

    private static Rect WithMinY(Rect rect, float yMin)
    {
        float yMax = rect.yMax;
        return new Rect(rect.x, yMin, rect.width, yMax - yMin);
    }

    private bool TryGetFeedbackLayout(Rect rect, out Rect toast, out float visibility, out float age)
    {
        age = CorporateClock.Since(feedbackAt);
        if (feedback is null || age > FeedbackDuration)
        {
            toast = default;
            visibility = 0f;
            return false;
        }

        visibility = CorporateEasing.SmoothStep01(age / 0.2f) * (1f - CorporateEasing.SmoothStep01((age - 3.85f) / 0.35f));
        float width = UnityMathf.Min(560f, rect.width - 60f);
        float height = UnityMathf.Clamp(textCache.Height(feedback, width - 58f, GameFont.Small) + 30f, 54f, 140f);
        toast = new Rect((rect.width - width) / 2f, rect.height - height - 42f + (1f - visibility) * 14f, width, height);
        return true;
    }

    private void DrawFeedback(Rect rect)
    {
        if (feedback is null || !TryGetFeedbackLayout(rect, out Rect toast, out float visibility, out float age))
        {
            return;
        }

        using (CorporateUI.BeginFrame(motion, "feedback", visibility))
        {
            CorporateUI.Panel(toast);
            CorporateUI.Label(new Rect(toast.x + 13f, toast.y + 14f, 22f, 24f), feedbackSuccess ? "+" : "!", color: CorporateUI.Ink);
            CorporateUI.Label(new Rect(toast.x + 40f, toast.y + 13f, toast.width - 56f, toast.height - 22f), feedback);
            CorporateUI.Rule(new Rect(toast.x, toast.yMax - 2f, toast.width * UnityMathf.Clamp01(1f - age / FeedbackDuration), 2f));
        }
    }

    public override void PostClose()
    {
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i].PostClose();
        }

        textCache.Clear();
        base.PostClose();
    }

    private PageLabels Labels => labels ??= new PageLabels();

    /// <summary>导航、面包屑与固定文案的翻译结果；窗口是模态的，打开期间语言不会变化，首次绘制时翻译一次。</summary>
    private sealed class PageLabels
    {
        internal readonly string[] Titles = new string[PageKeys.Length];
        internal readonly string[] Navigation = new string[PageKeys.Length];
        internal readonly string[] Breadcrumbs = new string[PageKeys.Length];
        internal readonly string[] MenuButtons = new string[PageKeys.Length];
        internal readonly string Title = "Mugirl.CorporateUI.Title".Translate();
        internal readonly string Services = "Mugirl.CorporateUI.Services".Translate();
        internal readonly string Motto = "Mugirl.CorporateUI.Motto".Translate();
        internal readonly string PrivateChannel = "Mugirl.CorporateUI.PrivateChannel".Translate();

        internal PageLabels()
        {
            for (int i = 0; i < PageKeys.Length; i++)
            {
                string number = (i + 1).ToString("00");
                Titles[i] = PageKeys[i].Translate();
                Navigation[i] = number + "   " + Titles[i];
                Breadcrumbs[i] = number + "  /  " + Titles[i];
                MenuButtons[i] = Breadcrumbs[i] + "  ▾";
            }
        }
    }
}
