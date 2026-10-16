namespace Mugirl.Features.Incidents;

// IncidentDef Mugirl_FusionInvestment：投资人来访（未投资、无进行中的投资、无在场投资人且冷却结束时）。
public class IncidentWorker_FusionInvestment : IncidentWorker
{
    protected override bool CanFireNowSub(IncidentParms parms)
    {
        if (!base.CanFireNowSub(parms) || MugirlMod.Settings?.enableFusionInvestmentEvent != true)
        {
            return false;
        }

        if (parms.target is not Map)
        {
            return false;
        }

        if (!GameUtility.TryGetGameComponent<GameComponent_Storyline>(out GameComponent_Storyline? state))
        {
            return false;
        }

        if (!TickUtility.TryGetCurrentGameTick(out int currentTick))
        {
            return false;
        }

        return !state.Investment.accepted
            && !state.Investment.pending
            && !state.Investment.investorActive
            && currentTick >= state.Investment.nextOfferTick;
    }

    protected override bool TryExecuteWorker(IncidentParms parms)
    {
        if (parms.target is not Map map || !FusionInvestmentUtility.TrySpawnInvestor(map, out Pawn? investor))
        {
            return false;
        }

        GameUtility.TryReceiveLetter(
            "Mugirl.FusionInvestment.LetterLabel".Translate(),
            "Mugirl.FusionInvestment.LetterText".Translate(),
            LetterDefOf.NeutralEvent,
            investor);
        GameUtility.TrySignalForceNormalSpeedShort();
        return true;
    }
}
