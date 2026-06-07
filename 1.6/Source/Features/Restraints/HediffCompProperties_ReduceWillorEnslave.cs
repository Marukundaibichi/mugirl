using RimWorld;
using UnityEngine;
using Verse;

namespace MooGirl
{
    public class CompProperties_ReduceWillorEnslave : HediffCompProperties
    {
        public CompProperties_ReduceWillorEnslave()
        {
            compClass = typeof(HediffComp_ReduceWillorEnslave);
        }

        // 多少个 tick 后触发
        public int triggerTicks = 600;

        // 是否削减意志力
        public bool reduceWill = false;

        // 是否直接转化为奴隶
        public bool makeSlave = false;

        // 意志力削减的量
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

            // 先检查 Pawn 是否穿着洗脑装备且已破解
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
            // 削减意志力
            if (compProps.reduceWill)
            {
                handled |= ReduceWill(pawn, compProps);
            }

            // 直接奴隶
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

        // 削减意志力
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

            // 计算削减的意志力
            float willpowerToReduce = Mathf.Min(guest.will, compProps.willReductionAmount);
            guest.will = Mathf.Max(0f, guest.will - willpowerToReduce);
            return true;
        }

        // 直接奴役囚犯
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
