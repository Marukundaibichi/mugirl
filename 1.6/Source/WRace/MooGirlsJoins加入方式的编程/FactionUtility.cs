using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MooGirl
{
    [StaticConstructorOnStartup]
    public static class MooGirl_FactionUtility
    {
        public static Faction GetNonHostileHumanlikeFaction()
        {
            List<Faction> factions = Find.FactionManager.AllFactionsListForReading;
            List<Faction> candidates = new List<Faction>();
            for (int i = 0; i < factions.Count; i++)
            {
                Faction faction = factions[i];
                if (IsAvailableHumanlikeFaction(faction) && !faction.HostileTo(Faction.OfPlayer))
                {
                    candidates.Add(faction);
                }
            }

            return candidates.RandomElementWithFallback(null);
        }

        public static Faction GetBestAvailableNonHostileFaction()
        {
            List<Faction> factions = Find.FactionManager.AllFactionsListForReading;
            Faction best = null;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction faction = factions[i];
                if (!IsAvailableHumanlikeFaction(faction) || faction.HostileTo(Faction.OfPlayer))
                {
                    continue;
                }

                if (best == null || faction.PlayerGoodwill > best.PlayerGoodwill)
                {
                    best = faction;
                }
            }

            if (best != null)
            {
                return best;
            }

            foreach (Faction faction in Find.FactionManager.AllFactionsVisible)
            {
                if (!faction.IsPlayer)
                {
                    return faction;
                }
            }

            return null;
        }

        private static bool IsAvailableHumanlikeFaction(Faction faction)
        {
            return faction != null
                && !faction.IsPlayer
                && faction.def.humanlikeFaction
                && !faction.def.hidden
                && !faction.defeated;
        }
    }
}
