using RimWorld.Planet;

namespace Mugirl.Features.Incidents;

// 事件小人的生成请求（无派系成人、访客）与奴隶背景判断。
internal static class EventPawnRequestUtility
{
    // 与 XML 背景故事的 spawnCategories 一致。
    private const string MugirlSlaveBackstoryCategory = "Mugirl_Slave";

    // 民间事件共用生成约束；事件自身只补充伤势、服装、去留与奖励规则。
    internal static PawnGenerationRequest CreateFactionlessAdultRequest(PawnKindDef kind,
        PlanetTile? tile = null, bool allowDowned = false, bool unarmed = false,
        float? fixedAge = null, Predicate<Pawn>? validator = null)
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

    // 牧场主与投资人共用的来访村民；faction 可以为 null（没有可用的非敌对派系时生成无派系村民）。
    internal static PawnGenerationRequest CreateVisitorRequest(Faction? faction, PlanetTile tile)
    {
        return new PawnGenerationRequest(
            PawnKindDefOf.Villager,
            faction,
            PawnGenerationContext.NonPlayer,
            tile,
            forceGenerateNewPawn: true,
            allowDead: false,
            allowDowned: false,
            canGeneratePawnRelations: false,
            mustBeCapableOfViolence: false,
            forceAddFreeWarmLayerIfNeeded: false,
            allowGay: true,
            allowPregnant: false,
            forceRecruitable: false,
            developmentalStages: DevelopmentalStage.Adult);
    }

    internal static bool HasMugirlSlaveBackstories(Pawn? pawn)
    {
        if (pawn?.story == null)
        {
            return false;
        }

        return BackstoryHasCategory(pawn.story.Childhood, MugirlSlaveBackstoryCategory)
            && BackstoryHasCategory(pawn.story.Adulthood, MugirlSlaveBackstoryCategory);
    }

    internal static bool BackstoryHasCategory(BackstoryDef? backstory, string category)
    {
        return backstory?.spawnCategories != null
            && backstory.spawnCategories.Contains(category);
    }
}
