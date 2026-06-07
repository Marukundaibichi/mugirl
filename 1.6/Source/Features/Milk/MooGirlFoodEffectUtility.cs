using System.Collections.Generic;
using Verse;

namespace MooGirl
{
    internal static class MooGirlFoodEffectUtility
    {
        // StaticCacheLifecycle: per-game Def lookup cache; stores DefDatabase results only and is reset by MooGirlStoryState on game init/new-game/load.
        private static readonly Dictionary<string, HediffDef> HediffDefCache = new Dictionary<string, HediffDef>();

        internal static void RemoveHediffs(Pawn pawn, List<string> removeHediffs)
        {
            if (pawn?.health?.hediffSet == null || removeHediffs == null || removeHediffs.Count == 0)
            {
                return;
            }

            for (int i = 0; i < removeHediffs.Count; i++)
            {
                HediffDef hediffDef = GetHediffDef(removeHediffs[i]);
                if (hediffDef == null)
                {
                    continue;
                }

                Hediff hediff;
                while ((hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef)) != null)
                {
                    pawn.health.RemoveHediff(hediff);
                }
            }
        }

        private static HediffDef GetHediffDef(string defName)
        {
            if (string.IsNullOrEmpty(defName))
            {
                return null;
            }

            // removeHediffs 可能从 HediffComp tick 中运行，因此 XML 字符串查找
            // 在 Def 加载后缓存；可选内容缺失得到的 null 也一起缓存。
            if (!HediffDefCache.TryGetValue(defName, out HediffDef hediffDef))
            {
                hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                HediffDefCache[defName] = hediffDef;
            }

            return hediffDef;
        }

        internal static void ResetDefCache()
        {
            HediffDefCache.Clear();
        }

        internal static void AddOrRefreshHediff(Pawn pawn, HediffDef hediffDef, float minimumSeverity = -1f)
        {
            if (pawn?.health?.hediffSet == null || hediffDef == null)
            {
                return;
            }

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(hediffDef, pawn);
                if (minimumSeverity > 0f)
                {
                    hediff.Severity = minimumSeverity;
                }

                pawn.health.AddHediff(hediff);
            }
            else if (minimumSeverity > 0f && hediff.Severity < minimumSeverity)
            {
                hediff.Severity = minimumSeverity;
            }

            RefreshTimedFoodEffect(hediff);
        }

        private static void RefreshTimedFoodEffect(Hediff hediff)
        {
            HediffComp_Disappears disappears = hediff.TryGetComp<HediffComp_Disappears>();
            if (disappears != null)
            {
                disappears.ResetElapsedTicks();
            }

            HediffComp_CureFoodEffects cure = hediff.TryGetComp<HediffComp_CureFoodEffects>();
            if (cure != null)
            {
                cure.ReapplyCure();
            }
        }
    }
}
