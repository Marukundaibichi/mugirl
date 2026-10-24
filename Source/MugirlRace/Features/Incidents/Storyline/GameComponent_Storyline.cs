using Mugirl.Features.LifeStages;

namespace Mugirl.Features.Incidents;

// 剧情组件只编排生命周期与状态操作；三条剧情各自保存事实，唤醒时钟是独立的瞬态对象。
public class GameComponent_Storyline : GameComponent
{
    private bool openingRead;
    private bool courierRead;
    private bool investmentRead;

    internal OpeningCrashState OpeningCrash { get; } = new();

    internal CourierContactState Courier { get; } = new();

    internal FusionInvestmentState Investment { get; } = new();

    public const int OpeningCrashInitialCheckTicks = 360;
    public const int CourierRaidInitialDelayTicks = 60000;

    public bool OpeningCrashStarted => OpeningCrash.started;
    public bool CourierTriggered => Courier.triggered;
    public bool CourierQuestStarted => Courier.questStarted;
    public bool InvestmentAccepted => Investment.accepted;
    public bool InvestmentPending => Investment.pending;

    internal StorylineClock Clock { get; } = new();

    public GameComponent_Storyline()
    {
    }

    public GameComponent_Storyline(Game game)
    {
    }

    // 原版新建与读档都先调用 FinalizeInit，再调用 StartedNewGame/LoadedGame（1.7 Game.InitNewGame/LoadGame 已核对），
    // 所以每局只在这里清理一次瞬态缓存；1.6 在三个回调里各做一次。
    public override void FinalizeInit()
    {
        base.FinalizeInit();
        ResetStoryServiceSchedule();
        GameLifecycle.ResetTransientState();
        NormalizeJuvenileGraphics();
    }

    // 每次加载维护已加入玩家的事件 Pawn 不变量，两个全体遍历保持先后顺序。
    public override void LoadedGame()
    {
        base.LoadedGame();
        EventPawnTags.ClearPlayerMigrationPawns();
        WildMugirlUtility.NormalizeLoadedPlayerPawnKinds();
    }

    public override void GameComponentTick()
    {
        base.GameComponentTick();
        StorylineService.Tick(this);
    }

    private static void NormalizeJuvenileGraphics()
    {
        int changed = LifeStageBodyTypeUtility.NormalizeLoadedPawns();
        if (changed > 0)
        {
            MugirlLog.DevMessage("Mugirl.Newborn.Log.CorrectedBodyTypes".Translate(changed).ToString());
        }
    }

    private void ResetStoryServiceSchedule()
    {
        Clock.lastTick = -1;
        Clock.nextTick = -1;
        Clock.nextStaleCheckTick = -1;
    }

    internal void WakeStoryService()
    {
        Clock.nextTick = -1;
    }

    internal void RecordOpeningCrashPawns(Pawn[] pawns)
    {
        OpeningCrash.pawns = new List<Pawn>(pawns);
        Courier.delayRemaining = CourierRaidInitialDelayTicks;
        WakeStoryService();
    }

    internal void MarkOpeningCrashStarted()
    {
        OpeningCrash.started = true;
        OpeningCrash.checkRemaining = -1;
    }

    internal void MarkCourierRaidStarted()
    {
        Courier.triggered = true;
        Courier.questStarted = true;
    }

    internal void SetFusionInvestor(Pawn investor)
    {
        Investment.investorActive = true;
        Investment.investor = investor;
        WakeStoryService();
    }

    internal void ClearFusionInvestor()
    {
        Investment.investorActive = false;
        Investment.investor = null;
    }

    internal void AcceptFusionInvestment(int amount, int resultDelayTicks)
    {
        Investment.accepted = true;
        Investment.pending = true;
        Investment.amount = amount;
        Investment.resultRemaining = resultDelayTicks;
        WakeStoryService();
    }

    public override void ExposeData()
    {
        base.ExposeData();
        EventStateScribe.Look(OpeningCrash, "opening", ref openingRead);
        EventStateScribe.Look(Courier, "courier", ref courierRead);
        EventStateScribe.Look(Investment, "investment", ref investmentRead);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            OpeningCrash.pawns ??= new List<Pawn>();
        }
    }
}
