using System.Linq;
using RimWorld.Planet;

namespace Mugirl.Features.Corporate;

/// <summary>任务板：日常、支线两个页签的任务卡片与操作按钮；“主线”页签显示介绍章节页。</summary>
internal sealed class CorporateMissionsPage(Dialog_CorporateTerminal terminal, CorporateIntroductionPage introduction) : CorporateTerminalPage(terminal)
{
    private const int MainTab = 2;
    private const float AcceptWidth = 145f;
    private const float SubmitWidth = 126f;
    private const float ClaimWidth = 170f;
    private const float SmallActionWidth = 92f;
    private const float AbandonWidth = 90f;

    // StaticCacheLifecycle: 不可变的翻译键。
    private static readonly string[] TabKeys = ["Mugirl.CQ.Daily", "Mugirl.CQ.Side", "Mugirl.CQ.Main"];

    private readonly CorporateIntroductionPage introduction = introduction;
    private readonly List<MissionCard> cards = [];
    private UVector2 scroll;
    private int tab;
    private float contentHeight = 600f;
    private int cardsVersion = -1;
    private int cardsTab = -1;
    private int cardsSignature;

    // 任务卡的显示文本与布局缓存：标题、说明与状态行都由任务字段派生（含多参数 Translate），
    // forcePause 下静态，按（页签、版本、任务状态签名）重建；说明高度另按宽度缓存。
    private sealed class MissionCard(CorporateMission mission)
    {
        internal readonly CorporateMission Mission = mission;
        internal string Title = string.Empty;
        internal string Description = string.Empty;
        internal string StatusLine = string.Empty;
        internal Site? LocateSite;
        internal bool CanTalk;
        internal bool CanAbandon;
        internal float MeasuredWidth = -1f;
        internal float DescriptionHeight;
        internal float ActionHeight;
    }

    internal override void Draw(Rect rect, CorporateNetwork network)
    {
        // 1.6 的分组与滚动视图没有 try/finally：绘制卡片时抛出异常会让 GUI 裁剪栈失衡，本帧其余界面连锁报错。
        Widgets.BeginGroup(rect);
        try
        {
            DrawBoard(rect.width, rect.height, network);
        }
        finally
        {
            Widgets.EndGroup();
        }
    }

    private void DrawBoard(float width, float height, CorporateNetwork network)
    {
        for (int i = 0; i < TabKeys.Length; i++)
        {
            if (CorporateUI.Button(new Rect(i * (width / 3), 0, width / 3 - 5, 34), TabKeys[i].Translate(), primary: tab == i, id: "missions/tab", index: i) && tab != i)
            {
                int selected = i;
                Terminal.RequestContentTransition(() =>
                {
                    tab = selected;
                    scroll = default;
                });
            }
        }

        if (tab == MainTab)
        {
            introduction.Draw(new Rect(0, 44, width, height - 44));
            return;
        }

        CorporateUI.Label(new Rect(0, 43, width, 44), "Mugirl.CQ.BoardHelp".Translate(network.ActiveMissionCount), color: CorporateUI.Muted);
        Rect outRect = new(0, 94, width, height - 94);
        Rect view = new(0, 0, width - 20, contentHeight);
        CorporateUI.BeginScrollView(outRect, ref scroll, view, "missions/page-scroll");
        try
        {
            contentHeight = UnityMathf.Max(DrawCards(view.width, network), 120);
        }
        finally
        {
            CorporateUI.EndScrollView();
        }
    }

    private float DrawCards(float width, CorporateNetwork network)
    {
        EnsureCards(network);
        float y = 0;
        if (cards.Count == 0)
        {
            CorporateUI.Notice(new Rect(0, y, width, 100), (tab == 1 ? "Mugirl.CQ.NoSide" : "Mugirl.CQ.NoMissions").Translate());
            y += 110;
        }

        foreach (MissionCard card in cards)
        {
            y += DrawCard(card, y, width, network) + 12;
        }

        return y;
    }

