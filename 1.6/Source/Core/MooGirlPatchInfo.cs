using System.Collections.Generic;
using Verse;

namespace MooGirl
{
    internal enum MooGirlPatchRiskLevel
    {
        Low,
        Medium,
        High,
        Critical
    }

    internal sealed class MooGirlPatchInfo
    {
        internal MooGirlPatchInfo(
            string moduleName,
            string patchClassName,
            string targetTypeName,
            string targetMethodName,
            string patchKind,
            bool maySkipOriginal,
            MooGirlPatchRiskLevel compatibilityRisk,
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
        internal string PatchClassFullName => typeof(MooGirlPatchInfo).Namespace + "." + PatchClassName;
        internal string TargetTypeName { get; }
        internal string TargetMethodName { get; }
        internal string PatchKind { get; }
        internal bool MaySkipOriginal { get; }
        internal MooGirlPatchRiskLevel CompatibilityRisk { get; }
        internal string FailureBehavior { get; }
        internal string RegistryNameKey { get; }
        internal bool IsManualPatch => !string.IsNullOrEmpty(RegistryNameKey);
    }

    internal static class MooGirlPatchCatalog
    {
        // StaticCacheLifecycle: 进程级不可变 patch 元数据；不持有游戏对象或 Def 实例。
        private static readonly List<MooGirlPatchInfo> patchInfos = new List<MooGirlPatchInfo>
        {
            Info(moduleName: "Apparel", patchClassName: "Harmony_Apparel_DrawColor", targetTypeName: "RimWorld.Apparel", targetMethodName: "DrawColor", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "The spacesuit uses the base apparel draw color path."),

            Info(moduleName: "Genes", patchClassName: "Harmony_PawnUtility_TrySpawnHatchedOrBornPawn_MooGirlXenotype", targetTypeName: "RimWorld.PawnUtility", targetMethodName: "TrySpawnHatchedOrBornPawn", patchKind: "Prefix/Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Birth-time xenotype correction is skipped; the low-frequency game component remains as fallback."),

            Info(moduleName: "Incidents", patchClassName: "Harmony_GiantCorpRaidTiers", targetTypeName: "RimWorld.PawnGroupMaker", targetMethodName: "CanGenerateFrom", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "Giant corporation raid tier filtering falls back to the base pawn-group result."),
            Info(moduleName: "Incidents", patchClassName: "IsWildMan_WildManUtility_Patch", targetTypeName: "RimWorld.WildManUtility", targetMethodName: "IsWildMan", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "Escape wild slaves are no longer exposed through the vanilla wild-man helper."),
            Info(moduleName: "Incidents", patchClassName: "RecruitUtility_Recruit_Patch", targetTypeName: "RimWorld.RecruitUtility", targetMethodName: "Recruit", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "Escape wild-slave cleanup after recruitment is skipped."),
            Info(moduleName: "Incidents", patchClassName: "TameUtility_CanTame_Patch", targetTypeName: "RimWorld.TameUtility", targetMethodName: "CanTame", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "Escape wild slaves are no longer accepted by the tame entry point."),
            Info(moduleName: "Incidents", patchClassName: "DesignatorTame_CanDesignateThing_Patch", targetTypeName: "RimWorld.Designator_Tame", targetMethodName: "CanDesignateThing", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "Escape wild slaves lose the tame designator UI entry."),
            Info(moduleName: "Incidents", patchClassName: "Pawn_SetFaction_MooGirlWildSlaveCleanup_Patch", targetTypeName: "Verse.Pawn", targetMethodName: "SetFaction", patchKind: "Prefix/Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Escape wild-slave normalization after joining the player is skipped."),
            Info(moduleName: "Incidents", patchClassName: "PawnGenerator_GeneratePawn_MooGirlWildSlaveBirth_Patch", targetTypeName: "Verse.PawnGenerator", targetMethodName: "GeneratePawn(PawnGenerationRequest)", patchKind: "Prefix/Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Player-faction newborn escape wild slaves keep the base generated kind."),

            Info(moduleName: "Milk", patchClassName: "Harmony_ChildcareUtility_CanBreastfeed_MooGirl", targetTypeName: "RimWorld.ChildcareUtility", targetMethodName: "CanBreastfeed", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "MooGirl milk sources no longer satisfy the base breastfeeding availability check."),
            Info(moduleName: "Milk", patchClassName: "Harmony_ChildcareUtility_CanBreastfeedNow_MooGirl", targetTypeName: "RimWorld.ChildcareUtility", targetMethodName: "CanBreastfeedNow", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "MooGirl milk sources no longer satisfy the immediate breastfeeding check."),
            Info(moduleName: "Milk", patchClassName: "Harmony_ChildcareUtility_SuckleFromLactatingPawn_MooGirl", targetTypeName: "RimWorld.ChildcareUtility", targetMethodName: "SuckleFromLactatingPawn", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Babies use the base lactation path and will not consume MooGirl milk fullness."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MooGirlNurtureGrowthPoints", targetTypeName: "RimWorld.Pawn_AgeTracker", targetMethodName: "GrowthPointsPerDay", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Nurture hediffs no longer alter child growth-point gain."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MooGirlNurturedSkillLearnCap", targetTypeName: "RimWorld.SkillRecord", targetMethodName: "Learn", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "The nurtured trait no longer relaxes the daily full-rate XP cap."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MooGirlMilkingAnimation_DisableCachedPawnRender", targetTypeName: "Verse.PawnRenderer", targetMethodName: "ParallelGetPreRenderResults", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Active milking animation may be rendered through cached pawn results."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MooGirlMilkingAnimation_PawnMatrix", targetTypeName: "Verse.PawnRenderer", targetMethodName: "GetDrawParms", patchKind: "Prefix/Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Milking animation body facing and draw matrix adjustments are skipped."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MooGirlMilkingAnimation_NodeTransform", targetTypeName: "Verse.PawnRenderNode", targetMethodName: "GetTransform", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Milking animation render-node offsets are skipped."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MooGirlMilkingAnimation_PawnDeSpawn", targetTypeName: "Verse.Pawn", targetMethodName: "DeSpawn", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "Milking animation state cleanup waits for other cleanup paths."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MooGirlMilkingAnimation_PawnDestroy", targetTypeName: "Verse.Pawn", targetMethodName: "Destroy", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "Milking animation state cleanup waits for other cleanup paths."),

            Info(moduleName: "Misc", patchClassName: "Patch_DrugAdministerDefs", targetTypeName: "RimWorld.RecipeDefGenerator", targetMethodName: "DrugAdministerDefs", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "The generated administer-milk recipe is not added."),
            Info(moduleName: "Misc", patchClassName: "Harmony_GhoulRenderingRefresh_PostAdd", targetTypeName: "Verse.Hediff", targetMethodName: "PostAdd", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "Ghoul graphics refresh relies on later renderer refreshes."),
            Info(moduleName: "Misc", patchClassName: "Harmony_MooGirlGrazeChain", targetTypeName: "Verse.AI.JobDriver", targetMethodName: "Cleanup", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "MooGirl grazing jobs no longer chain to the next nearby plant."),
            Info(moduleName: "Misc", patchClassName: "Harmony_MooGirlGrazeIngest", targetTypeName: "RimWorld.JobDriver_Ingest", targetMethodName: "PrepareToIngestToils", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "MooGirls use the base tool-user ingest preparation for map plants, which may pick plants up before eating."),
            Info(moduleName: "Misc", patchClassName: "Harmony_MooGirlGrazeFloatMenu", targetTypeName: "RimWorld.FloatMenuOptionProvider_Ingest", targetMethodName: "GetSingleOptionFor(Thing, FloatMenuContext)", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Manual MooGirl plant-ingest orders use the base menu action, which tries to unforbid non-forbiddable plants."),

            Info(moduleName: "Mounting", patchClassName: "Harmony_MountRendering", targetTypeName: "Verse.Pawn", targetMethodName: "DynamicDrawPhaseAt", patchKind: "Prefix/Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Mounted rider and weapon rendering is skipped."),
            Info(moduleName: "Mounting", patchClassName: "Harmony_MountedPawnGizmos", targetTypeName: "Verse.Pawn", targetMethodName: "GetGizmos", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "Mounted-pawn command gizmos fall back to the base pawn list."),
            Info(moduleName: "Mounting", patchClassName: "Harmony_MountedCombatController_WarmupTime", targetTypeName: "Verse.Verb", targetMethodName: "WarmupTime", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Mounted combat warmup override is skipped."),
            Info(moduleName: "Mounting", patchClassName: "Harmony_MountedCombatController_StanceWarmupDraw", targetTypeName: "Verse.Stance_Warmup", targetMethodName: "StanceDraw", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Mounted verbs use the base warmup stance draw path."),
            Info(moduleName: "Mounting", patchClassName: "Harmony_MountedCombatController_EquipmentRemoved", targetTypeName: "RimWorld.Pawn_EquipmentTracker", targetMethodName: "Notify_EquipmentRemoved", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Mounted combat verb cleanup after equipment removal is skipped."),

            Info(moduleName: "Newborn", patchClassName: "Harmony_PawnGenerator_NewbornVisuals", targetTypeName: "Verse.PawnGenerator", targetMethodName: "GeneratePawn(PawnGenerationRequest)", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Generated MooGirl newborns keep the base body visual state."),
            Info(moduleName: "Newborn", patchClassName: "Harmony_PawnRenderNode_Hair_MooGirlBaby", targetTypeName: "Verse.PawnRenderNode_Hair", targetMethodName: "GraphicFor", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "MooGirl babies keep the base baby hair rendering suppression."),
            Info(moduleName: "Newborn", patchClassName: "Harmony_PawnAgeTracker_RecalculateLifeStageIndex_MooGirlBodyType", targetTypeName: "RimWorld.Pawn_AgeTracker", targetMethodName: "RecalculateLifeStageIndex", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Life-stage body normalization waits for later explicit refreshes."),

            Info(moduleName: "Restraints", patchClassName: "PawnGenerator_GeneratePawn_Patch", targetTypeName: "Verse.PawnGenerator", targetMethodName: "GeneratePawn(PawnGenerationRequest)", patchKind: "Manual Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Generated slave apparel is not locked by the post-generation fallback.", registryNameKey: "MooGirl.PatchRegistry.PawnGeneratorGeneratePawn"),
            Info(moduleName: "Restraints", patchClassName: "ITab_Pawn_Gear_DrawThingRow_Transpiler", targetTypeName: "RimWorld.ITab_Pawn_Gear", targetMethodName: "DrawThingRow", patchKind: "Transpiler", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Critical, failureBehavior: "Advanced slave-apparel drop tooltip injection is skipped and a once-warning is emitted."),
            Info(moduleName: "Restraints", patchClassName: "Patch_SlaveRebellionUtility_InitiateSlaveRebellionMtbDays", targetTypeName: "RimWorld.SlaveRebellionUtility", targetMethodName: "InitiateSlaveRebellionMtbDays", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Brainwash-obey pawns use the base rebellion MTB calculation."),
            Info(moduleName: "Roping", patchClassName: "Building_Door_PawnCanOpen_Patch", targetTypeName: "RimWorld.Building_Door", targetMethodName: "PawnCanOpen", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "Roped followers use the base door-opening permission."),
            Info(moduleName: "Roping", patchClassName: "Patch_PawnDraftController_GetGizmos", targetTypeName: "RimWorld.Pawn_DraftController", targetMethodName: "GetGizmos", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "Draft gizmo disabling while roped is skipped."),
            Info(moduleName: "Roping", patchClassName: "JobGiver_PrisonerEscape_RopedBlock_Patch", targetTypeName: "RimWorld.JobGiver_PrisonerEscape", targetMethodName: "TryGiveJob", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Roped prisoners can receive the base escape job."),
            Info(moduleName: "Roping", patchClassName: "Harmony_Patch_RopingDraw", targetTypeName: "Verse.Pawn_RopeTracker", targetMethodName: "RopingDraw", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Wall-hitch rope drawing falls back to the base rope draw path."),
            Info(moduleName: "Roping", patchClassName: "Patch_RopingTick", targetTypeName: "Verse.Pawn_RopeTracker", targetMethodName: "RopingTick", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MooGirlPatchRiskLevel.Critical, failureBehavior: "Custom MooGirl-only roping tick falls back to vanilla roping behavior."),
            Info(moduleName: "Roping", patchClassName: "Patch_RopeTracker_RopePawn", targetTypeName: "Verse.Pawn_RopeTracker", targetMethodName: "RopePawn", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "RopingService does not learn about newly roped pawns immediately."),
            Info(moduleName: "Roping", patchClassName: "Patch_RopeTracker_RopeToSpot", targetTypeName: "Verse.Pawn_RopeTracker", targetMethodName: "RopeToSpot", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "RopingService does not learn about newly spot-roped pawns immediately."),
            Info(moduleName: "Roping", patchClassName: "Patch_RopeTracker_BreakAllRopes", targetTypeName: "Verse.Pawn_RopeTracker", targetMethodName: "BreakAllRopes", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "RopingService cleanup after BreakAllRopes is delayed."),
            Info(moduleName: "Roping", patchClassName: "Patch_RopeTracker_DropRope", targetTypeName: "Verse.Pawn_RopeTracker", targetMethodName: "DropRope", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "RopingService cleanup after DropRope is delayed."),
            Info(moduleName: "Roping", patchClassName: "Patch_RopeTracker_UnropeFromSpot", targetTypeName: "Verse.Pawn_RopeTracker", targetMethodName: "UnropeFromSpot", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "RopingService cleanup after UnropeFromSpot is delayed."),
            Info(moduleName: "Roping", patchClassName: "Patch_RestraintsUtility_InRestraints", targetTypeName: "RimWorld.RestraintsUtility", targetMethodName: "InRestraints", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Following ropers is treated as base restraint state."),
            Info(moduleName: "Roping", patchClassName: "RestraintsUtility_ShouldShowRestraintsInfo_Patch", targetTypeName: "RimWorld.RestraintsUtility", targetMethodName: "ShouldShowRestraintsInfo", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MooGirlPatchRiskLevel.High, failureBehavior: "Base restraint info may show while the custom roping state is active."),
            Info(moduleName: "Roping", patchClassName: "TakeToPreferredBedJob_Patch", targetTypeName: "RimWorld.WorkGiver_Warden_TakeToBed", targetMethodName: "TakeToPreferredBedJob", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MooGirlPatchRiskLevel.Medium, failureBehavior: "Wardens may create base take-to-bed jobs for roped prisoners.")
        };

        // StaticCacheLifecycle: 进程级派生 patch 元数据；仅在类型初始化时由 patchInfos 构建。
        private static readonly List<MooGirlPatchInfo> highRiskPatchInfos = BuildHighRiskPatchInfos();

        internal static IReadOnlyList<MooGirlPatchInfo> PatchInfos => patchInfos;
        internal static IReadOnlyList<MooGirlPatchInfo> HighRiskPatchInfos => highRiskPatchInfos;

        private static MooGirlPatchInfo Info(
            string moduleName,
            string patchClassName,
            string targetTypeName,
            string targetMethodName,
            string patchKind,
            bool maySkipOriginal,
            MooGirlPatchRiskLevel compatibilityRisk,
            string failureBehavior,
            string registryNameKey = null)
        {
            return new MooGirlPatchInfo(
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

        private static List<MooGirlPatchInfo> BuildHighRiskPatchInfos()
        {
            List<MooGirlPatchInfo> result = new List<MooGirlPatchInfo>();
            for (int i = 0; i < patchInfos.Count; i++)
            {
                MooGirlPatchInfo info = patchInfos[i];
                if (info.CompatibilityRisk >= MooGirlPatchRiskLevel.High)
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
                MooGirlPatchInfo info = patchInfos[i];
                if (!IsPatchActive(info, patchedClassNames, manualPatchNames))
                {
                    continue;
                }

                activePatchInfoCount++;
                if (info.CompatibilityRisk >= MooGirlPatchRiskLevel.High)
                {
                    activeHighRiskCount++;
                }
            }

            MooGirlLog.DevMessage(
                "Patch audit: registered=" + patchInfos.Count +
                ", highRisk=" + highRiskPatchInfos.Count +
                ", activeRegistered=" + activePatchInfoCount +
                ", activeHighRisk=" + activeHighRiskCount + ".");
        }

        private static bool IsPatchActive(MooGirlPatchInfo info, IReadOnlyList<string> patchedClassNames, IReadOnlyList<string> manualPatchNames)
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
