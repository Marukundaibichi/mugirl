using RimWorld;
using Verse;

namespace Mugirl
{
    public class IncidentWorker_Mugirl_RunawayFarmQuest : IncidentWorker_GiveQuest_Map
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            return MugirlMod.Settings?.enableRunawayMugirlFarmQuest == true
                && base.CanFireNowSub(parms);
        }
    }
}
