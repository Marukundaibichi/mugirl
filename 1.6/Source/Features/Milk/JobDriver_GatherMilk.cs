using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MooGirl
{
    // 雪牛娘挤奶工作：在通用身体资源采集流程上加入动画、固定产量和喷乳反馈。
    public class JobDriver_GatherMilk : JobDriver_GatherBodyResources
    {
        public float WorktickSelf = 600f;
        public float WorktickOther = 600f;

        private const float FastMilkingWorkTicks = 60f;

        protected override float WorkTotal
        {
            get
            {
                if (MooGirlMod.Settings != null && MooGirlMod.Settings.enableFastMilking)
                {
                    return FastMilkingWorkTicks;
                }

                if (this.pawn == TargetPawn)
                {
                    return WorktickSelf;
                }
                else
                {
                    return WorktickOther;
                }
            }
        }

        protected override CompMooHasBodyResource GetComp(Pawn animal)
        {
            return animal?.TryGetComp<CompMooMilkable>();
        }

        protected override bool CanGather(CompMooHasBodyResource comp)
        {
            if (!base.CanGather(comp))
            {
                return false;
            }

            CompMooMilkable milkComp = comp as CompMooMilkable;
            return milkComp != null && !milkComp.IsManagedByMilkingDevice;
        }

        protected override void StartGatherEffects(Pawn doer, Pawn target)
        {
            MooGirlMilkingAnimation.Start(doer, target);
        }

        protected override void TickGatherEffects(Pawn doer, Pawn target)
        {
            MooGirlMilkingAnimation.Tick(doer, target);
        }

        protected override void EndGatherEffects(Pawn doer, Pawn target)
        {
            MooGirlMilkingAnimation.End(doer, target);
        }

        // 挤奶使用固定消耗量，避免一次性清空全部奶量。
        protected override void CompleteGather(Pawn doer)
        {
            CompMooMilkable comp = GetComp(TargetPawn) as CompMooMilkable;
            if (comp == null) return;

            if (!CanGather(comp) || !comp.GatheredFixed(doer, out int milkAmount))
            {
                return;
            }

            if (milkAmount > 0)
            {
                ThingDef milkDef = MooGirl_DefOf.MooGirl_Milk;
                MooGirlMilkOutputUtility.SpawnStacksNear(milkDef, milkAmount, doer.Position, doer.Map);
            }

            comp.SpawnMilkEffect();
        }
    }
}
