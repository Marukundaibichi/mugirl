using Verse.AI;

namespace Mugirl.Features.Corporate;

/// <summary>右键巨企代表时提供“交谈”选项（原版按类型自动实例化 FloatMenuOptionProvider 的子类）。</summary>
public class FloatMenuOptionProvider_CorporateRepresentative : FloatMenuOptionProvider
{
    protected override bool Drafted => true;

    protected override bool Undrafted => true;

    protected override bool Multiselect => false;

    protected override bool RequiresManipulation => false;

    public override bool TargetPawnValid(Pawn target, FloatMenuContext context)
    {
        return base.TargetPawnValid(target, context) && CorporateIntroduction.Current?.CanTalk(context.FirstSelectedPawn, target) == true;
    }

    public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
    {
        Pawn actor = context.FirstSelectedPawn;
        if (CorporateIntroduction.Current?.CanTalk(actor, clickedPawn) != true)
        {
            yield break;
        }

        FloatMenuOption option = new("Mugirl.CorporateIntro.Talk".Translate(), () =>
        {
            if (CorporateIntroduction.Current?.CanTalk(actor, clickedPawn) == true)
            {
                actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(CorporateDefOf.Mugirl_CorporateTalkRepresentative, clickedPawn), JobTag.Misc);
            }
        }, MenuOptionPriority.High);
        if (!actor.CanReserveAndReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
        {
            option.Disabled = true;
            option.Label += ": " + "Mugirl.CorporateUI.NoPath".Translate();
        }

        yield return FloatMenuUtility.DecoratePrioritizedTask(option, actor, clickedPawn);
    }
}
