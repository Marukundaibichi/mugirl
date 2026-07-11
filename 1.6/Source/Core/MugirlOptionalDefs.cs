using RimWorld;
using Verse;
using Verse.AI;

namespace Mugirl
{
    // StaticCacheLifecycle: 进程级可选 Def 缓存；加载 Def 后初始化，可选内容缺失时允许为 null。
    internal static class MugirlOptionalDefs
    {
        internal static class ThingDefs
        {
            internal static readonly ThingDef MugirlMilk = DefDatabase<ThingDef>.GetNamedSilentFail("Mugirl_Milk");
            internal static readonly ThingDef MugirlMilkFilth = DefDatabase<ThingDef>.GetNamedSilentFail("MugirlMilkFilth");
            internal static readonly ThingDef MugirlMilkStain = DefDatabase<ThingDef>.GetNamedSilentFail("MugirlMilkStain");
        }

        internal static class SoundDefs
        {
            internal static readonly SoundDef MugirlMilkingSound = DefDatabase<SoundDef>.GetNamedSilentFail("Mugirl_Milking_Sound");
        }

        internal static class JobDefs
        {
            internal static readonly JobDef DrinkMilkFromMugirl = DefDatabase<JobDef>.GetNamedSilentFail("Job_DrinkMilkFromMugirl");
            internal static readonly JobDef Breastfeed = DefDatabase<JobDef>.GetNamedSilentFail("Job_Breastfeed");
            internal static readonly JobDef FeedMilkToDowned = DefDatabase<JobDef>.GetNamedSilentFail("Job_FeedMilkToDowned");
            internal static readonly JobDef ForceUnlockSlaveApparel = DefDatabase<JobDef>.GetNamedSilentFail("ForceUnlockSlaveApparel");
        }

        internal static class FleckDefs
        {
            internal static readonly FleckDef FoamSpray = DefDatabase<FleckDef>.GetNamedSilentFail("FoamSpray");
            internal static readonly FleckDef MugirlMilkSpray = DefDatabase<FleckDef>.GetNamedSilentFail("Mugirl_MilkSpray");
            internal static readonly FleckDef GroundWaterSplash = DefDatabase<FleckDef>.GetNamedSilentFail("GroundWaterSplash");
        }

        internal static class ThoughtDefs
        {
            internal static readonly ThoughtDef ConsumedMugirlMilk = DefDatabase<ThoughtDef>.GetNamedSilentFail("Consumed_MugirlMilk");
            internal static readonly ThoughtDef MugirlDrankMilk = DefDatabase<ThoughtDef>.GetNamedSilentFail("Mugirl_DrankMilk");
        }
    }
}
