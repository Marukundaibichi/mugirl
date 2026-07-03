using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public class CompProperties_ReadableBook : CompProperties
    {
        public string bookTitle = "Mugirl.CourierDiary.Title";

        public CompProperties_ReadableBook()
        {
            compClass = typeof(CompReadableBook);
        }
    }

    public class CompReadableBook : ThingComp
    {
        public CompProperties_ReadableBook Props => props as CompProperties_ReadableBook;

        public string BookTitle => MugirlText.Resolve(Props?.bookTitle ?? "Mugirl.CourierDiary.Title");

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            if (selPawn == null || parent == null)
            {
                yield break;
            }

            string title = BookTitle;
            FloatMenuOption option = FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption("Mugirl.CourierDiary.ReadOption".Translate(title), () =>
            {
                TryStartReadJob(selPawn, parent);
            }), selPawn, parent);

            if (!selPawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Deadly))
            {
                option.Disabled = true;
                option.Label = "Mugirl.CourierDiary.OptionDisabled".Translate(option.Label, "Mugirl.CourierDiary.CannotReach".Translate());
            }

            yield return option;
        }

        internal static bool CanReadNow(Pawn reader, Thing diary)
        {
            return reader != null
                && !reader.Dead
                && !reader.Downed
                && diary != null
                && !diary.Destroyed
                && diary.TryGetComp<CompReadableBook>() != null;
        }

        private static void TryStartReadJob(Pawn reader, Thing diary)
        {
            if (!CanReadNow(reader, diary)
                || !reader.CanReserveAndReach(diary, PathEndMode.Touch, Danger.Deadly))
            {
                return;
            }

            Job job = JobMaker.MakeJob(MugirlContentDefOf.Mugirl_ReadCourierDiary, diary);
            reader.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }

    public class JobDriver_ReadCourierDiary : JobDriver
    {
        private Thing Diary => job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Thing diary = Diary;
            return CompReadableBook.CanReadNow(pawn, diary)
                && pawn.Reserve(diary, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !CompReadableBook.CanReadNow(pawn, Diary));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A);

            yield return Toils_General.Do(() =>
            {
                Thing diary = Diary;
                CompReadableBook comp = diary?.TryGetComp<CompReadableBook>();
                if (comp != null)
                {
                    MugirlGameUtility.TryAddWindow(new Dialog_ReadBook(comp.BookTitle));
                }
            });
        }
    }
}
