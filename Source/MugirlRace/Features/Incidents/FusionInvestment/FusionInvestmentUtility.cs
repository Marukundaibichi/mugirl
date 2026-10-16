using System.Diagnostics.CodeAnalysis;
using Mugirl.Features.Corporate;
using Verse.AI.Group;

namespace Mugirl.Features.Incidents;

// 牛聚变投资：投资人来访 → 玩家投资（扣白银）→ 两到四周后空投奶制食品与野生雪牛娘。状态存于 GameComponent_Storyline。
internal static class FusionInvestmentUtility
{
    internal static readonly IntRange ResultDelayTicks = new IntRange(GenDate.TicksPerDay * 14, GenDate.TicksPerDay * 28);

    private const int ReofferDelayTicks = GenDate.TicksPerDay * 60;
    private const int PostponeDelayTicks = GenDate.TicksPerDay * 7;
    private const int InvestorWaitTicks = GenDate.TicksPerDay * 6;
    private const int InvestorWaitCellRadius = 20;

    internal static bool TrySpawnInvestor(Map? map, [NotNullWhen(true)] out Pawn? investor)
    {
        investor = null;
        if (map == null || !RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 spawnCell, map, 0f))
        {
            return false;
        }

        // 没有可用的非敌对派系时生成无派系投资人（与 1.6 相同）。
        GameUtility.TryGetRandomNonHostileFaction(out Faction? faction, allowHidden: false, minTechLevel: TechLevel.Medieval);
        Pawn? generated = PawnGenerator.GeneratePawn(EventPawnRequestUtility.CreateVisitorRequest(faction, map.Tile));
        if (generated == null)
        {
            return false;
        }

        EventPawnTags.MarkFusionInvestor(generated);
        GenSpawn.Spawn(generated, spawnCell, map);
        if (!generated.Spawned)
        {
            GeneratedPawnUtility.Discard(generated);
            return false;
        }

        IntVec3 waitCell = FindInvestorWaitCell(map);
        LordMaker.MakeNewLord(
            generated.Faction,
            new LordJob_WaitForDurationThenExit(waitCell, InvestorWaitTicks),
            map,
            Gen.YieldSingle(generated));

        if (GameUtility.TryGetGameComponent<GameComponent_Storyline>(out GameComponent_Storyline? state))
        {
            state.SetFusionInvestor(generated);
        }

