using RimWorld;
using Verse;
using Verse.AI;

namespace MooGirl
{
    // StaticCacheLifecycle: process-level optional Def cache; values come from DefDatabase after mod loading and may be null when optional content is absent.
    internal static class MooGirlOptionalDefs
    {
        internal static class ThingDefs
        {
            internal static readonly ThingDef MooGirlMilk = DefDatabase<ThingDef>.GetNamedSilentFail("MooGirl_Milk");
            internal static readonly ThingDef MooGirlMilkFilth = DefDatabase<ThingDef>.GetNamedSilentFail("MooGirlMilkFilth");
        }

        internal static class SoundDefs
        {
            internal static readonly SoundDef MooGirlMilkingSound = DefDatabase<SoundDef>.GetNamedSilentFail("MooGirl_Milking_Sound");
        }

        internal static class JobDefs
        {
            internal static readonly JobDef DrinkMilkFromMooGirl = DefDatabase<JobDef>.GetNamedSilentFail("Job_DrinkMilkFromMooGirl");
            internal static readonly JobDef Breastfeed = DefDatabase<JobDef>.GetNamedSilentFail("Job_Breastfeed");
            internal static readonly JobDef FeedMilkToDowned = DefDatabase<JobDef>.GetNamedSilentFail("Job_FeedMilkToDowned");
        }

        internal static class FleckDefs
        {
            internal static readonly FleckDef FoamSpray = DefDatabase<FleckDef>.GetNamedSilentFail("FoamSpray");
            internal static readonly FleckDef MooGirlMilkSpray = DefDatabase<FleckDef>.GetNamedSilentFail("MooGirl_MilkSpray");
            internal static readonly FleckDef GroundWaterSplash = DefDatabase<FleckDef>.GetNamedSilentFail("GroundWaterSplash");
        }

        internal static class ThoughtDefs
        {
            internal static readonly ThoughtDef ConsumedMooGirlMilk = DefDatabase<ThoughtDef>.GetNamedSilentFail("Consumed_MooGirlMilk");
            internal static readonly ThoughtDef MooGirlDrankMilk = DefDatabase<ThoughtDef>.GetNamedSilentFail("MooGirl_DrankMilk");
        }
    }
}
