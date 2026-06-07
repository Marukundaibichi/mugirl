using RimWorld;
using Verse;

namespace MooGirl
{
    // StaticCacheLifecycle: 进程级外部乳房 HediffDef 缓存；可选 mod 缺失时条目允许为 null。
    internal static class ExternalBreastHediffDefs
    {
        internal static readonly HediffDef HugeBreasts = Get("HugeBreasts");
        internal static readonly HediffDef BionicBreasts = Get("BionicBreasts");
        internal static readonly HediffDef SlimeBreasts = Get("SlimeBreasts");
        internal static readonly HediffDef GrMuffaloMammaries = Get("GR_MuffaloMammaries");
        internal static readonly HediffDef Breasts = Get("Breasts");
        internal static readonly HediffDef HydraulicBreasts = Get("HydraulicBreasts");
        internal static readonly HediffDef SmallBreasts = Get("SmallBreasts");
        internal static readonly HediffDef LargeBreasts = Get("LargeBreasts");
        internal static readonly HediffDef ArchotechBreasts = Get("ArchotechBreasts");
        internal static readonly HediffDef FlatBreasts = Get("FlatBreasts");

        private static HediffDef Get(string defName)
        {
            return DefDatabase<HediffDef>.GetNamedSilentFail(defName);
        }
    }
}
