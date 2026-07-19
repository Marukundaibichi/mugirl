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

        public static JobDef Job_LoadWeaponWheel;

        public static ThingDef Mugirl_AutoloadingSystem;

        public static ResearchProjectDef Mugirl_Autoloading;

        public static HediffDef Mugirl_Armbinder;

        public static HediffDef Mugirl_Abasia;

        public static HediffDef Mugirl_LinkedSwordDance;

        [MayRequire("Ludeon.RimWorld.Biotech")]
        public static HediffDef Mugirl_Stun;

        [MayRequire("Ludeon.RimWorld.Biotech")]
        public static HediffDef Mugirl_Charge;

        #if MUGIRL_THROW_SYSTEM
        [MayRequire("Ludeon.RimWorld.Biotech")]
        public static AbilityDef Mugirl_Throw;

        [MayRequire("Ludeon.RimWorld.Biotech")]
        public static ThingDef Mugirl_ThrowController;

        [MayRequire("Ludeon.RimWorld.Biotech")]
        public static DamageDef Mugirl_ThrownImpact;

        [MayRequire("Ludeon.RimWorld.Biotech")]
        public static AbilityDef Mugirl_Dunk;

        [MayRequire("Ludeon.RimWorld.Biotech")]
        public static JobDef Job_MugirlDunk;

        [MayRequire("Ludeon.RimWorld.Biotech")]
        public static ThingDef Mugirl_DunkProp;

        [MayRequire("Ludeon.RimWorld.Biotech")]
        public static ThingDef Mugirl_DunkFlyer;
        #endif

        public static FleckDef MugirlWashMentalEyes;

        public static TraitDef Mugirl_BrainWashObey;

        public static PawnKindDef Mugirl_Slave;

        public static PawnKindDef Mugirl_Beginning_Slave;

        public static PawnKindDef Mugirl_EscapeSpaceSlave;

        public static PawnKindDef Mugirl_EscapeWanderSlave;

        public static PawnKindDef Mugirl_EscapeWildSlave;

        public static PawnKindDef Mugirl_PreEscapeWildSlave;

        public static PawnKindDef Mugirl_WildMugirl;

        public static InteractionDef Mugirl_Nuzzle;

        public static ThoughtDef Mugirl_Nuzzled;

        public static QuestScriptDef Mugirl_SlaveOpeningPodCrash;

        public static RimWorld.BackstoryDef Mugirl_Newborn;

        public static RimWorld.BackstoryDef Mugirl_Colonist;

        [MayRequire("Ludeon.RimWorld.Biotech")]
        public static XenotypeDef Mugirl_Xenotype;
    }
}
