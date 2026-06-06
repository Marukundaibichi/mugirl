using System.Collections.Generic;
using Verse;
using RimWorld;
using RimWorld.Planet;
using MooGirl;

public class StockGenerator_MooGirl_Slaves : StockGenerator
{
    // 从 XML 设置生成奴隶的数量范围，例如 <slaveCountRange>1~3</slaveCountRange>
    public IntRange slaveCountRange = new IntRange(1, 8);

    // 可选：指定生成的奴隶类型，例如 <slaveKindDef>Slave</slaveKindDef>
    public PawnKindDef slaveKindDef;

    public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
    {
        // 如果派系有思想体系，并且不接受奴隶制，则不生成任何奴隶
        if (faction != null && faction.ideos != null && !AllIdeosApproveSlavery(faction))
        {
            yield break;
        }

        // 在设定的范围内随机决定生成多少个奴隶
        int count = slaveCountRange.RandomInRange;

        for (int i = 0; i < count; i++)
        {
            // 判断是否允许生成儿童
            DevelopmentalStage stage = Find.Storyteller.difficulty.ChildrenAllowed
                ? (DevelopmentalStage.Child | DevelopmentalStage.Adult)
                : DevelopmentalStage.Adult;

            // 获取奴隶种类，默认是内置的 Slave
            PawnKindDef kind = this.slaveKindDef ?? MooGirl_DefOf.MooGirl_Slave;

            // 生成请求（无派系，非玩家生成）
            PawnGenerationRequest request = new PawnGenerationRequest(
                kind,
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
                developmentalStages: stage
            );

            Pawn pawn = PawnGenerator.GeneratePawn(request);

            // 只有当 Ideo 中存在 preventApparelRequirements 的 meme 时才再穿一次服装
            MooGirlApparelTagUtility.TryWearIdeoSuppressedKindApparel(pawn);

            yield return pawn;
        }
    }

    public override bool HandlesThingDef(ThingDef thingDef)
    {
        return thingDef.category == ThingCategory.Pawn &&
                thingDef.race.Humanlike &&
                thingDef.tradeability > Tradeability.None;
    }

    private static bool AllIdeosApproveSlavery(Faction faction)
    {
        // 保留旧版 All() 语义：只要任一 Ideo 不接受奴隶制，就不生成奴隶库存。
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
