using System;
using RimWorld;
using Verse;

namespace MooGirl
{
    // StaticCacheLifecycle: 进程级必需 Def 缓存；缺失项会在静态初始化时立即失败。
    internal static class MooGirlRequiredDefs
    {
        internal static class Hediffs
        {
            internal static readonly HediffDef MooGirlLactation = Required<HediffDef>("MooGirl_Lactation");
            internal static readonly HediffDef MooGirlMilkHealing = Required<HediffDef>("MooGirl_MilkHealing");
        }

        internal static class Thoughts
        {
            internal static readonly ThoughtDef ConsumedMooGirlMilk = Required<ThoughtDef>("Consumed_MooGirlMilk");
            internal static readonly ThoughtDef MooGirlDrankMilk = Required<ThoughtDef>("MooGirl_DrankMilk");
            internal static readonly ThoughtDef MooGirlFedMilk = Required<ThoughtDef>("MooGirl_FedMilk");
        }

        private static T Required<T>(string defName) where T : Def
        {
            T def = DefDatabase<T>.GetNamedSilentFail(defName);
            if (def != null)
            {
                return def;
            }

            throw new InvalidOperationException($"MooGirl required {typeof(T).Name} is missing: {defName}");
        }
    }
}
