using Verse.AI;

namespace Mugirl.Features.Corporate;

/// <summary>
/// 走到巨企代表身边并打开介绍对白。类型名由 1.7/Defs 的 driverClass 引用，并作为 curDriver 的类名写入存档，不能改名或移动命名空间。
/// </summary>
public class JobDriver_CorporateTalkRepresentative : JobDriver
{
    private Pawn? Representative => job.GetTarget(TargetIndex.A).Thing as Pawn;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Pawn? representative = Representative;
        return representative is not null
            && CorporateIntroduction.Current?.CanTalk(pawn, representative) == true
            && pawn.Reserve(representative, job, 1, -1, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(TargetIndex.A);
        this.FailOn(() => Representative is not Pawn representative || CorporateIntroduction.Current?.CanTalk(pawn, representative) != true);
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
        yield return Toils_General.Do(() =>
        {
            if (Representative is Pawn representative)
            {
                CorporateIntroduction.Current?.ShowDialog(pawn, representative);
            }
        });
    }
}
