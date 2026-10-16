namespace Mugirl.Features.Incidents;

// 剧情事实与剩余倒计时分别保存，不推导或合并独立状态。
internal sealed class CourierContactState : IExposable
{
    internal bool triggered;
    internal int delayRemaining = GameComponent_Storyline.CourierRaidInitialDelayTicks;
    internal bool questStarted;

    public void ExposeData()
    {
        Scribe_Values.Look(ref triggered, "triggered", false);
        Scribe_Values.Look(ref delayRemaining, "delayRemaining", GameComponent_Storyline.CourierRaidInitialDelayTicks);
        Scribe_Values.Look(ref questStarted, "questStarted", false);
    }
}