    private float DrawCard(MissionCard card, float y, float width, CorporateNetwork network)
    {
        CorporateMission mission = card.Mission;
        if (card.MeasuredWidth != width)
        {
            card.MeasuredWidth = width;
            card.DescriptionHeight = Text.CalcHeight(card.Description, width - 24);
            card.ActionHeight = CorporateLayout.FlowHeight(width - 24f, ActionWidths(card));
        }

        float descHeight = card.DescriptionHeight;
        float cardHeight = 120f + descHeight + card.ActionHeight - 30f;
        if (mission.pendingGoods.Count > 0)
        {
            cardHeight += 36;
        }

        CorporateUI.Panel(new Rect(0, y, width, cardHeight));
        CorporateUI.Label(new Rect(12, y + 8, width - 24, 28), card.Title, color: CorporateUI.Accent);
        CorporateUI.Label(new Rect(12, y + 37, width - 24, descHeight), card.Description);
        float row = y + 43 + descHeight;
        CorporateUI.Label(new Rect(12, row, width - 24, 25), card.StatusLine, color: CorporateUI.Muted);
        row += 30;
        float x = 12;
        CorporateTradeContext context = Context;
        if (mission.state == CorporateMissionState.Available)
        {
            bool enabled = network.CanAcceptMission(mission, context, out string? reason);
            if (CorporateUI.Button(new Rect(x, row, AcceptWidth, 30), "Mugirl.CQ.Accept".Translate(), enabled, true, "missions/accept", mission.id))
            {
                Terminal.Confirm(mission.Description + "\n\n" + "Mugirl.CQ.SignConfirm".Translate(mission.cost), () =>
                {
                    bool success = network.AcceptMission(mission, context, out string? error);
                    Terminal.ShowTradeResult(success, error);
                });
            }

            if (!enabled)
            {
                CorporateUI.Label(new Rect(165, row, width - 177, 42), reason, color: CorporateUI.Danger);
            }
        }
        else
        {
            bool contextValid = Terminal.ContextValid;
            if (mission.state == CorporateMissionState.Active && mission.kind == CorporateMissionKind.Processing
                && CorporateUI.Button(ActionRect(ref x, ref row, width, SubmitWidth), "Mugirl.CQ.Submit".Translate(), contextValid, true, "missions/submit", mission.id))
            {
                SubmitProducts(mission, network);
            }

            if (mission.state == CorporateMissionState.Ready)
            {
                int total = mission.reward + (mission.kind == CorporateMissionKind.Processing ? mission.cost : 0);
                if (CorporateUI.Button(ActionRect(ref x, ref row, width, ClaimWidth), "Mugirl.CQ.Claim".Translate(total), contextValid, true, "missions/claim", mission.id))
                {
                    Terminal.ShowTradeResult(network.ClaimMission(mission, context), "Mugirl.CQ.DeliveryFailed".Translate());
                }
            }

            Site? site = card.LocateSite;
            if (site is not null
                && CorporateUI.Button(ActionRect(ref x, ref row, width, SmallActionWidth), "Mugirl.CQ.Locate".Translate(), id: "missions/locate", index: mission.id))
            {
                Terminal.CloseThen(() => CameraJumper.TryJumpAndSelect(site));
            }

            if (card.CanTalk
                && CorporateUI.Button(ActionRect(ref x, ref row, width, SmallActionWidth), "Mugirl.CQ.Talk".Translate(), id: "missions/talk", index: mission.id))
            {
                network.ShowResearchDialogue(mission);
            }

            if (mission.quest is not null
                && CorporateUI.Button(ActionRect(ref x, ref row, width, SmallActionWidth), "Mugirl.CQ.QuestLog".Translate(), id: "missions/log", index: mission.id))
            {
                Terminal.CloseThen(() => GameUtility.MainTabs.SetCurrentTab(MainButtonDefOf.Quests));
            }

            if (card.CanAbandon
                && CorporateUI.Button(ActionRect(ref x, ref row, width, AbandonWidth), "Mugirl.CQ.Abandon".Translate(), id: "missions/abandon", index: mission.id))
            {
                Terminal.Confirm("Mugirl.CQ.AbandonConfirm".Translate(), () => Terminal.ShowTradeResult(network.AbandonMission(mission), null), true);
            }
        }

        if (mission.pendingGoods.Count > 0)
        {
            row += 36;
            if (CorporateUI.Button(new Rect(12, row, width - 24, 30), "Mugirl.CQ.ReceiveGoods".Translate(), Terminal.ContextValid, id: "missions/goods", index: mission.id))
            {
                Terminal.ShowTradeResult(network.ReceiveMissionGoods(mission, context), "Mugirl.CQ.DeliveryFailed".Translate());
            }
        }

        return cardHeight;
    }

    private void SubmitProducts(CorporateMission mission, CorporateNetwork network)
    {
        CorporateTradeContext context = Context;
        if (network.MissionSubmissionNeedsQualityConfirmation(mission, context))
        {
            Terminal.Confirm("Mugirl.CQ.HighQualityConfirm".Translate(mission.Title, mission.reward),
                () => Terminal.ShowTradeResult(network.SubmitMissionProducts(mission, context, true), "Mugirl.CQ.NoProducts".Translate()));
        }
        else
        {
            Terminal.ShowTradeResult(network.SubmitMissionProducts(mission, context), "Mugirl.CQ.NoProducts".Translate());
        }
    }

