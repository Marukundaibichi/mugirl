using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl.Features.AdvancedArmor
{
    public sealed class CompProperties_NeuralShoulderDeflection : CompProperties
    {
        public float deflectionChancePerShoulder = 0.12f;
        public int shoulderCount = 2;
        public float durabilityDamageFactor = 0.04f;
        public int maximumDurabilityDamage = 8;

        public CompProperties_NeuralShoulderDeflection()
        {
            compClass = typeof(CompNeuralShoulderDeflection);
        }
    }

    public sealed class CompNeuralShoulderDeflection : ThingComp
    {
        private CompProperties_NeuralShoulderDeflection Props => (CompProperties_NeuralShoulderDeflection)props;

        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = false;
            Pawn wearer = (parent as Apparel)?.Wearer;
            if (wearer == null || wearer.Map == null || dinfo.Def?.isExplosive == true || dinfo.Weapon?.IsRangedWeapon != true)
            {
                return;
            }

            int attempts = Mathf.Max(0, Props.shoulderCount);
            for (int i = 0; i < attempts; i++)
            {
                if (!Rand.Chance(Props.deflectionChancePerShoulder))
                {
                    continue;
                }

                absorbed = true;
                int durabilityDamage = Mathf.Clamp(
                    Mathf.CeilToInt(dinfo.Amount * Props.durabilityDamageFactor),
                    1,
                    Mathf.Max(1, Props.maximumDurabilityDamage));
                parent.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, durabilityDamage));
                FleckMaker.ThrowMicroSparks(wearer.DrawPos, wearer.Map);
                return;
            }
        }
    }
}
