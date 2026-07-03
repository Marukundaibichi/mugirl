using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Mugirl
{
    internal static class MugirlXenotypeService
    {
        // StaticCacheLifecycle: 单次调用内使用的关系查询临时列表；返回前始终在 finally 中清空。
        private static readonly List<Pawn> tmpParents = new List<Pawn>();

        internal static void ForceFemaleMugirlXenotypeIfNeeded(Pawn child, Pawn geneticMother = null, Pawn father = null, Thing birtherThing = null)
        {
            if (!ModsConfig.BiotechActive || child == null || child.gender != Gender.Female || child.genes == null)
            {
                return;
            }

            XenotypeDef targetXenotype = Mugirl_DefOf.Mugirl_Xenotype;
            if (targetXenotype == null || !IsMugirlRelatedBirth(child, geneticMother, father, birtherThing))
            {
                return;
            }

            ApplyXenotypeAndMissingEndogenes(child, targetXenotype, forceNonHybrid: true);
        }

        internal static void FixLoadedPawnIfSafe(Pawn pawn, XenotypeDef targetXenotype)
        {
            if (targetXenotype == null || pawn == null || pawn.def != Mugirl_DefOf.Mugirl || pawn.genes == null)
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

        private static bool IsMugirlRelatedBirth(Pawn child, Pawn geneticMother, Pawn father, Thing birtherThing)
        {
            if (IsMugirlPawn(child) || IsMugirlPawn(geneticMother) || IsMugirlPawn(father) || IsMugirlPawn(birtherThing as Pawn))
            {
                return true;
            }

            List<Pawn> parents = tmpParents;
            parents.Clear();
            try
            {
                child.relations?.GetDirectRelations(PawnRelationDefOf.Parent, ref parents);
                child.relations?.GetDirectRelations(PawnRelationDefOf.ParentBirth, ref parents);
                return HasMugirlParent(parents);
            }
            finally
            {
                if (!ReferenceEquals(parents, tmpParents))
                {
                    parents?.Clear();
                }

                tmpParents.Clear();
            }
        }

        private static bool HasMugirlParent(List<Pawn> parents)
        {
            for (int i = 0; i < parents.Count; i++)
            {
                if (IsMugirlPawn(parents[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAnyXenogene(Pawn pawn)
        {
            List<Gene> xenogenes = pawn.genes.Xenogenes;
            return xenogenes != null && xenogenes.Count > 0;
        }

        private static void ApplyXenotypeAndMissingEndogenes(Pawn pawn, XenotypeDef targetXenotype, bool forceNonHybrid)
        {
            if (pawn?.genes == null || targetXenotype?.genes == null)
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
                GeneDef geneDef = targetXenotype.genes[i];
                if (geneDef == null)
                {
                    continue;
                }

                if (!pawn.genes.HasEndogene(geneDef) && !pawn.genes.HasXenogene(geneDef))
                {
                    pawn.genes.AddGene(geneDef, xenogene: false);
                }
            }
        }

        private static bool IsMugirlPawn(Pawn pawn)
        {
            return MugirlIdentity.IsMugirlPawn(pawn);
        }
    }

    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.TrySpawnHatchedOrBornPawn))]
    public static class Harmony_PawnUtility_TrySpawnHatchedOrBornPawn_MugirlXenotype
    {
        public static void Prefix(Pawn pawn, Thing motherOrEgg)
        {
            MugirlXenotypeService.ForceFemaleMugirlXenotypeIfNeeded(pawn, birtherThing: motherOrEgg);
        }

        public static void Postfix(Pawn pawn, Thing motherOrEgg, bool __result)
        {
            MugirlXenotypeService.ForceFemaleMugirlXenotypeIfNeeded(pawn, birtherThing: motherOrEgg);
            if (__result)
            {
                MugirlLactationUtility.NotifyBirth(motherOrEgg as Pawn);
            }
        }
    }

    // 游戏组件仅保留低频兜底扫描；出生时优先由 Harmony 入口即时修正。
    public class Mugirl_XenotypeFix_GameComp : GameComponent
    {
        public bool xenotypeFixed;
        public int checkTimer = 300;

        public Mugirl_XenotypeFix_GameComp() { }

        public Mugirl_XenotypeFix_GameComp(Game game) { }

        public override void GameComponentTick()
        {
            if (!ModsConfig.BiotechActive || xenotypeFixed)
            {
                return;
            }

            if (checkTimer > 0)
            {
                checkTimer--;
                return;
            }

            TryFixMugirlXenotype();
            xenotypeFixed = true;
        }

        private void TryFixMugirlXenotype()
        {
            XenotypeDef targetXenotype = Mugirl_DefOf.Mugirl_Xenotype;
            if (targetXenotype == null)
            {
                return;
            }

            foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
            {
                MugirlXenotypeService.FixLoadedPawnIfSafe(pawn, targetXenotype);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref xenotypeFixed, "xenotypeFixed", false);
            Scribe_Values.Look(ref checkTimer, "checkTimer", 300);
        }
    }
}
