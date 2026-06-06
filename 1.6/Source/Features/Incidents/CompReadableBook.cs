using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class CompProperties_ReadableBook : CompProperties
    {
        public string bookTitle = "MooGirl.CourierDiary.Title";

        public CompProperties_ReadableBook()
        {
            compClass = typeof(CompReadableBook);
        }
    }

    public class CompReadableBook : ThingComp
    {
        public CompProperties_ReadableBook Props => (CompProperties_ReadableBook)props;

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            string title = MooGirlText.Resolve(Props.bookTitle);
            FloatMenuOption option = FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption("MooGirl.CourierDiary.ReadOption".Translate(title), () =>
            {
                Job job = JobMaker.MakeJob(MooGirlContentDefOf.MooGirl_ReadCourierDiary, parent);
                selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }), selPawn, parent);

            if (!selPawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Deadly))
            {
                option.Disabled = true;
                option.Label = "MooGirl.CourierDiary.OptionDisabled".Translate(option.Label, "MooGirl.CourierDiary.CannotReach".Translate());
            }

            yield return option;
        }
    }

    public class JobDriver_ReadCourierDiary : JobDriver
    {
        private Thing Diary => job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Diary, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A);

            yield return Toils_General.Do(() =>
            {
                CompReadableBook comp = Diary.TryGetComp<CompReadableBook>();
                if (comp != null)
                {
                    Find.WindowStack.Add(new Dialog_ReadBook(MooGirlText.Resolve(comp.Props.bookTitle), Diary));
                }
            });
        }
    }
}
