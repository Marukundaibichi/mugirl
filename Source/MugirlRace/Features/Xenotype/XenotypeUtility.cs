namespace Mugirl.Features.Xenotype;

// 雪牛娘异种初始化：出生规则与一次性安全扫描共用同一套基因操作，保留玩家植入的异种与自定义异种。
internal static class XenotypeUtility
{
    internal static void ForceFemaleMugirlXenotypeIfNeeded(Pawn? child, Pawn? geneticMother = null, Pawn? father = null, Thing? birtherThing = null)
    {
        if (!ModsConfig.BiotechActive || child is null || child.gender != Gender.Female || child.genes is null)
        {
            return;
        }

        XenotypeDef? targetXenotype = MugirlXenotypeDefOf.Mugirl_Xenotype;
        if (targetXenotype is null || !IsMugirlRelatedBirth(child, geneticMother, father, birtherThing))
        {
            return;
        }

        ApplyXenotypeAndMissingEndogenes(child, targetXenotype, forceNonHybrid: true);
    }

    internal static void InitializeEligiblePawns()
    {
        XenotypeDef? targetXenotype = MugirlXenotypeDefOf.Mugirl_Xenotype;
        if (targetXenotype is null)
        {
            return;
        }

        foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
        {
            EnsureEligiblePawnXenotype(pawn, targetXenotype);
        }
    }

    private static void EnsureEligiblePawnXenotype(Pawn? pawn, XenotypeDef? targetXenotype)
    {
        if (targetXenotype is null || pawn is null || pawn.def != MugirlDefOf.Mugirl || pawn.genes is null)
        {
            return;
        }

        // 玩家植入的异种胚芽或人物编辑器创建的自定义异种不能被重置。
        if (pawn.genes.UniqueXenotype || HasAnyXenogene(pawn))
        {
            return;
        }

        ApplyXenotypeAndMissingEndogenes(pawn, targetXenotype, forceNonHybrid: false);
    }

    private static bool IsMugirlRelatedBirth(Pawn child, Pawn? geneticMother, Pawn? father, Thing? birtherThing)
    {
        if (IsMugirlPawn(child) || IsMugirlPawn(geneticMother) || IsMugirlPawn(father) || IsMugirlPawn(birtherThing as Pawn))
        {
            return true;
        }

        // 父母（Parent）或生母（ParentBirth，Biotech 代孕）之一是雪牛娘；直接遍历直接关系，不收集临时列表。
        List<DirectPawnRelation>? relations = child.relations?.DirectRelations;
        if (relations is null)
        {
            return false;
        }

        for (int i = 0; i < relations.Count; i++)
        {
            DirectPawnRelation relation = relations[i];
            if ((relation.def == PawnRelationDefOf.Parent || relation.def == PawnRelationDefOf.ParentBirth) && IsMugirlPawn(relation.otherPawn))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAnyXenogene(Pawn pawn)
    {
        List<Gene>? xenogenes = pawn.genes.Xenogenes;
        return xenogenes is not null && xenogenes.Count > 0;
    }

    private static void ApplyXenotypeAndMissingEndogenes(Pawn pawn, XenotypeDef targetXenotype, bool forceNonHybrid)
    {
        if (pawn.genes is null || targetXenotype.genes is null)
        {
            return;
        }

        if (pawn.genes.Xenotype != targetXenotype)
        {
            pawn.genes.SetXenotypeDirect(targetXenotype);
        }

        if (forceNonHybrid)
        {
            pawn.genes.hybrid = false;
        }

        for (int i = 0; i < targetXenotype.genes.Count; i++)
        {
            GeneDef? geneDef = targetXenotype.genes[i];
            if (geneDef is null)
            {
                continue;
            }

            if (!pawn.genes.HasEndogene(geneDef) && !pawn.genes.HasXenogene(geneDef))
            {
                pawn.genes.AddGene(geneDef, xenogene: false);
            }
        }
    }

    private static bool IsMugirlPawn(Pawn? pawn)
    {
        return MugirlIdentity.IsMugirlPawn(pawn);
    }
}
