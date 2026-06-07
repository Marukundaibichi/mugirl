using Verse;
using Verse.AI;

namespace MooGirl
{
    internal sealed class MountedAttackTargetSearcher : IAttackTargetSearcher
    {
        private readonly Comp_MooGirlMount comp;
        private readonly Verb verb;

        public MountedAttackTargetSearcher(Comp_MooGirlMount comp, Verb verb)
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
