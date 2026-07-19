using RimWorld;
using Verse;

namespace Mugirl
{
    public class CompProperties_AbilityEffect_PickupThrow : CompProperties_AbilityEffect
    {
        public CompProperties_AbilityEffect_PickupThrow()
        {
            compClass = typeof(CompAbilityEffect_PickupThrow);
        }
    }

    public class CompAbilityEffect_PickupThrow : CompAbilityEffect
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn caster = parent?.pawn;
            Thing targetThing = target.Thing;
            string reason;
            if (!base.Valid(target, throwMessages))
            {
                return false;
            }

            if (MugirlThrowUtility.CanPickUp(caster, targetThing, out reason))
            {
                return true;
            }

            if (throwMessages && !reason.NullOrEmpty())
            {
                Messages.Message(reason, targetThing ?? caster, MessageTypeDefOf.RejectInput, historical: false);
            }
            return false;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            Pawn caster = parent?.pawn;
            Thing targetThing = target.Thing;
            string reason;
            if (MugirlThrowUtility.CanPickUp(caster, targetThing, out reason))
            {
                Thing_MugirlThrownObject.TryCreate(caster, targetThing, parent);
            }
            else if (!reason.NullOrEmpty())
            {
                Messages.Message(reason, targetThing ?? caster, MessageTypeDefOf.RejectInput, historical: false);
            }

            base.Apply(target, dest);
        }

        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            Thing thing = target.Thing;
            Pawn caster = parent?.pawn;
            if (thing == null || caster == null)
            {
                return null;
            }

            float mass = MugirlThrowUtility.EffectiveMass(thing);
            float strength = MugirlThrowUtility.ThrowStrength(caster);
            return "Mugirl.Throw.PickupReadout".Translate(mass.ToString("0.#"), strength.ToString("0.#")).ToString();
        }
    }
}
