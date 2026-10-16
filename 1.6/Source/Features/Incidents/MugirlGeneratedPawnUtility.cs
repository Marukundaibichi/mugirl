using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Mugirl
{
    internal static class MugirlGeneratedPawnUtility
    {
        private const string MugirlSlaveBackstoryCategory = "Mugirl_Slave";

        // 民间事件共用生成约束；事件自身只补充伤势、服装、去留与奖励规则。
        public static PawnGenerationRequest CreateFactionlessAdultRequest(PawnKindDef kind,
            PlanetTile? tile = null, bool allowDowned = false, bool unarmed = false,
            float? fixedAge = null, Predicate<Pawn> validator = null)
        {
            PawnGenerationRequest request = new PawnGenerationRequest(kind, null,
                PawnGenerationContext.NonPlayer, tile,
                forceGenerateNewPawn: true, allowDead: false, allowDowned: allowDowned,
                canGeneratePawnRelations: false, mustBeCapableOfViolence: true,
                forceAddFreeWarmLayerIfNeeded: false, allowGay: true, allowPregnant: false,
                forceRecruitable: true, dontGiveWeapon: unarmed, validatorPostGear: validator,
                fixedBiologicalAge: fixedAge, fixedChronologicalAge: fixedAge,
                fixedGender: Gender.Female, developmentalStages: DevelopmentalStage.Adult);
            request.ForceNoIdeoGear = unarmed;
            return request;
        }

        public static bool HasMugirlSlaveBackstories(Pawn pawn)
        {
            if (pawn?.story == null)
            {
                return false;
            }

            return BackstoryHasCategory(pawn.story.Childhood, MugirlSlaveBackstoryCategory)
                && BackstoryHasCategory(pawn.story.Adulthood, MugirlSlaveBackstoryCategory);
        }

        private static bool BackstoryHasCategory(BackstoryDef backstory, string category)
        {
            return backstory?.spawnCategories != null
                && backstory.spawnCategories.Contains(category);
        }

        public static bool TryPassToWorld(Pawn pawn, bool keepFactionless = false)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return false;
            }

            if (pawn.IsWorldPawn())
            {
                return true;
            }

            if (pawn.Spawned)
            {
                MugirlLog.WarningOnce(
                    "GeneratedPawnPassToWorldSpawned",
                    "Tried to pass a spawned generated pawn to world storage; leaving it on its current map.");
                return false;
            }

            bool wasFactionless = pawn.Faction == null;
            bool registered = MugirlGameUtility.TryPassToWorld(pawn);
            // 任务尚未登记时，原版把人物视作自由世界人物并随机分配派系。
            // 救援/申请加入要保留无派系状态；巨企人员离图等调用仍遵循原版行为。
            if (registered && keepFactionless && wasFactionless && pawn.Faction != null)
                pawn.SetFaction(null);
            return registered;
        }

        public static void Discard(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            if (pawn.Spawned)
            {
                MugirlLog.WarningOnce(
                    "GeneratedPawnDiscardSpawned",
                    "Tried to discard a spawned generated pawn; leaving it on its current map.");
                return;
            }

            MugirlGameUtility.TryRemoveWorldPawn(pawn);

            MugirlGameUtility.TryPassToWorldForDiscard(pawn);
        }
    }
}
