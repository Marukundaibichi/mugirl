using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MooGirl
{
    public class StockGenerator_MooGirl_Slaves : StockGenerator
    {
        public IntRange slaveCountRange = new IntRange(1, 8);
        public PawnKindDef slaveKindDef;

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            if (faction != null && faction.ideos != null && !AllIdeosApproveSlavery(faction))
            {
                yield break;
            }

            int count = slaveCountRange.RandomInRange;
            for (int i = 0; i < count; i++)
            {
                DevelopmentalStage stage = MooGirlGameUtility.ChildrenAllowedByCurrentDifficulty()
                    ? DevelopmentalStage.Child | DevelopmentalStage.Adult
                    : DevelopmentalStage.Adult;

                PawnGenerationRequest request = new PawnGenerationRequest(
                    slaveKindDef ?? MooGirl_DefOf.MooGirl_Slave,
                    faction: null,
                    PawnGenerationContext.NonPlayer,
                    tile: forTile,
                    forceGenerateNewPawn: false,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: true,
                    mustBeCapableOfViolence: false,
                    colonistRelationChanceFactor: 1f,
                    forceAddFreeWarmLayerIfNeeded: true,
                    allowGay: true,
                    allowPregnant: false,
                    allowFood: true,
                    allowAddictions: true,
                    inhabitant: false,
                    certainlyBeenInCryptosleep: false,
                    forceRedressWorldPawnIfFormerColonist: false,
                    worldPawnFactionDoesntMatter: false,
                    biocodeWeaponChance: 0f,
                    biocodeApparelChance: 0f,
                    extraPawnForExtraRelationChance: null,
                    relationWithExtraPawnChanceFactor: 1f,
                    validatorPreGear: null,
                    validatorPostGear: null,
                    forcedTraits: null,
                    prohibitedTraits: null,
                    developmentalStages: stage);

                Pawn pawn = PawnGenerator.GeneratePawn(request);
                MooGirlApparelTagUtility.TryWearIdeoSuppressedKindApparel(pawn);

                yield return pawn;
            }
        }

        public override bool HandlesThingDef(ThingDef thingDef)
        {
            return thingDef?.category == ThingCategory.Pawn
                && thingDef.race?.Humanlike == true
                && thingDef.tradeability > Tradeability.None;
        }

        private static bool AllIdeosApproveSlavery(Faction faction)
        {
            foreach (Ideo ideo in faction.ideos.AllIdeos)
            {
                if (!ideo.IdeoApprovesOfSlavery())
                {
                    return false;
                }
            }

            return true;
        }
    }
}
