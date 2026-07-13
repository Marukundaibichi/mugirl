using Verse;
using Mugirl.Features.WeaponWheel;

namespace Mugirl
{
    public sealed class CompProperties_WeaponWheel : CompProperties
    {
        public int maxSlots = 6;
        public int baseUnlockedSlots = 3;
        public int equipAnimationTicks = 42;
        public int outgoingAnimationTicks = 18;
        public int snapTick = 38;
        public int switchSnapTick = 30;

        public CompProperties_WeaponWheel()
        {
            compClass = typeof(Comp_WeaponWheel);
        }
    }
}
