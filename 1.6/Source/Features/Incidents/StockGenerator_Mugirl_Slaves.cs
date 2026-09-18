using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Mugirl
{
    public class StockGenerator_Mugirl_Slaves : StockGenerator
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
                DevelopmentalStage stage = MugirlGameUtility.ChildrenAllowedByCurrentDifficulty()
                    ? DevelopmentalStage.Child | DevelopmentalStage.Adult
                    : DevelopmentalStage.Adult;

                PawnGenerationRequest request = new PawnGenerationRequest(
                    slaveKindDef ?? Mugirl_DefOf.Mugirl_Slave,
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

                Pawn pawn;
                try
                {
                    // 据点进货（Settlement_TraderTracker.RegenerateStock）会先销毁旧库存再逐个
                    // 生成器产出；这里任何一次抛异常都会中断整个枚举，把据点商品连同白银一起清空。
                    // 因此单个奴隶生成失败只跳过该个，绝不让异常穿透到库存生成流程。
                    pawn = PawnGenerator.GeneratePawn(request);
                    MugirlApparelTagUtility.TryWearIdeoSuppressedKindApparel(pawn);
                }
                catch (Exception ex)
                {
                    MugirlLog.WarningOnce(
                        "StockGeneratorMugirlSlaves.GenerationFailed",
                        "Slave stock generation failed, skipping one mugirl slave. " + ex.GetType().Name + ": " + ex.Message);
                    continue;
                }

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
