using RimWorld;
using System.Linq;
using Verse;

namespace MooGirl
{

    public class CompUnlockBondageGear : CompUseEffect
    {
        public override float OrderPriority => -69;

        public override void DoEffect(Pawn p)
        {
            base.DoEffect(p);

            if (p.MapHeld == null || p.apparel == null)
                return;

            Apparel lockedApparel = p.apparel.WornApparel.FirstOrDefault(a => a.IsSlaveApparel());

            if (lockedApparel is SlaveApparel apparel)
            {
                apparel.lockCount--;

                if (apparel.lockCount <= 0)
                {
                    apparel.isLocked = false;
                    p.apparel.Remove(lockedApparel);
                    Thing dropped = null;

                    if (GenThing.TryDropAndSetForbidden(lockedApparel, p.Position, p.MapHeld, ThingPlaceMode.Near, out dropped, false))
                    {
                        parent.Destroy();
                    }
                    else
                    {
                        apparel.lockCount = 1;
                    }
                }
            }
        }
    }

}
