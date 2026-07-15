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
        public float swordDanceChance = 0.33f;
        public int swordDanceStunTicks = 60;
        public int swordDanceHasteTicks = 300;
        public float swordDanceRamDamage = 10f;
        public float swordDanceRamArmorPenetration = 0.2f;

        public CompProperties_WeaponWheel()
        {
            compClass = typeof(Comp_WeaponWheel);
        }
    }
}
