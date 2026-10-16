using Verse.AI;

namespace Mugirl.Features.Incidents;

// XML driverClass，类名写进存档，不能改名。走到日记旁打开阅读窗口。
public class JobDriver_ReadCourierDiary : JobDriver
{
    private Thing? Diary => job.GetTarget(TargetIndex.A).Thing;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Thing? diary = Diary;
        return CompCourierDiary.CanReadNow(pawn, diary)
            && pawn.Reserve(diary, job, 1, -1, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
        this.FailOn(() => !CompCourierDiary.CanReadNow(pawn, Diary));

        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
            .FailOnSomeonePhysicallyInteracting(TargetIndex.A);

        yield return Toils_General.Do(() =>
        {
            CompCourierDiary? comp = Diary?.TryGetComp<CompCourierDiary>();
            if (comp != null)
            {
                GameUtility.TryAddWindow(new Dialog_CourierDiary(comp.BookTitle));
            }
        });
    }
}
