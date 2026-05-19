using Verse;

namespace MooGirl
{
    public class CompProperties_SlaveApperalKey : CompProperties
    {
        public CompProperties_SlaveApperalKey()
        {
            compClass = typeof(CompSlaveApperalKey);
        }
    }
    public class CompSlaveApperalKey : ThingComp
    {
        public override bool AllowStackWith(Thing t)
        {
            return false;
        }
    }
}
