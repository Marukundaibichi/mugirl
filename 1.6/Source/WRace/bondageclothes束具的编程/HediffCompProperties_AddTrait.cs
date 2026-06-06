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

        public CompProperties_AddTrait Props => (CompProperties_AddTrait)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            age++;

            if (triggered || age < Props.triggerTicks)
                return;

            if (Pawn?.Spawned != true || !Pawn.IsWearingCrackedBrainwashApparel())
                return;

            foreach (var entry in Props.pawnTypeTraitEntries)
            {
                if (MatchesPawnType(Pawn, entry.pawnType))
                {
                    if (!Pawn.story?.traits.HasTrait(entry.trait) ?? true)
                    {
                        Pawn.story?.traits.GainTrait(new Trait(entry.trait));
                        triggered = true;
                        break;
                    }
                }
            }

            // 旧方式 fallback（traitDef）
            if (!triggered && Props.traitDef != null)
            {
                if (!Pawn.story?.traits.HasTrait(Props.traitDef) ?? true)
                {
                    Pawn.story?.traits.GainTrait(new Trait(Props.traitDef));
                    triggered = true;
                }
            }
        }

        private bool MatchesPawnType(Pawn pawn, string pawnType)
        {
            switch (pawnType.ToLower())
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
                    return pawn.HostileTo(Faction.OfPlayer);
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
