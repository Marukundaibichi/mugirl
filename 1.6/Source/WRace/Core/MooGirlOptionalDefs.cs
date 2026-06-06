using RimWorld;
using Verse;
using Verse.AI;

namespace MooGirl
{
    internal static class MooGirlOptionalDefs
    {
        internal static class Hediffs
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
            internal static readonly HediffDef MooGirlLactation = Get("MooGirl_Lactation");

            private static HediffDef Get(string defName)
            {
                return DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            }
        }

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
