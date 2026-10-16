using Mugirl.Features.Roping;
using RimWorld.Planet;
using Verse.AI;

namespace Mugirl.Features.Incidents;

// 逃跑牧场的完成判定（QuestPart，按类名存档）：全部雪牛娘拴到牧场的拴绳桩上即成功，随机一位加入玩家；
// 任意一位死亡、消失或离开站点地图即失败。判定之间让未被拴住的雪牛娘持续四处乱跑。
public class QuestPart_RunawayFarmCompletion : QuestPartActivable
{
    private const int CheckIntervalTicks = 240;
    // 1.6 每 tick 给每位不在游荡的雪牛娘下一次游荡命令（倒地时每 tick 重建并排队）；改为按 Pawn 错开、每 60 tick 检查一次。
    private const int WanderRefreshIntervalTicks = 60;
    private const int WanderRadius = 12;

    public Site? site;
    public QuestPart_SpawnRunawayFarm? spawnPart;
    public string? failSignal;

    private int ticksUntilNextCheck;
    private bool resolved;

    public override IEnumerable<GlobalTargetInfo> QuestLookTargets
    {
        get
        {
            foreach (GlobalTargetInfo target in base.QuestLookTargets)
            {
                yield return target;
            }

            if (site != null)
            {
                yield return site;
            }
        }
    }

    public override void QuestPartTick()
    {
        base.QuestPartTick();
        if (resolved)
        {
            return;
        }

        ticksUntilNextCheck--;
        if (ticksUntilNextCheck > 0)
        {
            RefreshRunawayMovement();
            return;
        }

        ticksUntilNextCheck = CheckIntervalTicks;
        CheckCompletion();
    }

    private void CheckCompletion()
    {
        List<Pawn>? pawns = spawnPart?.runawayPawns;
        if (pawns == null || pawns.Count == 0)
        {
            return;
        }

        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn pawn = pawns[i];
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned)
            {
                FailQuest();
                return;
            }

