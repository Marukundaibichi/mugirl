using Verse;
using Verse.AI;

namespace Mugirl
{
    internal sealed class MountedAttackTargetSearcher : IAttackTargetSearcher
    {
        private readonly Comp_MugirlMount comp;
        private readonly Verb verb;

        public MountedAttackTargetSearcher(Comp_MugirlMount comp, Verb verb)
        {
            this.comp = comp;
            this.verb = verb;
        }

        public Thing Thing => comp?.MooPawn;

        public Verb CurrentEffectiveVerb => verb;

        public LocalTargetInfo LastAttackedTarget => comp?.turretLastAttackedTarget ?? LocalTargetInfo.Invalid;

        public int LastAttackTargetTick => comp?.turretLastAttackTargetTick ?? 0;
    }
}
