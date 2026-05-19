using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MooGirl
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
                defaultLabel = $"Dev: 解锁 {apparel.LabelCap}",
                defaultDesc = $"开发者模式下立即解除 {apparel.LabelCap} 的穿戴锁定，便于调试。",
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
            Messages.Message($"已开发者解锁：{wearer.LabelShortCap} 的 {apparel.LabelCap}", wearer, MessageTypeDefOf.PositiveEvent);
        }
    }
}
