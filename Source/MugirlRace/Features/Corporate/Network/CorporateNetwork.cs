using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.Corporate;

// 巨企业务唯一的资产保管者；各合同只保存 Vault 中对象的引用。
// 账户聚合各自拥有持久状态和局部不变量；组件负责跨账户交易、时钟与游戏副作用。
public partial class CorporateNetwork : GameComponent, IThingHolder
{
    private bool historyRead;

    public const int DayTicks = 60000;
    public const int WeekTicks = 7 * DayTicks;
    private const int ServiceIntervalTicks = 250;

    private ThingOwner<Thing> vault;
    private int nextRefreshTick = -1;
    private int sequence;
    private int nextServiceTick;
    private readonly CorporateHistory history = new();
    private readonly CorporateTradeDesk trade = new();
    private readonly CorporateLoanAccount loan = new();
    private readonly CorporatePeopleDesk people = new();
    private readonly CorporateMissionBoard missionBoard = new();
    private readonly CorporateClientAccount services = new();

    public CorporateNetwork(Game game)
    {
        vault = new ThingOwner<Thing>(this, false, LookMode.Deep);
        // 新档与读档都会构造新组件：先清掉上一局遗留的债务叠加投影。
        ResetDebtOverlay();
    }

    public static CorporateNetwork? Current => GameUtility.GameComponent<CorporateNetwork>();
    public static int Now => TickUtility.CurrentGameTickOrFallback(0);
    public bool Unlocked => CorporateIntroduction.Current?.Completed == true;
    public Faction? CorporateFaction => GameUtility.Factions?.FirstFactionOfDef(CorporateDefOf.Mugirl_GiantCorporation);
    public ThingOwner<Thing> Vault => vault;
    public int NextRefreshTick => nextRefreshTick;
    public IReadOnlyList<CorporateHistoryEntry> Records => history.Entries;
    public IThingHolder? ParentHolder => null;
    public ThingOwner GetDirectlyHeldThings() => vault;
    public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, vault);

    public int NewId() => ++sequence;

    public bool CanTrade([NotNullWhen(true)] CorporateTradeContext? context, [NotNullWhen(false)] out string? reason)
    {
        if (context == null || !context.IsValid)
        {
            reason = "Mugirl.Corporate.InvalidContext".Translate();
            return false;
        }

        if (!Unlocked)
        {
            reason = "Mugirl.Corporate.Locked".Translate();
            return false;
        }

        Faction? corporation = CorporateFaction;
        if (corporation == null || corporation.defeated)
        {
            reason = "Mugirl.Corporate.NoFaction".Translate();
            return false;
        }

        if (corporation.HostileTo(PlayerFactionUtility.PlayerFaction))
        {
            reason = "Mugirl.Corporate.Hostile".Translate();
            return false;
        }

        reason = null;
        return true;
    }

    public void Record(string key, string? detail, int amount = 0) => history.Add(Now, key, detail, amount);

    public void EnsureWeeklyOffers()
    {
        int now = Now;
        if (!Unlocked || (nextRefreshTick >= 0 && now < nextRefreshTick))
        {
            return;
        }

        // 一次跳到当前周期，不补生成过去每一周的人口或合同。
        nextRefreshTick = nextRefreshTick < 0 ? now + WeekTicks
            : nextRefreshTick + (((now - nextRefreshTick) / WeekTicks) + 1) * WeekTicks;
        TradeRefreshWeekly();
        PeopleRefreshWeekly();
        QuestsRefreshWeekly();
    }

    public override void GameComponentTick()
    {
        int now = Now;
        if (now < nextServiceTick)
        {
            return;
        }

        nextServiceTick = now + ServiceIntervalTicks;
        EnsureWeeklyOffers();
        TradeTick();
        FinanceTick();
        PeopleTick();
        QuestsTick();
    }

    public override void LoadedGame()
    {
        nextServiceTick = 0;
        PrepareOrderCatalog();
        // 样品和人员报价按固定顺序刷新，此时 Pawn 与市值已完成初始化。
        RefreshStockSamples();
        RefreshPeopleOffers();
    }

    public override void StartedNewGame()
    {
        nextServiceTick = 0;
        PrepareOrderCatalog();
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Deep.Look(ref vault, "vault", this);
        Scribe_Values.Look(ref nextRefreshTick, "nextRefreshTick", -1);
        Scribe_Values.Look(ref sequence, "sequence", 0);
        CorporateStateScribe.Look(history, "history", ref historyRead);
        TradeExposeData();
        FinanceExposeData();
        PeopleExposeData();
        QuestsExposeData();
        ServicesExposeData();
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            vault ??= new ThingOwner<Thing>(this, false, LookMode.Deep);
            history.RemoveMissingEntries();
            nextServiceTick = 0;
        }
    }
}
