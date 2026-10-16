using Verse.AI;

namespace Mugirl.Features.Corporate;

// 预约成功后才暂停伤员；接近、取背包医药和包扎共用同一等待任务。
// 等待被玩家命令或紧急行为打断时立即结束治疗，避免重新追逐移动中的目标。
// 类名由 JobDef driverClass 引用并随 Job 写入存档。
public class JobDriver_CorporateSupportTend : JobDriver_TendPatient
{
    private int patientWaitJobId = -1;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref patientWaitJobId, "patientWaitJobId", -1);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
        AddFailCondition(() => patientWaitJobId >= 0 && TargetPawnA?.CurJob?.loadID != patientWaitJobId);
        AddFinishAction(_ => ReleasePatient());
        Toil waitForDoctor = ToilMaker.MakeToil("CorporatePatientWait");
        waitForDoctor.initAction = delegate
        {
            Pawn? patient = TargetPawnA;
            if (patient == null || !CorporateSupportUtility.CanAcceptFieldTending(pawn, patient))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            if (patient == pawn || patient.Downed || patient.InBed())
            {
                return;
            }

            PawnUtility.ForceWait(patient, GenDate.TicksPerHour, pawn, maintainPosture: true, maintainSleep: true);
            patientWaitJobId = patient.CurJob?.loadID ?? -1;
            if (patientWaitJobId < 0)
            {
                EndJobWith(JobCondition.Incompletable);
            }
        };
        waitForDoctor.defaultCompleteMode = ToilCompleteMode.Instant;
        yield return waitForDoctor;
        foreach (Toil toil in base.MakeNewToils())
        {
            yield return toil;
        }
    }

    private void ReleasePatient()
    {
        Pawn? patient = TargetPawnA;
        if (patientWaitJobId < 0 || patient == null || patient.CurJob?.loadID != patientWaitJobId)
        {
            return;
        }

        patientWaitJobId = -1;
        patient.jobs.EndCurrentJob(JobCondition.InterruptForced);
    }
}
