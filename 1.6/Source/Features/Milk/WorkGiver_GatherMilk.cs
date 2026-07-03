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
            if (comp != null && comp.IsManagedByMilkingDevice)
            {
                return false;
            }

            return base.HasJobOnThing(pawn, thing, forced);
        }
    }

}
