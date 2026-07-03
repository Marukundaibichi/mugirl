using RimWorld;
using Verse;

namespace Mugirl
{
    [DefOf]
    public static class Mugirl_DefOf
    {
        public static ThingDef WallRopeHitch;

        public static ThingDef Mugirl;

        public static ThingDef Mugirl_Milk;

        public static BodyDef MugirlBody;

        public static JobDef Job_GatherMilk;

        public static JobDef Job_MugirlMilkingTargetWait;

        public static JobDef JobDriver_RopeMoo;

        public static JobDef JobDriver_RemoveRopeMoo;

        public static JobDef RopeToBuild;

        public static JobDef Job_Nuzzle;

        public static JobDef Job_FollowRoper;

        public static JobDef Job_MountMugirl;

        public static JobDef Job_DismountMugirl;

        public static HediffDef Mugirl_Armbinder;

        public static HediffDef Mugirl_Abasia;

        [MayRequire("Ludeon.RimWorld.Biotech")]
        public static HediffDef Mugirl_Stun;

        [MayRequire("Ludeon.RimWorld.Biotech")]
        public static HediffDef Mugirl_Charge;

        public static FleckDef MugirlWashMentalEyes;

        public static TraitDef Mugirl_BrainWashObey;

        public static PawnKindDef Mugirl_Slave;

        public static PawnKindDef Mugirl_Beginning_Slave;

        public static PawnKindDef Mugirl_EscapeSpaceSlave;

        public static PawnKindDef Mugirl_EscapeWanderSlave;

        public static PawnKindDef Mugirl_EscapeWildSlave;

        public static PawnKindDef Mugirl_PreEscapeWildSlave;

        public static InteractionDef Mugirl_Nuzzle;

        public static ThoughtDef Mugirl_Nuzzled;

        public static QuestScriptDef Mugirl_SlaveOpeningPodCrash;

        public static RimWorld.BackstoryDef Mugirl_Newborn;

        public static RimWorld.BackstoryDef Mugirl_Colonist;

        [MayRequire("Ludeon.RimWorld.Biotech")]
        public static XenotypeDef Mugirl_Xenotype;
    }
}
