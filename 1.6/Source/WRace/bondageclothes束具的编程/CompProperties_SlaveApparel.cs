using System.Linq;
using RimWorld;
using Verse;

namespace MooGirl
{
    public class CompProperties_SlaveApparel : CompProperties
    {
        public CompProperties_SlaveApparel()
        {
            compClass = typeof(Comp_SlaveApparel);
        }
    }

    public class Comp_SlaveApparel : ThingComp
    {
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (parent is Apparel_SlaveApparel ap && ap.Wearer != null)
            {
                // 获取穿戴者
                Pawn wearer = ap.Wearer;

                // 直接锁定该Pawn的所有服装
                wearer.apparel?.LockAll();
            }
        }
    }



}
