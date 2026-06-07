using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace MooGirl
{
    internal static class MooGirlXenotypeService
    {
        // StaticCacheLifecycle: per-call scratch list for relation lookup; always cleared in finally before returning.
        private static readonly List<Pawn> tmpParents = new List<Pawn>();

        internal static void ForceFemaleMooGirlXenotypeIfNeeded(Pawn child, Pawn geneticMother = null, Pawn father = null, Thing birtherThing = null)
        {
            if (!ModsConfig.BiotechActive || child == null || child.gender != Gender.Female || child.genes == null)
            {
                return;
            }

            XenotypeDef targetXenotype = MooGirl_DefOf.MooGirl_Xenotype;
            if (targetXenotype == null || !IsMooGirlRelatedBirth(child, geneticMother, father, birtherThing))
            {
                return;
            }

            ApplyXenotypeAndMissingEndogenes(child, targetXenotype, forceNonHybrid: true);
        }

        internal static void FixLoadedPawnIfSafe(Pawn pawn, XenotypeDef targetXenotype)
        {
            if (targetXenotype == null || pawn == null || pawn.def != MooGirl_DefOf.MooGirl || pawn.genes == null)
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

        private static bool IsMooGirlRelatedBirth(Pawn child, Pawn geneticMother, Pawn father, Thing birtherThing)
        {
            if (IsMooGirlPawn(child) || IsMooGirlPawn(geneticMother) || IsMooGirlPawn(father) || IsMooGirlPawn(birtherThing as Pawn))
            {
                return true;
            }

            List<Pawn> parents = tmpParents;
            parents.Clear();
            try
            {
                child.relations?.GetDirectRelations(PawnRelationDefOf.Parent, ref parents);
                child.relations?.GetDirectRelations(PawnRelationDefOf.ParentBirth, ref parents);
                return HasMooGirlParent(parents);
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

        private static bool HasMooGirlParent(List<Pawn> parents)
        {
            for (int i = 0; i < parents.Count; i++)
            {
                if (IsMooGirlPawn(parents[i]))
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

        private static bool IsMooGirlPawn(Pawn pawn)
        {
            return MooGirlIdentity.IsMooGirlPawn(pawn);
        }
    }

    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.TrySpawnHatchedOrBornPawn))]
    public static class Harmony_PawnUtility_TrySpawnHatchedOrBornPawn_MooGirlXenotype
    {
        public static void Prefix(Pawn pawn, Thing motherOrEgg)
        {
            MooGirlXenotypeService.ForceFemaleMooGirlXenotypeIfNeeded(pawn, birtherThing: motherOrEgg);
        }

        public static void Postfix(Pawn pawn, Thing motherOrEgg, bool __result)
        {
            MooGirlXenotypeService.ForceFemaleMooGirlXenotypeIfNeeded(pawn, birtherThing: motherOrEgg);
            if (__result)
            {
                MooGirlLactationUtility.NotifyBirth(motherOrEgg as Pawn);
            }
        }
    }

    // 游戏组件仅保留低频兜底扫描；出生时优先由 Harmony 入口即时修正。
    public class MooGirl_XenotypeFix_GameComp : GameComponent
    {
        public bool xenotypeFixed;
        public int checkTimer = 300;

        public MooGirl_XenotypeFix_GameComp() { }

        public MooGirl_XenotypeFix_GameComp(Game game) { }

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

            TryFixMooGirlXenotype();
            xenotypeFixed = true;
        }

        private void TryFixMooGirlXenotype()
        {
            XenotypeDef targetXenotype = MooGirl_DefOf.MooGirl_Xenotype;
            if (targetXenotype == null)
            {
                return;
            }

            foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
            {
                MooGirlXenotypeService.FixLoadedPawnIfSafe(pawn, targetXenotype);
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