            if (pawn.Map == null || site?.Map != pawn.Map)
            {
                FailQuest();
                return;
            }
        }

        for (int i = 0; i < pawns.Count; i++)
        {
            if (!IsRopedToPreparedHitch(pawns[i]))
            {
                return;
            }
        }

        SucceedQuest();
    }

    private bool IsRopedToPreparedHitch(Pawn pawn)
    {
        if (!RopingService.IsRopedToSpot(pawn) || pawn.roping?.RopedTo.IsValid != true)
        {
            return false;
        }

        List<Thing>? preparedHitches = spawnPart?.hitches;
        if (preparedHitches == null || preparedHitches.Count == 0)
        {
            return false;
        }

        IntVec3 ropedCell = pawn.roping.RopedTo.Cell;
        for (int i = 0; i < preparedHitches.Count; i++)
        {
            Thing hitch = preparedHitches[i];
            if (hitch != null && !hitch.Destroyed && hitch.Spawned && hitch.Position == ropedCell)
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshRunawayMovement()
    {
        List<Pawn>? pawns = spawnPart?.runawayPawns;
        if (pawns == null)
        {
            return;
        }

        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn pawn = pawns[i];
            if (pawn == null || pawn.Dead || !pawn.Spawned || !pawn.IsHashIntervalTick(WanderRefreshIntervalTicks))
            {
                continue;
            }

            // 被拴住、倒地或处于精神状态时不打断；其余时间只要没在游荡就重新下令。
            if (pawn.Downed || pawn.InMentalState || RopingService.HasAnyRope(pawn))
            {
                continue;
            }

            if (pawn.CurJob == null || pawn.jobs?.curDriver == null || pawn.CurJob.def != JobDefOf.GotoWander)
            {
                RunawayWanderUtility.TryForceWander(pawn, pawn.Position, WanderRadius);
            }
        }
    }

    private void FailQuest()
    {
        if (resolved)
        {
            return;
        }

        resolved = true;
        if (!string.IsNullOrEmpty(failSignal))
        {
            GameUtility.TrySendSignal(failSignal);
        }
    }

    private void SucceedQuest()
    {
        if (resolved)
        {
            return;
        }

        resolved = true;
        Pawn? reward = TryChooseRewardPawn();
        if (reward != null && JoinRewardPawn(reward))
        {
            GameUtility.TrySignalForceNormalSpeedShort();
            GameUtility.TryReceiveLetter(
                "Mugirl.RunawayFarm.SuccessLetterLabel".Translate(),
                "Mugirl.RunawayFarm.SuccessLetterText".Translate(reward.Named("PAWN")),
                LetterDefOf.PositiveEvent,
                reward);
        }

        CleanupRunaways();
        Complete();
        RemoveQuestSite();
    }

    private Pawn? TryChooseRewardPawn()
    {
        List<Pawn>? pawns = spawnPart?.runawayPawns;
        if (pawns == null)
        {
            return null;
        }

        List<Pawn> candidates = new List<Pawn>();
        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn pawn = pawns[i];
            if (pawn != null
                && !pawn.Destroyed
                && !pawn.Dead
                && pawn.Spawned
                && !PlayerFactionUtility.IsPlayerFaction(pawn.Faction))
            {
                candidates.Add(pawn);
            }
        }

        return candidates.Count == 0 ? null : candidates.RandomElement();
    }

    // 玩家仍有 Pawn 在站点时保留地图，由玩家自行离开。
    private void RemoveQuestSite()
    {
        if (site?.HasMap == true && !HasPlayerControlledPawn(site.Map))
        {
            GameUtility.TryDeinitAndRemoveMap(site.Map, notifyPlayer: true);
        }

        QuestPart_DestroyWorldObject.TryRemove(site);
    }

    private static bool HasPlayerControlledPawn(Map? map)
    {
        IReadOnlyList<Pawn>? pawns = map?.mapPawns?.AllPawnsSpawned;
        if (pawns == null)
        {
            return false;
        }

        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn pawn = pawns[i];
            if (pawn != null && !pawn.Destroyed && (PlayerFactionUtility.IsPlayerFaction(pawn.Faction) || PlayerFactionUtility.IsPlayerFaction(pawn.HostFaction)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool JoinRewardPawn(Pawn pawn)
    {
        if (pawn.Destroyed || pawn.Dead)
        {
            return false;
        }

        // 1.6 还会写入只写不读的 "Mugirl_RunawayReward" 标签，它会一直留在殖民者身上参与任务信号广播，1.7 不再写入。
        EventPawnTags.ClearRunawayPanic(pawn);
        EventPawnTags.ClearTemporaryEventTags(pawn);
        RopingService.BreakAllRopesAndNotify(pawn);
        RopingService.ClearPendingSpotRope(pawn);
        pawn.jobs?.ClearQueuedJobs();
        if (pawn.CurJob != null)
        {
            pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
        }

        if (!PlayerFactionUtility.TrySetPlayerFaction(pawn))
        {
            return false;
        }

        // SetFaction 补丁通常已清理过；补丁未能应用时这里仍保证事件状态被清除（重复调用无副作用）。
        WildMugirlUtility.CleanupAfterJoiningPlayer(pawn);
        return PlayerFactionUtility.IsPlayerFaction(pawn.Faction);
    }

    private void CleanupRunaways()
    {
        List<Pawn>? pawns = spawnPart?.runawayPawns;
        if (pawns == null)
        {
            return;
        }

        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn pawn = pawns[i];
            if (pawn == null || pawn.Destroyed)
            {
                continue;
            }

            EventPawnTags.ClearRunawayPanic(pawn);
            EventPawnTags.ClearTemporaryEventTags(pawn);
            spawnPart?.ClearProtectedTag(pawn);
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref site, "site");
        Scribe_References.Look(ref spawnPart, "spawnPart");
        Scribe_Values.Look(ref failSignal, "failSignal");
        Scribe_Values.Look(ref ticksUntilNextCheck, "ticksUntilNextCheck", 0);
        Scribe_Values.Look(ref resolved, "resolved", false);
    }
}
