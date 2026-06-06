using Verse;

namespace MooGirl
{
    public class CompProperties_SlaveApparelKey : CompProperties
    {
        public CompProperties_SlaveApparelKey()
        {
            compClass = typeof(CompSlaveApparelKey);
        }
    }
    public class CompSlaveApparelKey : ThingComp
    {
        public override bool AllowStackWith(Thing t)
        {
            return false;
        }
    }
}
