using System;
using RimWorld;
using Verse;

namespace Mugirl
{
    // StaticCacheLifecycle: 进程级必需 Def 缓存；缺失项会在静态初始化时立即失败。
    internal static class MugirlRequiredDefs
    {
        internal static class Hediffs
        {
            internal static readonly HediffDef MugirlLactation = Required<HediffDef>("Mugirl_Lactation");
            internal static readonly HediffDef MugirlMilkHealing = Required<HediffDef>("Mugirl_MilkHealing");
        }

        internal static class Thoughts
        {
            internal static readonly ThoughtDef ConsumedMugirlMilk = Required<ThoughtDef>("Consumed_MugirlMilk");
            internal static readonly ThoughtDef MugirlDrankMilk = Required<ThoughtDef>("Mugirl_DrankMilk");
            internal static readonly ThoughtDef MugirlFedMilk = Required<ThoughtDef>("Mugirl_FedMilk");
        }

        private static T Required<T>(string defName) where T : Def
        {
            T def = DefDatabase<T>.GetNamedSilentFail(defName);
            if (def != null)
            {
                return def;
            }

            throw new InvalidOperationException($"Mugirl required {typeof(T).Name} is missing: {defName}");
        }
    }
}
