using System.Collections.Generic;
using Verse;

namespace Mugirl
{
    internal enum MugirlPatchRiskLevel
    {
        Low,
        Medium,
        High,
        Critical
    }

    internal sealed class MugirlPatchInfo
    {
        internal MugirlPatchInfo(
            string moduleName,
            string patchClassName,
            string targetTypeName,
            string targetMethodName,
            string patchKind,
            bool maySkipOriginal,
            MugirlPatchRiskLevel compatibilityRisk,
            string failureBehavior,
            string registryNameKey = null)
        {
            ModuleName = moduleName;
            PatchClassName = patchClassName;
            TargetTypeName = targetTypeName;
            TargetMethodName = targetMethodName;
            PatchKind = patchKind;
            MaySkipOriginal = maySkipOriginal;
            CompatibilityRisk = compatibilityRisk;
            FailureBehavior = failureBehavior;
            RegistryNameKey = registryNameKey;
        }

        internal string ModuleName { get; }
        internal string PatchClassName { get; }
        internal string PatchClassFullName => typeof(MugirlPatchInfo).Namespace + "." + PatchClassName;
        internal string TargetTypeName { get; }
        internal string TargetMethodName { get; }
        internal string PatchKind { get; }
        internal bool MaySkipOriginal { get; }
        internal MugirlPatchRiskLevel CompatibilityRisk { get; }
        internal string FailureBehavior { get; }
        internal string RegistryNameKey { get; }
        internal bool IsManualPatch => !string.IsNullOrEmpty(RegistryNameKey);
    }

