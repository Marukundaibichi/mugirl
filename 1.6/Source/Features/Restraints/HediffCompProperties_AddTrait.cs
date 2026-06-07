using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MooGirl
{
    public class CompProperties_AddTrait : HediffCompProperties
    {
        public CompProperties_AddTrait()
        {
            compClass = typeof(HediffComp_AddTrait);
        }

        public int triggerTicks = 600;
        public TraitDef traitDef;

        public List<PawnTypeTraitEntry> pawnTypeTraitEntries = new List<PawnTypeTraitEntry>();

        public class PawnTypeTraitEntry
        {
            public string pawnType;
            public TraitDef trait;
        }
    }

    public class HediffComp_AddTrait : HediffComp
    {
        private int age = 0;
        private bool triggered = false;

        public CompProperties_AddTrait Props => props as CompProperties_AddTrait;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            age++;

            CompProperties_AddTrait compProps = Props;
            if (triggered || compProps == null)
                return;

            int triggerTicks = compProps.triggerTicks < 1 ? 1 : compProps.triggerTicks;
            if (age < triggerTicks)
                return;

            if (Pawn?.Spawned != true || !Pawn.IsWearingCrackedBrainwashApparel())
                return;

            if (compProps.pawnTypeTraitEntries != null)
            {
                foreach (var entry in compProps.pawnTypeTraitEntries)
                {
                    if (entry == null || entry.trait == null || !MatchesPawnType(Pawn, entry.pawnType))
                    {
                        continue;
                    }

                    if (TryGainTrait(Pawn, entry.trait))
                    {
                        triggered = true;
                        return;
                    }
                }
            }

            if (!triggered && TryGainTrait(Pawn, compProps.traitDef))
            {
                triggered = true;
            }
        }

        private static bool TryGainTrait(Pawn pawn, TraitDef traitDef)
        {
            TraitSet traits = pawn?.story?.traits;
            if (traits == null || traitDef == null || traits.HasTrait(traitDef))
            {
                return false;
            }

            traits.GainTrait(new Trait(traitDef));
            return true;
        }

        private static bool MatchesPawnType(Pawn pawn, string pawnType)
        {
            if (pawn == null || string.IsNullOrWhiteSpace(pawnType))
            {
                return false;
            }

            switch (pawnType.Trim().ToLowerInvariant())
            {
                case "colonist":
                    return pawn.IsColonist;
                case "slave":
                    return pawn.IsSlave;
                case "prisoner":
                    return pawn.IsPrisoner;
                case "free":
                    return !pawn.IsSlave && !pawn.IsPrisoner;
                case "hostile":
                    return MooGirlWildSlaveUtility.IsHostileToPlayer(pawn);
                default:
                    return false;
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref age, "age", 0);
            Scribe_Values.Look(ref triggered, "triggered", false);
        }
    }
}
