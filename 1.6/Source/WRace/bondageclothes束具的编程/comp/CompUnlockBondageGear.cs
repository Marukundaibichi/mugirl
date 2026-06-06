using RimWorld;
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

            Apparel lockedApparel = null;
            for (int i = 0; i < p.apparel.WornApparel.Count; i++)
            {
                if (p.apparel.WornApparel[i].IsSlaveApparel())
                {
                    lockedApparel = p.apparel.WornApparel[i];
                    break;
                }
            }

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
