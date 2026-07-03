using RimWorld;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public class IncidentWorker_Mugirl_CourierRaid : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!base.CanFireNowSub(parms)) return false;
            if (!(parms.target is Map)) return false;
            return MugirlGameUtility.TryGetFirstFactionOfDef(MugirlContentDefOf.Mugirl_GiantCorporations_Hostile, out _);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(parms.target is Map map)) return false;
            if (!MugirlGameUtility.TryGetFirstFactionOfDef(MugirlContentDefOf.Mugirl_GiantCorporations_Hostile, out Faction _)) return false;

            float points = parms.points > 0f ? Mathf.Min(parms.points, 200f) : 200f;
            Slate slate = new Slate();
            slate.Set("points", points);
            slate.Set("map", map);
            QuestUtility.GenerateQuestAndMakeAvailable(MugirlContentDefOf.Mugirl_CourierRaid, slate);
            return true;
        }
    }
}
