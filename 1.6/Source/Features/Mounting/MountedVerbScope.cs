using System;
using RimWorld;
using Verse;

namespace Mugirl
{
    public readonly struct MountedVerbScope : IDisposable
    {
        private readonly Verb verb;
        private readonly Thing restoreCaster;
        private readonly bool active;

        public MountedVerbScope(Verb verb, Thing temporaryCaster, Thing restoreCaster)
        {
            this.verb = verb;
            this.restoreCaster = restoreCaster ?? verb?.caster;
            active = verb != null && temporaryCaster != null;

            if (active && verb.caster != temporaryCaster)
            {
                verb.caster = temporaryCaster;
            }
        }

        public void Dispose()
        {
            if (active && verb.caster != restoreCaster)
            {
                verb.caster = restoreCaster;
            }
        }
    }
}
