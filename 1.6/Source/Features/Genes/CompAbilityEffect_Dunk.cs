using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    internal static class MugirlDunkUtility
    {
        internal static bool CanDunk(Pawn caster, Pawn victim, out BodyPartRecord head, out string reason)
        {
            head = null;
            reason = null;
            if (!Prefs.DevMode)
            {
                reason = "Mugirl.Dunk.DevOnly".Translate().ToString();
                return false;
            }

            if (caster == null || victim == null || caster == victim || victim.Destroyed || victim.Dead
                || !victim.Spawned || victim.Map != caster.Map || !victim.RaceProps.Humanlike
                || victim.story?.headType == null)
            {
                reason = "Mugirl.Dunk.InvalidTarget".Translate().ToString();
                return false;
            }

            if (MugirlThrowUtility.FindHeldController(caster) != null
                || MugirlThrowUtility.FindDunkController(caster) != null)
            {
                reason = "Mugirl.Throw.AlreadyHolding".Translate().ToString();
                return false;
            }

            List<BodyPartRecord> parts = victim.RaceProps.body.AllParts;
            for (int i = 0; i < parts.Count; i++)
            {
                BodyPartRecord part = parts[i];
                if (part.def == BodyPartDefOf.Head && !victim.health.hediffSet.PartIsMissing(part))
                {
                    head = part;
                    return true;
                }
            }

            reason = "Mugirl.Dunk.NoHead".Translate(victim.LabelCap).ToString();
            return false;
        }
    }

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
        public override bool ShouldHideGizmo => !Prefs.DevMode;
        public override bool CanCast => Prefs.DevMode && base.CanCast;

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
            Pawn victim = target.Thing as Pawn;
            BodyPartRecord head;
            string reason;
            if (!base.Valid(target, throwMessages))
            {
                return false;
            }

            if (MugirlDunkUtility.CanDunk(caster, victim, out head, out reason))
            {
                return true;
            }

            if (throwMessages && !reason.NullOrEmpty())
            {
                Messages.Message(reason, (Thing)victim ?? caster, MessageTypeDefOf.RejectInput, historical: false);
            }
            return false;
        }

        public override bool CanHitTarget(LocalTargetInfo target)
        {
            return target.IsValid
                && parent?.pawn?.Map != null
                && target.Cell.InBounds(parent.pawn.Map)
                && selectedTarget.IsValid
                && selectedTarget.Cell.DistanceTo(target.Cell) <= Props.range
                && Thing_MugirlDunkProp.TryFindLandingCell(parent.pawn, target.Cell, out _);
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
            Pawn victim = target.Thing as Pawn;
            if (victim == null)
            {
                return null;
            }

            return "Mugirl.Dunk.PickupReadout".Translate(victim.LabelShortCap).ToString();
        }
    }
}
