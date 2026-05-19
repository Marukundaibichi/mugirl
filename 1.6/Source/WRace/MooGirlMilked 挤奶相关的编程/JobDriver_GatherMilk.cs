using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;


namespace MooGirl  // 定义命名空间MooGirl，用于组织相关类
{
    // 定义JobDriver_GatherMilk类，继承自JobDriver_GatherBodyResources，用于处理挤奶工作
    public class JobDriver_GatherMilk : JobDriver_GatherBodyResources
    {
        // 定义常量，表示当角色对自己挤奶时的工作时间（tick）
        public float WorktickSelf = 600f;
        // 定义常量，表示当角色对其他动物挤奶时的工作时间（tick）
        public float WorktickOther = 200f;

        // 重写WorkTotal属性，根据挤奶对象返回不同的总工作时间
        protected override float WorkTotal
        {
            get
            {
                // 判断当前角色是否是挤奶目标（即是否是自己挤奶）
                if (this.pawn == (Pawn)this.job.GetTarget(TargetIndex.A).Thing)
                {
                    return WorktickSelf;  // 返回对自己挤奶的工作时间
                }
                else
                {
                    return WorktickOther;  // 返回对其他动物挤奶的工作时间
                }
            }
        }

        // 重写GetComp方法，获取指定动物的挤奶组件
        protected override CompMooHasBodyResource GetComp(Pawn animal)
        {
            // 返回动物的挤奶组件实例（CompMooMilkable是挤奶功能的具体实现）
            return animal.GetComp<CompMooMilkable>();
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

        // 使用固定产量榨乳 + 喷乳特效
        protected override void CompleteGather(Pawn doer)
        {
            CompMooMilkable comp = GetComp((Pawn)((Thing)job.GetTarget(TargetIndex.A))) as CompMooMilkable;
            if (comp == null) return;

            if (comp.GatheredFixed(doer, out int milkAmount) && milkAmount > 0)
            {
                ThingDef milkDef = MooGirl_DefOf.MooGirl_Milk;
                while (milkAmount > 0)
                {
                    int stack = Mathf.Clamp(milkAmount, 1, milkDef.stackLimit);
                    milkAmount -= stack;
                    Thing thing = ThingMaker.MakeThing(milkDef);
                    thing.stackCount = stack;
                    GenPlace.TryPlaceThing(thing, doer.Position, doer.Map, ThingPlaceMode.Near);
                }
            }

            comp.SpawnMilkEffect();
        }
    }
}
