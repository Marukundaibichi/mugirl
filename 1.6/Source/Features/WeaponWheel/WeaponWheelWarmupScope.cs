using System;
using System.Collections.Generic;
using Verse;

namespace Mugirl.Features.WeaponWheel
{
    internal readonly struct WeaponWheelWarmupScope : IDisposable
    {
        // StaticCacheLifecycle: 仅在同步调用下一把武器的 TryStartCastOn 时临时登记；Dispose 后立即移除，不跨 Tick。
        private static readonly HashSet<Verb> skippedVerbs = new HashSet<Verb>();

        private readonly Verb verb;
        private readonly bool added;

        internal WeaponWheelWarmupScope(Verb verb)
        {
            this.verb = verb;
            added = verb != null && skippedVerbs.Add(verb);
        }

        internal static bool IsSkipping(Verb verb)
        {
            return verb != null && skippedVerbs.Contains(verb);
        }

        public void Dispose()
        {
            if (added)
            {
                skippedVerbs.Remove(verb);
            }
        }
    }
}