        investor = generated;
        return true;
    }

    internal static void ScheduleNextOffer()
    {
        ScheduleNextOffer(ReofferDelayTicks);
    }

    internal static bool HasSilver(Map map, int amount) => EventSilverLedger.HasSilver(map, amount);

    internal static bool TryAcceptInvestment(Map targetMap, int amount, Pawn? investor)
    {
        if (!TryTakeSilver(targetMap, amount))
        {
            Messages.Message("Mugirl.FusionInvestment.MessageNoSilver".Translate(amount), MessageTypeDefOf.RejectInput, historical: false);
            return false;
        }

        if (GameUtility.TryGetGameComponent<GameComponent_Storyline>(out GameComponent_Storyline? state))
        {
            state.AcceptFusionInvestment(amount, ResultDelayTicks.RandomInRange);
        }

        Messages.Message("Mugirl.FusionInvestment.MessageAccepted".Translate(amount), MessageTypeDefOf.PositiveEvent);
        CorporateNetwork.Current?.RememberFusionInvestor(investor);
        ResolveInvestor(investor);
        return true;
    }

    internal static void RejectInvestment(Pawn? investor)
    {
        ScheduleNextOffer();
        Messages.Message("Mugirl.FusionInvestment.MessageRejected".Translate(), MessageTypeDefOf.NeutralEvent, historical: false);
        ResolveInvestor(investor);
    }

    internal static void NotifyInvestorAttacked(Pawn? investor)
    {
        if (!EventPawnTags.IsFusionInvestor(investor))
        {
            return;
        }

        if (GameUtility.TryGetGameComponent<GameComponent_Storyline>(out GameComponent_Storyline? state))
        {
            state.ClearFusionInvestor();
            state.WakeStoryService();
            if (!state.Investment.accepted && !state.Investment.pending)
            {
                ScheduleNextOffer(PostponeDelayTicks);
            }
        }

        EventPawnTags.ClearFusionInvestor(investor);
        EventLordUtility.ExitMapJogging(investor);
    }

    internal static void DevDeliverPendingResult()
    {
        if (!GameUtility.TryGetGameComponent<GameComponent_Storyline>(out GameComponent_Storyline? state) || !state.Investment.pending)
        {
            Messages.Message("Mugirl.FusionInvestment.DevDeliver.NoPending".Translate(), MessageTypeDefOf.RejectInput, historical: false);
            return;
        }

        state.Investment.resultRemaining = 0;
        FusionInvestmentRewards.DeliverResult(state);
    }

    internal static bool TryTakeSilver(Map map, int amount) => EventSilverLedger.TryTakeSilver(map, amount);

    internal static void Tick(GameComponent_Storyline? state, int elapsedTicks, bool checkInvestor)
    {
        if (state == null)
        {
            return;
        }

        if (checkInvestor)
        {
            ClearStaleInvestor(state);
        }

        if (!state.Investment.pending)
        {
            return;
        }

        state.Investment.resultRemaining -= elapsedTicks > 0 ? elapsedTicks : 1;
        if (state.Investment.resultRemaining > 0)
        {
            return;
        }

        FusionInvestmentRewards.DeliverResult(state);
    }

    // 每帧为每个可见 Pawn 调用（问号覆盖层）：先查标签，绝大多数 Pawn 在第一步就返回。
    internal static bool CanTalkToInvestor([NotNullWhen(true)] Pawn? investor)
    {
        if (!EventPawnTags.IsFusionInvestor(investor)
            || !investor.Spawned
            || investor.Dead
            || investor.Downed
            || investor.InAggroMentalState)
        {
            return false;
        }

        return GameUtility.TryGetGameComponent<GameComponent_Storyline>(out GameComponent_Storyline? state)
            && state.Investment.investorActive
            && !state.Investment.accepted
            && !state.Investment.pending
            && (state.Investment.investor == null || state.Investment.investor == investor);
    }

    internal static bool CanNegotiateWithInvestor(Pawn? negotiator, [NotNullWhen(true)] Pawn? investor)
    {
        return negotiator != null
            && PlayerFactionUtility.IsPlayerFaction(negotiator.Faction)
            && negotiator.RaceProps?.Humanlike == true
            && !negotiator.Dead
            && !negotiator.Downed
            && CanTalkToInvestor(investor);
    }

    internal static void ShowDialog(Pawn? investor, Pawn negotiator) => FusionInvestmentDialog.ShowDialog(investor, negotiator);

    private static IntVec3 FindInvestorWaitCell(Map map)
    {
        if (CellFinder.TryFindRandomCellNear(map.Center, map, InvestorWaitCellRadius, c => c.Standable(map) && !c.Fogged(map), out IntVec3 cell))
        {
            return cell;
        }

        return map.Center;
    }

    private static void ScheduleNextOffer(int delayTicks)
    {
        if (GameUtility.TryGetGameComponent<GameComponent_Storyline>(out GameComponent_Storyline? state))
        {
            int currentTick = TickUtility.CurrentGameTickOrFallback(state.Investment.nextOfferTick);
            state.Investment.nextOfferTick = currentTick + delayTicks;
        }
    }

    private static void ResolveInvestor(Pawn? investor)
    {
        if (GameUtility.TryGetGameComponent<GameComponent_Storyline>(out GameComponent_Storyline? state))
        {
            state.ClearFusionInvestor();
            state.WakeStoryService();
        }

        if (investor == null)
        {
            return;
        }

        EventPawnTags.ClearFusionInvestor(investor);
        EventLordUtility.ExitMapJogging(investor);
    }

    // 投资人离图、死亡或倒地后清除记录；尚未投资时一周后再来。
    private static void ClearStaleInvestor(GameComponent_Storyline state)
    {
        if (!state.Investment.investorActive)
        {
            return;
        }

        Pawn? investor = state.Investment.investor;
        if (investor != null && investor.Spawned && !investor.Dead && !investor.Downed && EventPawnTags.IsFusionInvestor(investor))
        {
            return;
        }

        state.ClearFusionInvestor();
        if (!state.Investment.accepted && !state.Investment.pending)
        {
            ScheduleNextOffer(PostponeDelayTicks);
        }
    }

}
