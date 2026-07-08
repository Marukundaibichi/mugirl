using RimWorld;
using Verse;

namespace Mugirl
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
            for (int i = 0; i < p.apparel.WornApparel.Count; i++)
            {
                Apparel wornApparel = p.apparel.WornApparel[i];
                if (p.IsWornLockedSlaveApparel(wornApparel) && wornApparel.SatisfiesKey(parent))
                {
                    lockedApparel = wornApparel;
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
                    p.apparel.Unlock(lockedApparel);

                    bool dropped = false;
                    SlaveApparelAutoStageContext.BeginSuppressNextStage();
                    try
                    {
                        dropped = p.apparel.TryDrop(lockedApparel, out Apparel _, p.PositionHeld, false);
                    }
                    finally
                    {
                        SlaveApparelAutoStageContext.EndSuppressNextStage();
                    }

                    if (dropped)
                    {
                        parent.Destroy();
                    }
                    else
                    {
                        apparel.lockCount = previousLockCount > 0 ? previousLockCount : 1;
                        apparel.isLocked = true;
                        p.apparel.Lock(lockedApparel);
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
