namespace Mugirl.Features.Corporate;

// 开户过程的完整事实集。保留独立标志，不把尚未完成的分支压缩成会丢信息的单一阶段。
internal sealed class CorporateContactState : IExposable
{
    internal bool attached;
    internal bool retrofitted;
    internal bool visitPending;
    internal bool accountOpened;
    internal bool usesIndependentContact;
    internal bool giftBundlePrepared;
    internal bool independentlyAggressive;
    internal bool playerKilledCourier;
    internal bool courierDead;
    internal int courierOutcome;
    internal int lastContactTick = -1;
    internal int serviceResumeTick;
    internal Pawn? courierPawn;
    internal Pawn? representativePawn;
    internal Map? visitMap;
    internal Quest? quest;
    internal int visitDueTick = -1;

    public void ExposeData()
    {
        Scribe_Values.Look(ref attached, "attached", false);
        Scribe_Values.Look(ref retrofitted, "retrofitted", false);
        Scribe_Values.Look(ref visitPending, "visitPending", false);
        Scribe_Values.Look(ref accountOpened, "accountOpened", false);
        Scribe_Values.Look(ref usesIndependentContact, "usesIndependentContact", false);
        Scribe_Values.Look(ref giftBundlePrepared, "giftBundlePrepared", false);
        Scribe_Values.Look(ref independentlyAggressive, "independentlyAggressive", false);
        Scribe_Values.Look(ref playerKilledCourier, "playerKilledCourier", false);
        Scribe_Values.Look(ref courierDead, "courierDead", false);
        Scribe_Values.Look(ref courierOutcome, "courierOutcome", 0);
        Scribe_Values.Look(ref lastContactTick, "lastContactTick", -1);
        Scribe_Values.Look(ref serviceResumeTick, "serviceResumeTick", 0);
        Scribe_Values.Look(ref visitDueTick, "visitDueTick", -1);
        Scribe_References.Look(ref courierPawn, "courierPawn");
        Scribe_References.Look(ref representativePawn, "representativePawn");
        Scribe_References.Look(ref visitMap, "visitMap");
        Scribe_References.Look(ref quest, "quest");
    }
}
