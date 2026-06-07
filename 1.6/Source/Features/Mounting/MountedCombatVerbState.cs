using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MooGirl
{
    public static partial class MountedCombatController
    {
        // StaticCacheLifecycle: 每局游戏的临时 Verb 施放者覆盖；下骑、移除装备和全局重置时清空。
        private static readonly Dictionary<Verb, Thing> OriginalCasters = new Dictionary<Verb, Thing>();
        // StaticCacheLifecycle: 每局游戏的临时骑乘 Verb 暖机覆盖；施放清理、移除装备和全局重置时清空。
        private static readonly Dictionary<Verb, float> WarmupTimeOverrides = new Dictionary<Verb, float>();

        public static void ResetTransientState()
        {
            foreach (KeyValuePair<Verb, Thing> entry in OriginalCasters)
            {
                Verb verb = entry.Key;
                if (verb != null)
                {
                    verb.Reset();
                    if (entry.Value != null)
                    {
                        verb.caster = entry.Value;
                    }
                }
            }

            OriginalCasters.Clear();
            WarmupTimeOverrides.Clear();
        }

        public static bool TryGetWarmupTimeOverride(Verb verb, ref float warmupTime)
        {
            if (verb != null && WarmupTimeOverrides.TryGetValue(verb, out float overrideValue))
            {
                warmupTime = overrideValue;
                return true;
            }

            return false;
        }

        public static bool IsMountedVerb(Verb verb)
        {
            return verb != null && OriginalCasters.ContainsKey(verb);
        }

        private struct MountedWarmupOverride : System.IDisposable
        {
            private readonly Verb verb;

            public MountedWarmupOverride(Verb verb, float warmupTime)
            {
                this.verb = verb;
                if (verb != null)
                {
                    WarmupTimeOverrides[verb] = warmupTime;
                }
            }

            public void Dispose()
            {
                if (verb != null)
                {
                    WarmupTimeOverrides.Remove(verb);
                }
            }
        }

        private struct MountedMinRangeOverride : System.IDisposable
        {
            private readonly Verb verb;
            private readonly float originalMinRange;
            private readonly bool active;

            public MountedMinRangeOverride(Verb verb, float? minRange)
            {
                this.verb = verb;
                originalMinRange = verb?.verbProps?.minRange ?? 0f;
                active = verb?.verbProps != null && minRange.HasValue;
                if (active)
                {
                    verb.verbProps.minRange = minRange.Value;
                }
            }

            public void Dispose()
            {
                if (active)
                {
                    verb.verbProps.minRange = originalMinRange;
                }
            }
        }

        private static void EnsureVerbCaster(Comp_MooGirlMount comp, Verb verb)
        {
            Pawn carrier = comp?.MooPawn;
            Pawn rider = comp?.MountedPawn;
            if (carrier == null || verb == null)
            {
                return;
            }

            if (!OriginalCasters.ContainsKey(verb))
            {
                OriginalCasters.Add(verb, verb.caster == carrier && rider != null ? rider : verb.caster);
            }
        }

        private static MountedVerbScope MountedCasterScope(Comp_MooGirlMount comp, Verb verb)
        {
            EnsureVerbCaster(comp, verb);
            Pawn carrier = comp?.MooPawn;
            Pawn rider = comp?.MountedPawn;
            return new MountedVerbScope(verb, carrier, OriginalCasterFor(verb, rider));
        }

        private static Thing OriginalCasterFor(Verb verb, Pawn fallback)
        {
            if (verb == null)
            {
                return fallback;
            }

            return OriginalCasters.TryGetValue(verb, out Thing originalCaster) ? originalCaster ?? fallback : fallback ?? verb.caster;
        }

        private static void RestoreVerbCaster(Pawn rider)
        {
            if (rider?.equipment == null)
            {
                return;
            }

            List<ThingWithComps> equipment = rider.equipment.AllEquipmentListForReading;
            for (int i = 0; i < equipment.Count; i++)
            {
                CompEquippable comp = equipment[i].GetComp<CompEquippable>();
                List<Verb> verbs = comp?.AllVerbs;
                if (verbs == null)
                {
                    continue;
                }

                for (int j = 0; j < verbs.Count; j++)
                {
                    Verb verb = verbs[j];
                    if (OriginalCasters.TryGetValue(verb, out Thing originalCaster))
                    {
                        verb.Reset();
                        verb.caster = originalCaster ?? rider;
                        ForgetVerb(verb, removeWarmupOverride: true, resetVerb: false);
                    }
                    else if (verb.caster != rider)
                    {
                        verb.Reset();
                        verb.caster = rider;
                    }
                }
            }
        }

        private static void ForgetVerb(Verb verb, bool removeWarmupOverride = true, bool resetVerb = true)
        {
            if (verb == null)
            {
                return;
            }

            if (OriginalCasters.TryGetValue(verb, out Thing originalCaster))
            {
                if (resetVerb)
                {
                    verb.Reset();
                }

                if (originalCaster != null)
                {
                    verb.caster = originalCaster;
                }

                OriginalCasters.Remove(verb);
            }

            if (removeWarmupOverride)
            {
                WarmupTimeOverrides.Remove(verb);
            }
        }
    }
}
