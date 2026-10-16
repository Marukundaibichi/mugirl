using Verse.AI;

namespace Mugirl.Core;

// 共享预留、走到目标 Pawn 身边和打开对话窗口的两段流程；具体交涉条件由功能模块提供。
public abstract class JobDriver_TalkToEventPawn : JobDriver
{
    protected Pawn? TalkTarget => job.GetTarget(TargetIndex.A).Thing as Pawn;

    protected abstract bool CanNegotiate(Pawn negotiator, Pawn? target);

    /// <summary>打开对话；目标已失效时由实现给出“已处理”提示。</summary>
    protected abstract void ShowDialog(Pawn? target, Pawn negotiator);

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Pawn? target = TalkTarget;
        return target != null
            && CanNegotiate(pawn, target)
            && pawn.Reserve(target, job, 1, -1, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(TargetIndex.A);
        this.FailOn(() => !CanNegotiate(pawn, TalkTarget));

        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
        yield return Toils_General.Do(() => ShowDialog(TalkTarget, pawn));
    }
}
