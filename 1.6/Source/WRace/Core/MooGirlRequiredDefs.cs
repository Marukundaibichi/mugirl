using RimWorld;
using Verse;

namespace MooGirl
{
    internal static class MooGirlRequiredDefs
    {
        internal static class Hediffs
        {
            internal static readonly HediffDef MooGirlMilkHealing = DefDatabase<HediffDef>.GetNamed("MooGirl_MilkHealing");
        }

        internal static class Thoughts
        {
            internal static readonly ThoughtDef ConsumedMooGirlMilk = DefDatabase<ThoughtDef>.GetNamed("Consumed_MooGirlMilk");
            internal static readonly ThoughtDef MooGirlDrankMilk = DefDatabase<ThoughtDef>.GetNamed("MooGirl_DrankMilk");
            internal static readonly ThoughtDef MooGirlFedMilk = DefDatabase<ThoughtDef>.GetNamed("MooGirl_FedMilk");
        }
    }
}
