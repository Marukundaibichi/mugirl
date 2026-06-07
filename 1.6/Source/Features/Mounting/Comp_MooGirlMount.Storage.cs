using System.Collections.Generic;
using Verse;

namespace MooGirl
{
    public partial class Comp_MooGirlMount
    {
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_Values.Look(ref fireAtWill, "fireAtWill", Props?.turretFireAtWillDefault ?? true);
            Scribe_Values.Look(ref physiologicalTickCounter, "physiologicalTickCounter", 0);
            Scribe_Values.Look(ref safetyTickCounter, "safetyTickCounter", 0);
            Scribe_Values.Look(ref turretTickCounter, "turretTickCounter", 0);
            Scribe_Values.Look(ref riderMeleeTickCounter, "riderMeleeTickCounter", 0);
            Scribe_Values.Look(ref turretBurstCooldownTicksLeft, "turretBurstCooldownTicksLeft", 0);
            Scribe_TargetInfo.Look(ref turretAimTarget, "turretAimTarget");
            Scribe_Values.Look(ref turretAimTicksLeft, "turretAimTicksLeft", 0);
            Scribe_Values.Look(ref turretAimTicksTotal, "turretAimTicksTotal", 0);
            Scribe_Values.Look(ref turretCastStartTick, "turretCastStartTick", -1);
            Scribe_TargetInfo.Look(ref turretLastAttackedTarget, "turretLastAttackedTarget");
            Scribe_Values.Look(ref turretLastAttackTargetTick, "turretLastAttackTargetTick", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                MakeContainer();
            }
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            MakeContainer();
            return innerContainer;
        }
    }
}
