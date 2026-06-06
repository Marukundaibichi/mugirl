using RimWorld;
using System.Collections.Generic;
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

        public CompProperties_ReduceWillorEnslave Props => (CompProperties_ReduceWillorEnslave)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            age++;

            // 先检查 Pawn 是否穿着洗脑装备且已破解
            if ( age >= Props.triggerTicks && Pawn.IsPrisoner && Pawn.IsWearingCrackedBrainwashApparel())
            {
                // 削减意志力
                if (Props.reduceWill)
                {
                    ReduceWill();
                }

                // 直接奴隶
                if (Props.makeSlave)
                {
                    MakeSlave();
                }
            }

        }

        // 削减意志力
        private void ReduceWill()
        {
            if (Pawn.guest.IsPrisoner )
            {
                // 计算削减的意志力
                float willpowerToReduce = Mathf.Min(Pawn.guest.will, Props.willReductionAmount);
                Pawn.guest.will = Mathf.Max(0f, Pawn.guest.will - willpowerToReduce);
            }
        }

        // 直接奴役囚犯
        private void MakeSlave()
        {
            if (Pawn.IsPrisoner)
            {
                Pawn.guest.will = 0f;
                Pawn.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Slave);
            }
        }
        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref age, "age", 0);
        }

    }

}
