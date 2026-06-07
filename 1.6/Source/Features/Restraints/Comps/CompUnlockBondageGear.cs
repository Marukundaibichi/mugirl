using RimWorld;
using Verse;

namespace MooGirl
{

    public class CompUnlockBondageGear : CompUseEffect
    {
        public override float OrderPriority => -69;

        public override void DoEffect(Pawn p)
        {
            if (p == null || p.MapHeld == null || p.apparel == null)
                return;

            base.DoEffect(p);

            Apparel lockedApparel = null;
            for (int i = 0; i < p.apparel.LockedApparel.Count; i++)
            {
                if (p.apparel.LockedApparel[i].SatisfiesKey(parent))
                {
                    lockedApparel = p.apparel.LockedApparel[i];
                    break;
                }
            }

            if (lockedApparel is SlaveApparel apparel)
            {
                int previousLockCount = apparel.lockCount;
                apparel.lockCount--;

                if (apparel.lockCount <= 0)
                {
                    apparel.isLocked = false;
                    apparel.lockCount = 0;

                    if (p.apparel.TryDrop(lockedApparel, out Apparel _, p.PositionHeld, false))
                    {
                        parent.Destroy();
                    }
                    else
                    {
                        apparel.lockCount = previousLockCount > 0 ? previousLockCount : 1;
                        apparel.isLocked = true;
                    }
                }
                else if (!parent.Destroyed)
                {
                    parent.Destroy();
                }
            }
        }
    }

}
