namespace Mugirl.Features.Mounting;

// 每个任务保留自己的累计余量；禁用任务时仍累计，重新启用后传递完整 elapsed。
internal sealed class MountTickSchedule : IExposable
{
    internal int physiology;
    internal int safety;
    internal int turret;
    internal int melee;

    internal void Add(int delta)
    {
        physiology += delta;
        safety += delta;
        turret += delta;
        melee += delta;
    }

    internal bool ConsumePhysiology(int interval, out int elapsed) => ConsumeReady(ref physiology, interval, out elapsed);
    internal bool ConsumeSafety(int interval, out int elapsed) => ConsumeReady(ref safety, interval, out elapsed);
    internal bool ConsumeTurret(int interval, out int elapsed) => ConsumeReady(ref turret, interval, out elapsed);
    internal bool ConsumeMelee(int interval, out int elapsed) => ConsumeReady(ref melee, interval, out elapsed);

    private static bool ConsumeReady(ref int counter, int interval, out int elapsed)
    {
        if (counter < interval)
        {
            elapsed = 0;
            return false;
        }

        elapsed = counter;
        counter = 0;
        return true;
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref physiology, "physiology", 0);
        Scribe_Values.Look(ref safety, "safety", 0);
        Scribe_Values.Look(ref turret, "turret", 0);
        Scribe_Values.Look(ref melee, "melee", 0);
    }
}
