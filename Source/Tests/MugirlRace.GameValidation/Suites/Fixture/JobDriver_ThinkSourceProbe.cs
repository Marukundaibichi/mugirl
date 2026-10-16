using Verse.AI;

namespace Mugirl.GameValidation.Suites;

/// <summary>外部 JobDriver 的隔离替身：保存自有进度，检测读取是否重放初始化；不模拟卫生模组玩法。</summary>
internal sealed class JobDriver_ThinkSourceProbe : JobDriver
{
    internal int initCount;
    internal int workDone;

    public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref initCount, "initCount");
        Scribe_Values.Look(ref workDone, "workDone");
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        for (int i = 0; i < 3; i++)
        {
            var toil = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = 500,
                initAction = () => initCount++,
                tickAction = () => workDone++,
            };
            yield return toil;
        }
    }
}
