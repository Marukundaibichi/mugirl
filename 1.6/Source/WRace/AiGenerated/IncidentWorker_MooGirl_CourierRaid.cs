using RimWorld;
using UnityEngine;
using Verse;

namespace MooGirl
{
    public class IncidentWorker_MooGirl_CourierRaid : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!base.CanFireNowSub(parms)) return false;
            if (!(parms.target is Map)) return false;
            return Find.FactionManager.FirstFactionOfDef(AiGenerated_DefOf.MooGirl_GiantCorporations_Hostile) != null;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(parms.target is Map map)) return false;
            Faction faction = Find.FactionManager.FirstFactionOfDef(AiGenerated_DefOf.MooGirl_GiantCorporations_Hostile);
            if (faction == null) return false;

            float points = parms.points > 0f ? Mathf.Min(parms.points, 200f) : 200f;
            QuestUtility.GenerateQuestAndMakeAvailable(AiGenerated_DefOf.MooGirl_CourierRaid, points);
            return true;
        }
    }
}
