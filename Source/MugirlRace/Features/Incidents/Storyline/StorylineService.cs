using Mugirl.Features.Corporate;

namespace Mugirl.Features.Incidents;

// 剧情调度：开局坠舱、一次性运货员来访与牛聚变投资计时。只在有事可做的 tick 唤醒，其余 tick 只比较一次 tick 号。
internal static class StorylineService
{
    private const int OpeningCrashRetryTicks = 1000;
    private const float OpeningCrashQuestPoints = 10000f;
    private const float CourierRaidQuestPoints = 200f;
    private const int CourierRaidRetryTicks = 60000;
    private const float OpeningCrashMinDaysPassed = 1f;

    internal static void Tick(GameComponent_Storyline? state)
    {
        if (state == null || !GameUtility.IsPlaying())
        {
            return;
        }

        if (!TickUtility.TryGetCurrentGameTick(out int currentTick))
        {
            return;
        }

        if (!state.Clock.TryBegin(currentTick, state.Investment.investorActive, out int elapsedTicks, out bool checkFusionInvestor))
        {
            return;
        }

        TickCourierRaid(state, elapsedTicks);
        TickOpeningCrash(state, elapsedTicks);
        FusionInvestmentUtility.Tick(state, elapsedTicks, checkFusionInvestor);
        ScheduleNextWake(state, currentTick);
    }

    // 开局坠舱是否允许自动触发（设置开关与第一天之后）；调用方已确认尚未触发。
    internal static bool IsOpeningCrashAllowed()
    {
        return MugirlMod.Settings?.enableOpeningStoryline == true
            && GenDate.DaysPassedFloat >= OpeningCrashMinDaysPassed;
    }

    private static void TickOpeningCrash(GameComponent_Storyline state, int elapsedTicks)
    {
        if (state.OpeningCrash.started)
        {
            state.OpeningCrash.checkRemaining = -1;
            return;
        }

        if (!StorylineSchedule.AdvanceTimer(ref state.OpeningCrash.checkRemaining, elapsedTicks))
        {
            return;
        }

        if (GameUtility.TryGetFirstFactionOfDef(CorporateDefOf.Mugirl_GiantCorporation, out _)
            && GameUtility.TryResolvePlayerEventMap(out Map? map)
            && IsOpeningCrashAllowed())
        {
            // 只有真正投放了救援对象（任务含 QuestPart_JoinOnRescue）才算触发；生成或投放失败时不发布空任务，
            // 也不烧掉一次性开场事件，稍后换一批随机结果重试。
            if (QuestLauncher.TryLaunch<QuestPart_JoinOnRescue>(
                    IncidentsDefOf.Mugirl_SlaveOpeningPodCrash, map, OpeningCrashQuestPoints) != null)
            {
                state.MarkOpeningCrashStarted();
                return;
            }
        }

        state.OpeningCrash.checkRemaining = OpeningCrashRetryTicks;
    }

    private static void TickCourierRaid(GameComponent_Storyline state, int elapsedTicks)
    {
        if (state.Courier.triggered || !state.OpeningCrash.started)
        {
            return;
        }

        if (state.Courier.delayRemaining > GameComponent_Storyline.CourierRaidInitialDelayTicks)
        {
            state.Courier.delayRemaining = GameComponent_Storyline.CourierRaidInitialDelayTicks;
        }

        if (!StorylineSchedule.AdvanceTimer(ref state.Courier.delayRemaining, elapsedTicks))
        {
            return;
        }

        if (state.Courier.questStarted)
        {
            state.Courier.triggered = true;
            return;
        }

        bool hasGiantCorp = GameUtility.TryGetFirstFactionOfDef(CorporateDefOf.Mugirl_GiantCorporation, out _);
        if (!hasGiantCorp || !GameUtility.TryResolvePlayerEventMap(out Map? map))
        {
            state.Courier.delayRemaining = CourierRaidRetryTicks;
            return;
        }

        // 任务根节点找不到入场格等情况下不会生成 QuestPart_SpawnCourier；1.6 仍会标记已触发，一次性来访因此永久丢失。
        if (QuestLauncher.TryLaunch<QuestPart_SpawnCourier>(IncidentsDefOf.Mugirl_CourierContact, map, CourierRaidQuestPoints) == null)
        {
            state.Courier.delayRemaining = CourierRaidRetryTicks;
            return;
        }

        state.MarkCourierRaidStarted();
    }

    private static void ScheduleNextWake(GameComponent_Storyline state, int currentTick)
    {
        int delayTicks = int.MaxValue;

        if (!state.OpeningCrash.started)
        {
            StorylineSchedule.IncludeWakeDelay(ref delayTicks, state.OpeningCrash.checkRemaining);
        }

        if (!state.Courier.triggered && state.OpeningCrash.started)
        {
            StorylineSchedule.IncludeWakeDelay(ref delayTicks, state.Courier.delayRemaining);
        }

        if (state.Investment.pending)
        {
            StorylineSchedule.IncludeWakeDelay(ref delayTicks, state.Investment.resultRemaining);
        }

        if (state.Investment.investorActive)
        {
            StorylineSchedule.IncludeWakeDelay(ref delayTicks, state.Clock.nextStaleCheckTick - currentTick);
        }

        state.Clock.nextTick = StorylineSchedule.NextWakeTick(currentTick, delayTicks);
    }
}
