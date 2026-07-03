using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public class CompProperties_ReduceWillorEnslave : HediffCompProperties
    {
        public CompProperties_ReduceWillorEnslave()
        {
            compClass = typeof(HediffComp_ReduceWillorEnslave);
        }

        // 达到触发时间后可一次性削减意志或直接奴役囚犯。
        public int triggerTicks = 600;
        public bool reduceWill = false;
        public bool makeSlave = false;
        public float willReductionAmount = 10f;
    }

    public class HediffComp_ReduceWillorEnslave : HediffComp
    {
        private int age = 0;
        private bool triggered = false;

        public CompProperties_ReduceWillorEnslave Props => props as CompProperties_ReduceWillorEnslave;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            age++;

            CompProperties_ReduceWillorEnslave compProps = Props;
            Pawn pawn = Pawn;
            if (triggered || compProps == null)
            {
                return;
            }

            int triggerTicks = Mathf.Max(1, compProps.triggerTicks);
            if (age < triggerTicks || pawn == null || !pawn.IsPrisoner || !pawn.IsWearingCrackedBrainwashApparel())
            {
                return;
            }

            bool handled = false;
            if (compProps.reduceWill)
            {
                handled |= ReduceWill(pawn, compProps);
            }

            if (compProps.makeSlave)
            {
                handled |= MakeSlave(pawn);
            }

            if (!compProps.reduceWill && !compProps.makeSlave)
            {
                handled = true;
            }

            if (handled)
            {
                triggered = true;
            }
        }

        private bool ReduceWill(Pawn pawn, CompProperties_ReduceWillorEnslave compProps)
        {
            var guest = pawn?.guest;
            if (guest?.IsPrisoner != true)
            {
                return false;
            }

            if (compProps.willReductionAmount <= 0f)
            {
                return true;
            }

            float willpowerToReduce = Mathf.Min(guest.will, compProps.willReductionAmount);
            guest.will = Mathf.Max(0f, guest.will - willpowerToReduce);
            return true;
        }

        private bool MakeSlave(Pawn pawn)
        {
            var guest = pawn?.guest;
            Faction playerFaction = Faction.OfPlayerSilentFail;
            if (guest?.IsPrisoner != true || playerFaction == null)
            {
                return false;
            }

            guest.will = 0f;
            guest.SetGuestStatus(playerFaction, GuestStatus.Slave);
            return true;
        }
        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref age, "age", 0);
            Scribe_Values.Look(ref triggered, "triggered", false);
        }

    }

}
