using RimWorld.Planet;

namespace Mugirl.Features.Corporate;

public class QuestPart_CorporateIntroduction : QuestPart
{
    public override string DescriptionPart
    {
        get
        {
            string text = "Mugirl.CorporateIntro.QuestProgress".Translate();
            CorporateIntroduction? introduction = CorporateIntroduction.Current;
            int remaining = introduction?.RemainingRepresentativeDelay ?? 0;
            if (remaining > 0)
            {
                text += "\n" + (introduction?.IndependentContact == true
                    ? "Mugirl.CorporateIntro.Independent.QuestProgressWait".Translate(GenDate.ToStringTicksToPeriod(remaining))
                    : "Mugirl.CorporateIntro.QuestProgressWait".Translate(GenDate.ToStringTicksToPeriod(remaining)));
            }

            return text;
        }
    }

    public override IEnumerable<GlobalTargetInfo> QuestLookTargets
    {
        get
        {
            Pawn? pawn = CorporateIntroduction.Current?.Representative;
            if (pawn?.Spawned == true)
            {
                yield return pawn;
            }
        }
    }

    public override bool QuestPartReserves(Pawn pawn)
    {
        CorporateIntroduction? introduction = CorporateIntroduction.Current;
        return introduction?.Completed != true && pawn == introduction?.Representative;
    }
}
