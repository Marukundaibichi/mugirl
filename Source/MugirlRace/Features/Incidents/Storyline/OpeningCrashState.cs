namespace Mugirl.Features.Incidents;

// 剧情事实与剩余倒计时分别保存，不推导或合并独立状态。
internal sealed class OpeningCrashState : IExposable
{
    internal bool started;
    internal int checkRemaining = GameComponent_Storyline.OpeningCrashInitialCheckTicks;
    internal List<Pawn> pawns = new List<Pawn>();

    public void ExposeData()
    {
        Scribe_Values.Look(ref started, "started", false);
        Scribe_Values.Look(ref checkRemaining, "checkRemaining", GameComponent_Storyline.OpeningCrashInitialCheckTicks);
        Scribe_Collections.Look(ref pawns, "pawns", LookMode.Reference);
    }
}
