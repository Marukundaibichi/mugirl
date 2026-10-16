using System.Diagnostics.CodeAnalysis;
using Verse.AI;

namespace Mugirl.Features.Social;

/// <summary>走到同伴身边蹭蹭（XML driverClass，按类名存档）。Toil 结构沿用原版 RimWorld.JobDriver_Nuzzle。</summary>
public class JobDriver_MugirlNuzzle : JobDriver
{
    private Pawn? Recipient => job.GetTarget(TargetIndex.A).Thing as Pawn;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Pawn? recipient = Recipient;
        return CanNuzzle(pawn, recipient)
            && pawn.Reserve(recipient, job, 1, -1, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
        this.FailOnNotCasualInterruptible(TargetIndex.A);
        this.FailOn(() => !CanNuzzle(pawn, Recipient));
        // 原版在这里还新建了一个设置 socialMode 却从未返回的走路 Toil（无效代码，1.6 照抄）；走路仍用默认社交模式，与原版行为一致。
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch, false);
        yield return Toils_Interpersonal.WaitToBeAbleToInteract(pawn);
        yield return Toils_General.Do(delegate
        {
            Pawn? recipient = Recipient;
            if (CanNuzzle(pawn, recipient))
            {
                pawn.interactions.TryInteractWith(recipient, SocialDefOf.Mugirl_NuzzleInteraction);
            }
        });
    }

    private static bool CanNuzzle([NotNullWhen(true)] Pawn? actor, [NotNullWhen(true)] Pawn? recipient)
    {
        return actor is not null
            && recipient is not null
            && actor != recipient
            && !actor.Dead
            && !actor.Downed
            && actor.Spawned
            && recipient.Spawned
            && !recipient.Dead
            && recipient.CanCasuallyInteractNow(false, false, false, false);
    }
}
