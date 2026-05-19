using System.Collections.Generic;
using System.Linq;
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
        if (faction != null && faction.ideos != null)
        {
            bool approved = faction.ideos.AllIdeos.All(ideo => ideo.IdeoApprovesOfSlavery());
            if (!approved)
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
            ApplyApparelTagsIfMemeAllows(pawn);

            yield return pawn;
        }
    }

    // 检测 preventApparelRequirements 并穿上 PawnKindDef.apparelTags
    private void ApplyApparelTagsIfMemeAllows(Pawn pawn)
    {
        if (pawn.kindDef.apparelTags == null || pawn.kindDef.apparelTags.Count == 0)
            return;

        bool hasPreventMeme = false;

        if (pawn.Ideo != null)
        {
            foreach (var meme in pawn.Ideo.memes)
            {
                if (meme.preventApparelRequirements)
                {
                    hasPreventMeme = true;
                    break;
                }
            }
        }

        if (!hasPreventMeme)
            return; // 没有 preventApparelRequirements，不穿额外服装

        List<string> apparelTags = pawn.kindDef.apparelTags.ToList();

        foreach (var tag in apparelTags)
        {
            var candidates = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(td => td.IsApparel
                             && td.apparel != null
                             && td.apparel.tags != null
                             && td.apparel.tags.Contains(tag)
                             && AdultContentUtility.IsAllowed(td))
                .ToList();

            if (candidates.Count > 0)
            {
                ThingDef chosenDef = candidates.RandomElement();
                Apparel newApparel;

                if (chosenDef.MadeFromStuff)
                {
                    ThingDef stuff = GenStuff.RandomStuffFor(chosenDef);
                    newApparel = (Apparel)ThingMaker.MakeThing(chosenDef, stuff);
                }
                else
                {
                    newApparel = (Apparel)ThingMaker.MakeThing(chosenDef, null);
                }

                // 只给特定类上锁
                bool shouldLock = newApparel is AdvancedSlaveApparel || newApparel is BrainWashSlaveApparel;

                pawn.apparel.Wear(newApparel, dropReplacedApparel: true, locked: shouldLock);
            }
        }
    }

    public override bool HandlesThingDef(ThingDef thingDef)
    {
        return thingDef.category == ThingCategory.Pawn &&
               thingDef.race.Humanlike &&
               thingDef.tradeability > Tradeability.None;
    }
}
