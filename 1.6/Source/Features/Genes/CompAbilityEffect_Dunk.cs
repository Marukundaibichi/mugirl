using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public class CompProperties_AbilityEffect_Dunk : CompProperties_EffectWithDest
    {
        public CompProperties_AbilityEffect_Dunk()
        {
            compClass = typeof(CompAbilityEffect_Dunk);
            destination = AbilityEffectDestination.Selected;
        }
    }

    public class CompAbilityEffect_Dunk : CompAbilityEffect_WithDest
    {
        public override TargetingParameters targetParams => new TargetingParameters
        {
            canTargetLocations = true,
            canTargetPawns = false,
            canTargetBuildings = false,
            canTargetItems = false,
            canTargetFires = false
        };

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn caster = parent?.pawn;
            Thing thing = target.Thing;
            string reason;
            if (!base.Valid(target, throwMessages))
            {
                return false;
            }

            if (MugirlThrowUtility.CanPickUp(caster, thing, out reason))
            {
                return true;
            }

            if (throwMessages && !reason.NullOrEmpty())
            {
                Messages.Message(reason, thing ?? caster, MessageTypeDefOf.RejectInput, historical: false);
            }
            return false;
        }

        public override bool CanHitTarget(LocalTargetInfo target)
        {
            return target.IsValid
                && parent?.pawn?.Map != null
                && target.Cell.InBounds(parent.pawn.Map)
                && selectedTarget.IsValid
                && selectedTarget.Cell.DistanceTo(target.Cell) <= Props.range;
        }

        public override bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
        {
            if (CanHitTarget(target))
            {
                return true;
            }

            if (showMessages)
            {
                Messages.Message("Mugirl.Dunk.InvalidDestination".Translate(Props.range.ToString("0.#")), parent?.pawn, MessageTypeDefOf.RejectInput, historical: false);
            }
            return false;
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
