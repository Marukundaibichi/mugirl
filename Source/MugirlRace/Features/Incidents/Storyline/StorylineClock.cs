namespace Mugirl.Features.Incidents;

// 不持久化的按需唤醒时钟；读档从当前 tick 的前一 tick 起步，首次结算仍是 1 tick。
internal sealed class StorylineClock
{
    private const int StaleCheckIntervalTicks = 250;
    internal int lastTick = -1;
    internal int nextTick = -1;
    internal int nextStaleCheckTick = -1;

    internal bool TryBegin(int currentTick, bool investorActive, out int elapsedTicks, out bool checkInvestor)
    {
        elapsedTicks = 0;
        checkInvestor = false;
        if (lastTick < 0)
        {
            lastTick = currentTick - 1;
        }

        if (nextTick < 0)
        {
            nextTick = currentTick;
        }

        if (nextStaleCheckTick < 0)
        {
            nextStaleCheckTick = currentTick;
        }

        if (nextTick > currentTick)
        {
            return false;
        }

        elapsedTicks = currentTick - lastTick;
        if (elapsedTicks <= 0)
        {
            elapsedTicks = 1;
        }

        lastTick = currentTick;
        if (investorActive && nextStaleCheckTick <= currentTick)
        {
            nextStaleCheckTick = StorylineSchedule.SafeAddTicks(currentTick, StaleCheckIntervalTicks);
            checkInvestor = true;
        }

        return true;
    }
}
