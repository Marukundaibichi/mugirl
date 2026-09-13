using Verse;

namespace Mugirl
{

    public class WorkGiver_GatherMilk : WorkGiver_GatherBodyResources
    {
        protected override JobDef JobDef
        {
            get
            {
                return Mugirl_DefOf.Job_GatherMilk;
            }
        }

        protected override CompMooHasBodyResource GetComp(Pawn animal)
        {
            return animal.TryGetComp<CompMooMilkable>();
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            Pawn target = thing as Pawn;
            CompMooMilkable comp = target?.TryGetComp<CompMooMilkable>();
            // 先用奶量和激活条件拒绝候选，避免为不能挤奶的目标扫描穿戴设备。
            // forced 仍允许任意正奶量，自动任务仍使用玩家设定的阈值。
            if (comp == null || (forced ? comp.Fullness <= 0f : comp.Fullness < comp.MilkThreshold)
                || !comp.Active || comp.IsManagedByMilkingDevice)
            {
                return false;
            }

            return base.HasJobOnThing(pawn, thing, forced);
        }
    }

}
