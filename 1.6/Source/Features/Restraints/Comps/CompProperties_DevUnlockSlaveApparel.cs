using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public class CompProperties_DevUnlockSlaveApparel : CompProperties
    {
        public CompProperties_DevUnlockSlaveApparel()
        {
            compClass = typeof(Comp_DevUnlockSlaveApparel);
        }
    }

    public class Comp_DevUnlockSlaveApparel : ThingComp
    {
        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetWornGizmosExtra())
            {
                yield return gizmo;
            }

            if (!Prefs.DevMode || !(parent is SlaveApparel apparel) || apparel.Wearer == null)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "Mugirl.Restraints.DevUnlock.Label".Translate(apparel.LabelCap),
                defaultDesc = "Mugirl.Restraints.DevUnlock.Desc".Translate(apparel.LabelCap),
                icon = TexCommand.ClearPrioritizedWork,
                action = () => DevUnlock(apparel)
            };
        }

        private static void DevUnlock(SlaveApparel apparel)
        {
            Pawn wearer = apparel.Wearer;
            if (wearer == null)
            {
                return;
            }

            apparel.isLocked = false;
            apparel.lockCount = 0;
            wearer.apparel?.Unlock(apparel);
            Messages.Message("Mugirl.Restraints.DevUnlock.Message".Translate(wearer.LabelShortCap, apparel.LabelCap), wearer, MessageTypeDefOf.PositiveEvent);
        }
    }
}
