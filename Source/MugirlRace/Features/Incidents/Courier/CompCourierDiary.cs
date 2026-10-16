using System.Diagnostics.CodeAnalysis;
using Verse.AI;

namespace Mugirl.Features.Incidents;

public class CompCourierDiary : ThingComp
{
    public CompProperties_CourierDiary? Props => props as CompProperties_CourierDiary;

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

    internal static bool CanReadNow([NotNullWhen(true)] Pawn? reader, [NotNullWhen(true)] Thing? diary)
    {
        return reader != null
            && !reader.Dead
            && !reader.Downed
            && diary != null
            && !diary.Destroyed
            && diary.TryGetComp<CompCourierDiary>() != null;
    }

    private static void TryStartReadJob(Pawn reader, Thing diary)
    {
        if (!CanReadNow(reader, diary)
            || !reader.CanReserveAndReach(diary, PathEndMode.Touch, Danger.Deadly))
        {
            return;
        }

        Job job = JobMaker.MakeJob(IncidentsDefOf.Mugirl_ReadCourierDiary, diary);
        reader.jobs.TryTakeOrderedJob(job, JobTag.Misc);
    }
}
