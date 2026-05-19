using System;
using System.Linq;
using RimWorld;
using Verse;

namespace MooGirlRace
{
    internal class Thought_Hediff : Thought_Memory
    {
        public override void ExposeData()
        {
            Scribe_Values.Look<bool>(ref this.added, "added", false, false);
        }

        public override float MoodOffset()
        {
            bool flag = !this.added;
            if (flag)
            {
                bool flag2 = !ThoughtUtility.ThoughtNullified(this.pawn, this.def);
                if (flag2)
                {
                    bool flag3 = this.def.hediff != null;
                    if (flag3)
                    {
                        this.pawn.health.AddHediff(this.def.hediff, null, null, null);
                    }
                    bool flag4 = this.def.HasModExtension<Thought_Hediff_Extension>();
                    if (flag4)
                    {
                        Thought_Hediff_Extension modExtension = this.def.GetModExtension<Thought_Hediff_Extension>();
                        bool flag5 = modExtension.hediffToAffect != null;
                        if (flag5)
                        {
                            BodyPartRecord part = this.pawn.RaceProps.body.GetPartsWithDef(modExtension.partToAffect).FirstOrDefault<BodyPartRecord>();
                            this.pawn.health.AddHediff(modExtension.hediffToAffect, part, null, null);
                            this.pawn.health.hediffSet.GetFirstHediffOfDef(modExtension.hediffToAffect, false).Severity += modExtension.percentage;
                        }
                        bool flag6 = modExtension.secondHediffToAffect != null;
                        if (flag6)
                        {
                            BodyPartRecord part2 = this.pawn.RaceProps.body.GetPartsWithDef(modExtension.secondPartToAffect).FirstOrDefault<BodyPartRecord>();
                            this.pawn.health.AddHediff(modExtension.secondHediffToAffect, part2, null, null);
                            this.pawn.health.hediffSet.GetFirstHediffOfDef(modExtension.secondHediffToAffect, false).Severity += modExtension.secondPercentage;
                        }
                        bool increaseJoy = modExtension.increaseJoy;
                        if (increaseJoy)
                        {
                            this.pawn.needs.joy.GainJoy(modExtension.extraJoy, JoyKindDefOf.Gluttonous);
                        }
                    }
                }
                this.added = true;
            }
            return base.MoodOffset();
        }

        public bool added = false;
    }
    public class Thought_Hediff_Extension : DefModExtension
    {
        public HediffDef hediffToAffect = null;

        public BodyPartDef partToAffect = null;

        public float percentage = 1f;

        public HediffDef secondHediffToAffect = null;

        public BodyPartDef secondPartToAffect = null;

        public float secondPercentage = 1f;

        public bool increaseJoy = false;

        public float extraJoy = 0f;
    }
}