    internal static class MugirlPatchCatalog
    {
        // StaticCacheLifecycle: 进程级不可变 patch 元数据；不持有游戏对象或 Def 实例。
        private static readonly List<MugirlPatchInfo> patchInfos = new List<MugirlPatchInfo>
        {
            Info(moduleName: "Apparel", patchClassName: "Harmony_Apparel_DrawColor", targetTypeName: "RimWorld.Apparel", targetMethodName: "DrawColor", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "The spacesuit uses the base apparel draw color path."),

            Info(moduleName: "Genes", patchClassName: "Harmony_PawnUtility_TrySpawnHatchedOrBornPawn_MugirlXenotype", targetTypeName: "RimWorld.PawnUtility", targetMethodName: "TrySpawnHatchedOrBornPawn", patchKind: "Prefix/Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Birth-time xenotype correction is skipped; the low-frequency game component remains as fallback."),

            Info(moduleName: "Incidents", patchClassName: "Harmony_GiantCorpRaidTiers", targetTypeName: "RimWorld.PawnGroupMaker", targetMethodName: "CanGenerateFrom", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Giant corporation raid tier filtering falls back to the base pawn-group result."),
            Info(moduleName: "Incidents", patchClassName: "IsWildMan_WildManUtility_Patch", targetTypeName: "RimWorld.WildManUtility", targetMethodName: "IsWildMan", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Escape wild slaves are no longer exposed through the vanilla wild-man helper."),
            Info(moduleName: "Incidents", patchClassName: "RecruitUtility_Recruit_Patch", targetTypeName: "RimWorld.RecruitUtility", targetMethodName: "Recruit", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Escape wild-slave cleanup after recruitment is skipped."),
            Info(moduleName: "Incidents", patchClassName: "TameUtility_CanTame_Patch", targetTypeName: "RimWorld.TameUtility", targetMethodName: "CanTame", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Escape wild slaves are no longer accepted by the tame entry point."),
            Info(moduleName: "Incidents", patchClassName: "DesignatorTame_CanDesignateThing_Patch", targetTypeName: "RimWorld.Designator_Tame", targetMethodName: "CanDesignateThing", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Escape wild slaves lose the tame designator UI entry."),
            Info(moduleName: "Incidents", patchClassName: "Pawn_SetFaction_MugirlWildSlaveCleanup_Patch", targetTypeName: "Verse.Pawn", targetMethodName: "SetFaction", patchKind: "Prefix/Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Escape wild-slave normalization after joining the player is skipped."),
            Info(moduleName: "Incidents", patchClassName: "PawnGenerator_GeneratePawn_MugirlWildSlaveBirth_Patch", targetTypeName: "Verse.PawnGenerator", targetMethodName: "GeneratePawn(PawnGenerationRequest)", patchKind: "Prefix/Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Player-faction newborn escape wild slaves keep the base generated kind."),
            Info(moduleName: "Incidents", patchClassName: "PawnMindState_CheckStartMentalStateBecauseRecruitAttempted_MugirlEvents_Patch", targetTypeName: "Verse.AI.Pawn_MindState", targetMethodName: "CheckStartMentalStateBecauseRecruitAttempted", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Passive event pawns may start the base recruit-attempt mental state."),
            Info(moduleName: "Incidents", patchClassName: "PawnMindState_NotifyDamageTaken_MugirlEvents_Patch", targetTypeName: "Verse.AI.Pawn_MindState", targetMethodName: "Notify_DamageTaken", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Passive event pawns use the base damage reaction path."),
            Info(moduleName: "Incidents", patchClassName: "Pawn_PreApplyDamage_MugirlMigrationFlee_Patch", targetTypeName: "Verse.Pawn", targetMethodName: "PreApplyDamage", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Migration Mugirls only flee after damage is actually recorded."),
            Info(moduleName: "Incidents", patchClassName: "Pawn_ShouldShowQuestionMark_MugirlFusionInvestor_Patch", targetTypeName: "Verse.Pawn", targetMethodName: "ShouldShowQuestionMark", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Fusion investors may not show the question-mark interaction indicator."),

            Info(moduleName: "Milk", patchClassName: "Harmony_ChildcareUtility_CanBreastfeed_Mugirl", targetTypeName: "RimWorld.ChildcareUtility", targetMethodName: "CanBreastfeed", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Mugirl milk sources no longer satisfy the base breastfeeding availability check."),
            Info(moduleName: "Milk", patchClassName: "Harmony_ChildcareUtility_CanBreastfeedNow_Mugirl", targetTypeName: "RimWorld.ChildcareUtility", targetMethodName: "CanBreastfeedNow", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Mugirl milk sources no longer satisfy the immediate breastfeeding check."),
            Info(moduleName: "Milk", patchClassName: "Harmony_ChildcareUtility_SuckleFromLactatingPawn_Mugirl", targetTypeName: "RimWorld.ChildcareUtility", targetMethodName: "SuckleFromLactatingPawn", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Babies use the base lactation path and will not consume Mugirl milk fullness."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MugirlNurtureGrowthPoints", targetTypeName: "RimWorld.Pawn_AgeTracker", targetMethodName: "GrowthPointsPerDay", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Nurture hediffs no longer alter child growth-point gain."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MugirlNurturedSkillLearnCap", targetTypeName: "RimWorld.SkillRecord", targetMethodName: "Learn", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "The nurtured trait no longer relaxes the daily full-rate XP cap."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MugirlMilkingAnimation_DisableCachedPawnRender", targetTypeName: "Verse.PawnRenderer", targetMethodName: "ParallelGetPreRenderResults", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Active milking animation may be rendered through cached pawn results."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MugirlMilkingAnimation_PawnMatrix", targetTypeName: "Verse.PawnRenderer", targetMethodName: "GetDrawParms", patchKind: "Prefix/Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Milking animation body facing and draw matrix adjustments are skipped."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MugirlMilkingAnimation_NodeTransform", targetTypeName: "Verse.PawnRenderNode", targetMethodName: "GetTransform", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Milking animation render-node offsets are skipped."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MugirlMilkingAnimation_PawnDeSpawn", targetTypeName: "Verse.Pawn", targetMethodName: "DeSpawn", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Milking animation state cleanup waits for other cleanup paths."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MugirlMilkingAnimation_PawnDestroy", targetTypeName: "Verse.Pawn", targetMethodName: "Destroy", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Milking animation state cleanup waits for other cleanup paths."),

            Info(moduleName: "Misc", patchClassName: "Patch_DrugAdministerDefs", targetTypeName: "RimWorld.RecipeDefGenerator", targetMethodName: "DrugAdministerDefs", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "The generated administer-milk recipe is not added."),
            Info(moduleName: "Misc", patchClassName: "Harmony_GhoulRenderingRefresh_PostAdd", targetTypeName: "Verse.Hediff", targetMethodName: "PostAdd", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Ghoul graphics refresh relies on later renderer refreshes."),
            Info(moduleName: "Misc", patchClassName: "Harmony_MugirlMigrationFixedIngestNutrition", targetTypeName: "Verse.Thing", targetMethodName: "Ingested", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Migration Mugirl ingest nutrition falls back to the eaten thing's base nutrition."),
            Info(moduleName: "Misc", patchClassName: "Harmony_MugirlGrazeChain", targetTypeName: "Verse.AI.JobDriver", targetMethodName: "Cleanup", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Mugirl grazing jobs no longer chain to the next nearby plant."),
            Info(moduleName: "Misc", patchClassName: "Harmony_MugirlGrazeIngest", targetTypeName: "RimWorld.JobDriver_Ingest", targetMethodName: "PrepareToIngestToils", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Mugirls use the base tool-user ingest preparation for map plants, which may pick plants up before eating."),
            Info(moduleName: "Misc", patchClassName: "Harmony_MugirlGrazeFloatMenu", targetTypeName: "RimWorld.FloatMenuOptionProvider_Ingest", targetMethodName: "GetSingleOptionFor(Thing, FloatMenuContext)", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Manual Mugirl plant-ingest orders use the base menu action, which tries to unforbid non-forbiddable plants."),

            Info(moduleName: "Mounting", patchClassName: "Harmony_MountRendering", targetTypeName: "Verse.Pawn", targetMethodName: "DynamicDrawPhaseAt", patchKind: "Prefix/Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Mounted rider and weapon rendering is skipped."),
            Info(moduleName: "Mounting", patchClassName: "Harmony_MountedPawnGizmos", targetTypeName: "Verse.Pawn", targetMethodName: "GetGizmos", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Mounted-pawn command gizmos fall back to the base pawn list."),
            Info(moduleName: "Mounting", patchClassName: "Harmony_MountedCombatController_WarmupTime", targetTypeName: "Verse.Verb", targetMethodName: "WarmupTime", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Mounted combat warmup override is skipped."),
            Info(moduleName: "Mounting", patchClassName: "Harmony_MountedCombatController_StanceWarmupDraw", targetTypeName: "Verse.Stance_Warmup", targetMethodName: "StanceDraw", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Mounted verbs use the base warmup stance draw path."),
            Info(moduleName: "Mounting", patchClassName: "Harmony_MountedCombatController_EquipmentRemoved", targetTypeName: "RimWorld.Pawn_EquipmentTracker", targetMethodName: "Notify_EquipmentRemoved", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Mounted combat verb cleanup after equipment removal is skipped."),

            Info(moduleName: "Newborn", patchClassName: "Harmony_PawnGenerator_NewbornVisuals", targetTypeName: "Verse.PawnGenerator", targetMethodName: "GeneratePawn(PawnGenerationRequest)", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Generated Mugirl newborns keep the base body visual state."),
            Info(moduleName: "Newborn", patchClassName: "Harmony_PawnRenderNode_Hair_MugirlBaby", targetTypeName: "Verse.PawnRenderNode_Hair", targetMethodName: "GraphicFor", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Mugirl babies keep the base baby hair rendering suppression."),
            Info(moduleName: "Newborn", patchClassName: "Harmony_PawnAgeTracker_RecalculateLifeStageIndex_MugirlBodyType", targetTypeName: "RimWorld.Pawn_AgeTracker", targetMethodName: "RecalculateLifeStageIndex", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Life-stage body normalization waits for later explicit refreshes."),

            Info(moduleName: "Restraints", patchClassName: "PawnGenerator_GeneratePawn_Patch", targetTypeName: "Verse.PawnGenerator", targetMethodName: "GeneratePawn(PawnGenerationRequest)", patchKind: "Manual Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Generated slave apparel is not locked by the post-generation fallback.", registryNameKey: "Mugirl.PatchRegistry.PawnGeneratorGeneratePawn"),
            Info(moduleName: "Restraints", patchClassName: "ITab_Pawn_Gear_DrawThingRow_Transpiler", targetTypeName: "RimWorld.ITab_Pawn_Gear", targetMethodName: "DrawThingRow", patchKind: "Transpiler", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Critical, failureBehavior: "Advanced slave-apparel drop tooltip injection is skipped and a once-warning is emitted."),
            Info(moduleName: "Restraints", patchClassName: "Pawn_ApparelTracker_IsLocked_SlaveApparel_Patch", targetTypeName: "Verse.Pawn_ApparelTracker", targetMethodName: "IsLocked", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Locked slave apparel is not reported through the base apparel lock query."),
            Info(moduleName: "Restraints", patchClassName: "Pawn_ApparelTracker_TryDropFull_SlaveApparel_Patch", targetTypeName: "Verse.Pawn_ApparelTracker", targetMethodName: "TryDrop(Apparel, out Apparel, IntVec3, bool)", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Locked slave apparel can be removed through the full TryDrop overload."),
            Info(moduleName: "Restraints", patchClassName: "Pawn_ApparelTracker_TryDropWithResult_SlaveApparel_Patch", targetTypeName: "Verse.Pawn_ApparelTracker", targetMethodName: "TryDrop(Apparel, out Apparel)", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Locked slave apparel can be removed through the TryDrop overload that returns the dropped apparel."),
            Info(moduleName: "Restraints", patchClassName: "Pawn_ApparelTracker_TryDropSimple_SlaveApparel_Patch", targetTypeName: "Verse.Pawn_ApparelTracker", targetMethodName: "TryDrop(Apparel)", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Locked slave apparel can be removed through the simple TryDrop overload."),
            Info(moduleName: "Restraints", patchClassName: "Pawn_ApparelTracker_WouldReplaceLockedApparel_SlaveApparel_Patch", targetTypeName: "Verse.Pawn_ApparelTracker", targetMethodName: "WouldReplaceLockedApparel", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Force-wear checks ignore locked slave apparel that would be replaced by the new apparel."),
            Info(moduleName: "Restraints", patchClassName: "FloatMenuOptionProvider_Wear_SlaveApparel_Patch", targetTypeName: "RimWorld.FloatMenuOptionProvider_Wear", targetMethodName: "GetSingleOptionFor(Thing, FloatMenuContext)", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "The wear float menu may offer apparel that would replace locked slave apparel."),
            Info(moduleName: "Restraints", patchClassName: "JobDriver_Wear_TryMakePreToilReservations_SlaveApparel_Patch", targetTypeName: "RimWorld.JobDriver_Wear", targetMethodName: "TryMakePreToilReservations", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Wear jobs can reserve apparel that would replace locked slave apparel."),
            Info(moduleName: "Restraints", patchClassName: "JobDriver_Wear_TryUnequipSomething_SlaveApparel_Patch", targetTypeName: "RimWorld.JobDriver_Wear", targetMethodName: "TryUnequipSomething", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Wear jobs can proceed to unequip locked slave apparel while making room for new apparel."),
            Info(moduleName: "Restraints", patchClassName: "Patch_SlaveRebellionUtility_InitiateSlaveRebellionMtbDays", targetTypeName: "RimWorld.SlaveRebellionUtility", targetMethodName: "InitiateSlaveRebellionMtbDays", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Brainwash-obey pawns use the base rebellion MTB calculation."),
            Info(moduleName: "Roping", patchClassName: "Building_Door_PawnCanOpen_Patch", targetTypeName: "RimWorld.Building_Door", targetMethodName: "PawnCanOpen", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Roped followers use the base door-opening permission."),
            Info(moduleName: "Roping", patchClassName: "Patch_PawnDraftController_GetGizmos", targetTypeName: "RimWorld.Pawn_DraftController", targetMethodName: "GetGizmos", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Draft gizmo disabling while roped is skipped."),
            Info(moduleName: "Roping", patchClassName: "JobGiver_PrisonerEscape_RopedBlock_Patch", targetTypeName: "RimWorld.JobGiver_PrisonerEscape", targetMethodName: "TryGiveJob", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Roped prisoners can receive the base escape job."),
            Info(moduleName: "Roping", patchClassName: "Harmony_Patch_RopingDraw", targetTypeName: "Verse.Pawn_RopeTracker", targetMethodName: "RopingDraw", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Wall-hitch rope drawing falls back to the base rope draw path."),
            Info(moduleName: "Roping", patchClassName: "Patch_RopingTick", targetTypeName: "Verse.Pawn_RopeTracker", targetMethodName: "RopingTick", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.Critical, failureBehavior: "Custom Mugirl-only roping tick falls back to vanilla roping behavior."),
            Info(moduleName: "Roping", patchClassName: "Patch_RopeTracker_RopePawn", targetTypeName: "Verse.Pawn_RopeTracker", targetMethodName: "RopePawn", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "RopingService does not learn about newly roped pawns immediately."),
            Info(moduleName: "Roping", patchClassName: "Patch_RopeTracker_RopeToSpot", targetTypeName: "Verse.Pawn_RopeTracker", targetMethodName: "RopeToSpot", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "RopingService does not learn about newly spot-roped pawns immediately."),
            Info(moduleName: "Roping", patchClassName: "Patch_RopeTracker_BreakAllRopes", targetTypeName: "Verse.Pawn_RopeTracker", targetMethodName: "BreakAllRopes", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "RopingService cleanup after BreakAllRopes is delayed."),
            Info(moduleName: "Roping", patchClassName: "Patch_RopeTracker_DropRope", targetTypeName: "Verse.Pawn_RopeTracker", targetMethodName: "DropRope", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "RopingService cleanup after DropRope is delayed."),
            Info(moduleName: "Roping", patchClassName: "Patch_RopeTracker_UnropeFromSpot", targetTypeName: "Verse.Pawn_RopeTracker", targetMethodName: "UnropeFromSpot", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "RopingService cleanup after UnropeFromSpot is delayed."),
            Info(moduleName: "Roping", patchClassName: "Patch_RestraintsUtility_InRestraints", targetTypeName: "RimWorld.RestraintsUtility", targetMethodName: "InRestraints", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Following ropers is treated as base restraint state."),
            Info(moduleName: "Roping", patchClassName: "RestraintsUtility_ShouldShowRestraintsInfo_Patch", targetTypeName: "RimWorld.RestraintsUtility", targetMethodName: "ShouldShowRestraintsInfo", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Base restraint info may show while the custom roping state is active."),
            Info(moduleName: "Roping", patchClassName: "TakeToPreferredBedJob_Patch", targetTypeName: "RimWorld.WorkGiver_Warden_TakeToBed", targetMethodName: "TakeToPreferredBedJob", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Wardens may create base take-to-bed jobs for roped prisoners.")
        };

        // StaticCacheLifecycle: 进程级派生 patch 元数据；仅在类型初始化时由 patchInfos 构建。
        private static readonly List<MugirlPatchInfo> highRiskPatchInfos = BuildHighRiskPatchInfos();

        internal static IReadOnlyList<MugirlPatchInfo> PatchInfos => patchInfos;
        internal static IReadOnlyList<MugirlPatchInfo> HighRiskPatchInfos => highRiskPatchInfos;

        private static MugirlPatchInfo Info(
            string moduleName,
            string patchClassName,
            string targetTypeName,
            string targetMethodName,
            string patchKind,
            bool maySkipOriginal,
            MugirlPatchRiskLevel compatibilityRisk,
            string failureBehavior,
            string registryNameKey = null)
        {
            return new MugirlPatchInfo(
                moduleName: moduleName,
                patchClassName: patchClassName,
                targetTypeName: targetTypeName,
                targetMethodName: targetMethodName,
                patchKind: patchKind,
                maySkipOriginal: maySkipOriginal,
                compatibilityRisk: compatibilityRisk,
                failureBehavior: failureBehavior,
                registryNameKey: registryNameKey);
        }

        private static List<MugirlPatchInfo> BuildHighRiskPatchInfos()
        {
            List<MugirlPatchInfo> result = new List<MugirlPatchInfo>();
            for (int i = 0; i < patchInfos.Count; i++)
            {
                MugirlPatchInfo info = patchInfos[i];
                if (info.CompatibilityRisk >= MugirlPatchRiskLevel.High)
                {
                    result.Add(info);
                }
            }

            return result;
        }

        internal static void LogDevSummary(IReadOnlyList<string> patchedClassNames, IReadOnlyList<string> manualPatchNames)
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            int activePatchInfoCount = 0;
            int activeHighRiskCount = 0;
            for (int i = 0; i < patchInfos.Count; i++)
            {
                MugirlPatchInfo info = patchInfos[i];
                if (!IsPatchActive(info, patchedClassNames, manualPatchNames))
                {
                    continue;
                }

                activePatchInfoCount++;
                if (info.CompatibilityRisk >= MugirlPatchRiskLevel.High)
                {
                    activeHighRiskCount++;
                }
            }

            MugirlLog.DevMessage(
                "Patch audit: registered=" + patchInfos.Count +
                ", highRisk=" + highRiskPatchInfos.Count +
                ", activeRegistered=" + activePatchInfoCount +
                ", activeHighRisk=" + activeHighRiskCount + ".");
        }

        private static bool IsPatchActive(MugirlPatchInfo info, IReadOnlyList<string> patchedClassNames, IReadOnlyList<string> manualPatchNames)
        {
            if (info == null)
            {
                return false;
            }

            IReadOnlyList<string> source = info.IsManualPatch ? manualPatchNames : patchedClassNames;
            string key = info.IsManualPatch ? info.RegistryNameKey : info.PatchClassFullName;
            if (source == null || string.IsNullOrEmpty(key))
            {
                return false;
            }

            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] == key)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