    // 卡片中按钮的流式布局宽度（与 DrawCard 中按钮出现的顺序一致）。
    private static float[] ActionWidths(MissionCard card)
    {
        CorporateMission mission = card.Mission;
        if (mission.state == CorporateMissionState.Available)
        {
            return [AcceptWidth];
        }

        List<float> widths = [];
        if (mission.state == CorporateMissionState.Active && mission.kind == CorporateMissionKind.Processing)
        {
            widths.Add(SubmitWidth);
        }

        if (mission.state == CorporateMissionState.Ready)
        {
            widths.Add(ClaimWidth);
        }

        if (card.LocateSite is not null)
        {
            widths.Add(SmallActionWidth);
        }

        if (card.CanTalk)
        {
            widths.Add(SmallActionWidth);
        }

        if (mission.quest is not null)
        {
            widths.Add(SmallActionWidth);
        }

        if (card.CanAbandon)
        {
            widths.Add(AbandonWidth);
        }

        return [.. widths];
    }

    private static Rect ActionRect(ref float x, ref float y, float width, float buttonWidth)
        => CorporateLayout.FlowNext(ref x, ref y, width, buttonWidth);

    private void EnsureCards(CorporateNetwork network)
    {
        IReadOnlyList<CorporateMission> missions = network.Missions;
        // 研究对白等其他窗口会改变任务状态而不经过终端的版本号（1.6 中卡片按钮因此停留在旧状态），所以把状态签名也作为缓存键。
        int signature = StateSignature(missions);
        if (cardsVersion == Terminal.CacheVersion && cardsTab == tab && cardsSignature == signature)
        {
            return;
        }

        cardsVersion = Terminal.CacheVersion;
        cardsTab = tab;
        cardsSignature = signature;
        cards.Clear();
        bool side = tab == 1;
        IEnumerable<CorporateMission> ordered = missions.Where(m => m.IsSide == side)
            .OrderBy(m => m.IsFinished ? 2 : m.state == CorporateMissionState.Available ? 1 : 0)
            .ThenByDescending(m => m.id);
        foreach (CorporateMission mission in ordered)
        {
            cards.Add(BuildCard(mission));
        }
    }

    private static int StateSignature(IReadOnlyList<CorporateMission> missions)
    {
        unchecked
        {
            int hash = missions.Count;
            for (int i = 0; i < missions.Count; i++)
            {
                CorporateMission mission = missions[i];
                hash = hash * 31 + mission.id;
                hash = hash * 31 + (int)mission.state;
                hash = hash * 31 + (int)mission.choice;
                hash = hash * 31 + mission.progress;
                hash = hash * 31 + mission.reward;
                hash = hash * 31 + mission.pendingGoods.Count;
            }

            return hash;
        }
    }

    private static MissionCard BuildCard(CorporateMission mission)
    {
        string description = mission.Description;
        if (mission.kind != CorporateMissionKind.Native)
        {
            description += "\n" + "Mugirl.CQ.Terms".Translate(mission.duration / 60000f, mission.goodwill).ToString();
        }

        if (mission.kind == CorporateMissionKind.Fusion)
        {
            description += "\n" + "Mugirl.CQ.FusionTerms".Translate(mission.reward, CorporateNetwork.MissionConfig.fusionRefusalGoodwill).ToString();
        }

        if (mission.IsSide && mission.choice == CorporateResearchChoice.Undecided)
        {
            description += "\n" + "Mugirl.CQ.ProtectionReward".Translate(CorporateNetwork.FusionProtectionRewardDescription).ToString();
        }

        string progress = mission.kind == CorporateMissionKind.Processing || mission.kind == CorporateMissionKind.Distribution
            ? " · " + "Mugirl.CQ.Progress".Translate(mission.progress, mission.kind == CorporateMissionKind.Distribution ? mission.SalesTarget : mission.count).ToString()
            : string.Empty;
        string time = string.Empty;
        if (mission.state == CorporateMissionState.Available)
        {
            time = "Mugirl.CQ.OfferTime".Translate(((mission.acceptByTick - CorporateNetwork.Now) / 60000f).ToString("0.0"));
        }
        else if (mission.state == CorporateMissionState.Active && mission.kind != CorporateMissionKind.Native)
        {
            time = "Mugirl.CQ.DueTime".Translate(UnityMathf.Max(0f, (mission.deadline - CorporateNetwork.Now) / 60000f).ToString("0.0"));
        }

        Site? site = mission.site;
        return new MissionCard(mission)
        {
            Title = mission.Title,
            Description = description,
            StatusLine = mission.Status + progress + " · " + time,
            LocateSite = site is not null && site.Spawned ? site : null,
            CanTalk = mission.IsSide && mission.state == CorporateMissionState.Active && mission.choice == CorporateResearchChoice.Undecided
                && site?.HasMap == true && site.Map.mapPawns.FreeColonistsSpawned.Any(),
            CanAbandon = mission.state == CorporateMissionState.Active && mission.kind != CorporateMissionKind.Investment
                && mission.kind != CorporateMissionKind.Native,
        };
    }
}
