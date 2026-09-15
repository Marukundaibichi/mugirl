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
            Info(moduleName: "Corporate", patchClassName: "Harmony_CorporateCommsConsole", targetTypeName: "RimWorld.Building_CommsConsole", targetMethodName: "GetFloatMenuOptions", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Low, failureBehavior: "Corporate console access is unavailable; vanilla communications remain intact."),
            Info(moduleName: "Corporate", patchClassName: "Harmony_CorporateSettlementGizmos", targetTypeName: "RimWorld.Planet.Settlement", targetMethodName: "GetCaravanGizmos", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Low, failureBehavior: "Corporate settlement access is unavailable; console access remains available."),
            Info(moduleName: "Corporate", patchClassName: "Harmony_CorporateIntroductionDamage", targetTypeName: "Verse.Pawn", targetMethodName: "PreApplyDamage", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Introduction dialogue uses incomplete courier and corporate aggression evidence."),
            Info(moduleName: "Corporate", patchClassName: "Harmony_CorporateIntroductionKilled", targetTypeName: "Verse.Pawn", targetMethodName: "Kill", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Low, failureBehavior: "Courier death details fall back to neutral historical dialogue."),
            Info(moduleName: "Corporate", patchClassName: "CorporateDebt_RelationKind_Patch", targetTypeName: "RimWorld.Faction", targetMethodName: "RelationKindWith", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Debt hostility may not appear in diplomacy UI."),
            Info(moduleName: "Corporate", patchClassName: "CorporateDebt_HostileTo_Patch", targetTypeName: "RimWorld.FactionUtility", targetMethodName: "HostileTo", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Debt collection cannot rely on enforced combat hostility."),
            Info(moduleName: "Corporate", patchClassName: "CorporateDebt_Goodwill_Patch", targetTypeName: "RimWorld.Faction", targetMethodName: "CanChangeGoodwillFor", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Positive goodwill changes may be accepted during default; debt balance is preserved."),
            Info(moduleName: "Corporate", patchClassName: "Harmony_CorporateDistributionSale", targetTypeName: "RimWorld.Tradeable", targetMethodName: "ResolveTrade", patchKind: "Prefix/Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Distribution sales progress is not updated; vanilla trade remains intact."),
            Info(moduleName: "Corporate", patchClassName: "Harmony_CorporateResearchAttacked", targetTypeName: "Verse.Thing", targetMethodName: "TakeDamage", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Attacking a research target before dialogue may not select the execution branch."),
            Info(moduleName: "Corporate", patchClassName: "Harmony_CorporateResearchGizmos", targetTypeName: "RimWorld.Planet.Site", targetMethodName: "GetGizmos", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Low, failureBehavior: "Research site dialogue remains accessible through the corporate terminal."),
            Info(moduleName: "Appearance", patchClassName: "Harmony_StylingStationRefresh", targetTypeName: "AlienRace.StylingStation", targetMethodName: "DoRaceTabs", patchKind: "Prefix/Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Low, failureBehavior: "HAR styling selections retain their original behavior and may remain visually stale until another graphics refresh."),
            Info(moduleName: "Appearance", patchClassName: "Harmony_ExtraOutline_Body", targetTypeName: "Verse.PawnRenderTree", targetMethodName: "Draw", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Optional Mugirl body/apparel and back-weapon outlines are absent; original drawing is preserved."),
            Info(moduleName: "Appearance", patchClassName: "Harmony_ExtraOutline_Weapon", targetTypeName: "Verse.PawnRenderUtility", targetMethodName: "DrawEquipmentAiming", patchKind: "Transpiler", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "An unmatched DrawMesh anchor leaves the original instructions intact and disables held-weapon outlines."),
            Info(moduleName: "Apparel", patchClassName: "Harmony_Apparel_DrawColor", targetTypeName: "RimWorld.Apparel", targetMethodName: "DrawColor", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Fixed-color apparel uses the base apparel draw color path."),

            Info(moduleName: "Genes", patchClassName: "Harmony_PawnUtility_TrySpawnHatchedOrBornPawn_MugirlXenotype", targetTypeName: "RimWorld.PawnUtility", targetMethodName: "TrySpawnHatchedOrBornPawn", patchKind: "Prefix/Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Birth-time xenotype correction is skipped; the low-frequency game component remains as fallback."),

            Info(moduleName: "Incidents", patchClassName: "Harmony_GiantCorpRaidTiers", targetTypeName: "RimWorld.PawnGroupMaker", targetMethodName: "CanGenerateFrom", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Giant corporation raid tier filtering falls back to the base pawn-group result."),
            Info(moduleName: "Incidents", patchClassName: "IsWildMan_WildManUtility_Patch", targetTypeName: "RimWorld.WildManUtility", targetMethodName: "IsWildMan", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Escape wild slaves are no longer exposed through the vanilla wild-man helper."),
            Info(moduleName: "Incidents", patchClassName: "RecruitUtility_Recruit_Patch", targetTypeName: "RimWorld.RecruitUtility", targetMethodName: "Recruit", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Escape wild-slave cleanup after recruitment is skipped."),
            Info(moduleName: "Incidents", patchClassName: "TameUtility_CanTame_Patch", targetTypeName: "RimWorld.TameUtility", targetMethodName: "CanTame", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Escape wild slaves are no longer accepted by the tame entry point."),
            Info(moduleName: "Incidents", patchClassName: "DesignatorTame_CanDesignateThing_Patch", targetTypeName: "RimWorld.Designator_Tame", targetMethodName: "CanDesignateThing", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Escape wild slaves lose the tame designator UI entry."),
            Info(moduleName: "Incidents", patchClassName: "Pawn_SetFaction_MugirlWildSlaveCleanup_Patch", targetTypeName: "Verse.Pawn", targetMethodName: "SetFaction", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Transient wild-slave event state cleanup after joining the player is skipped."),
            Info(moduleName: "Incidents", patchClassName: "JobGiver_DropRandomGearOrApparel_MugirlMigration_Patch", targetTypeName: "RimWorld.JobGiver_DropRandomGearOrApparel", targetMethodName: "TryGiveJob", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Migration Mugirls can receive the vanilla wild-man job that randomly drops their bikini on the ground."),
            Info(moduleName: "Incidents", patchClassName: "PawnMindState_CheckStartMentalStateBecauseRecruitAttempted_MugirlEvents_Patch", targetTypeName: "Verse.AI.Pawn_MindState", targetMethodName: "CheckStartMentalStateBecauseRecruitAttempted", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Passive event pawns may start the base recruit-attempt mental state."),
            Info(moduleName: "Incidents", patchClassName: "PawnMindState_NotifyDamageTaken_MugirlEvents_Patch", targetTypeName: "Verse.AI.Pawn_MindState", targetMethodName: "Notify_DamageTaken", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Passive event pawns use the base damage reaction path."),
            Info(moduleName: "Incidents", patchClassName: "Pawn_Kill_MugirlMigrationOutcome_Patch", targetTypeName: "Verse.Pawn", targetMethodName: "Kill", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Low, failureBehavior: "Punisher eligibility no longer verifies that the player caused each migration death."),
            Info(moduleName: "Incidents", patchClassName: "Pawn_ShouldShowQuestionMark_MugirlFusionInvestor_Patch", targetTypeName: "Verse.Pawn", targetMethodName: "ShouldShowQuestionMark", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Fusion investors may not show the question-mark interaction indicator."),

            Info(moduleName: "Milk", patchClassName: "Harmony_ChildcareUtility_CanBreastfeed_Mugirl", targetTypeName: "RimWorld.ChildcareUtility", targetMethodName: "CanBreastfeed", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Mugirl milk sources no longer satisfy the base breastfeeding availability check."),
            Info(moduleName: "Milk", patchClassName: "Harmony_ChildcareUtility_CanBreastfeedNow_Mugirl", targetTypeName: "RimWorld.ChildcareUtility", targetMethodName: "CanBreastfeedNow", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Mugirl milk sources no longer satisfy the immediate breastfeeding check."),
            Info(moduleName: "Milk", patchClassName: "Harmony_ChildcareUtility_SuckleFromLactatingPawn_Mugirl", targetTypeName: "RimWorld.ChildcareUtility", targetMethodName: "SuckleFromLactatingPawn", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Babies use the base lactation path and will not consume Mugirl milk fullness."),
            Info(moduleName: "Compatibility", patchClassName: "Harmony_LactationExpansion_CanBreastfeedNow_Mugirl", targetTypeName: "RimWorld.ChildcareUtility", targetMethodName: "CanBreastfeedNow", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Lactation Expansion may evaluate Mugirl milk sources through the vanilla Lactating hediff path."),
            Info(moduleName: "Compatibility", patchClassName: "Harmony_LactationExpansion_SuckleFromLactatingPawn_Mugirl", targetTypeName: "RimWorld.ChildcareUtility", targetMethodName: "SuckleFromLactatingPawn", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Lactation Expansion may consume Mugirl milk sources through the vanilla Lactating hediff path."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MugirlNurtureGrowthPoints", targetTypeName: "RimWorld.Pawn_AgeTracker", targetMethodName: "GrowthPointsPerDay", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Nurture hediffs no longer alter child growth-point gain."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MugirlNurturedSkillLearnCap", targetTypeName: "RimWorld.SkillRecord", targetMethodName: "Learn", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "The nurtured trait no longer relaxes the daily full-rate XP cap."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MugirlMilkingAnimation_PawnDeSpawn", targetTypeName: "Verse.Pawn", targetMethodName: "DeSpawn", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Milking animation state cleanup waits for other cleanup paths."),
            Info(moduleName: "Milk", patchClassName: "Harmony_MugirlMilkingAnimation_PawnDestroy", targetTypeName: "Verse.Pawn", targetMethodName: "Destroy", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Milking animation state cleanup waits for other cleanup paths."),

            Info(moduleName: "Misc", patchClassName: "Patch_DrugAdministerDefs", targetTypeName: "RimWorld.RecipeDefGenerator", targetMethodName: "DrugAdministerDefs", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "The generated administer-milk recipe is not added."),
            Info(moduleName: "Misc", patchClassName: "Harmony_GhoulRenderingRefresh_PostAdd", targetTypeName: "Verse.Hediff", targetMethodName: "PostAdd", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Ghoul graphics refresh relies on later renderer refreshes."),
            Info(moduleName: "Misc", patchClassName: "Harmony_MugirlGrazeChain", targetTypeName: "Verse.AI.JobDriver", targetMethodName: "Cleanup", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Mugirl grazing jobs no longer chain to the next nearby plant."),
            Info(moduleName: "Misc", patchClassName: "Harmony_MugirlGrazeIngest", targetTypeName: "RimWorld.JobDriver_Ingest", targetMethodName: "PrepareToIngestToils", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Mugirls use the base tool-user ingest preparation for map plants, which may pick plants up before eating."),
            Info(moduleName: "Misc", patchClassName: "Harmony_MugirlGrazeFloatMenu", targetTypeName: "RimWorld.FloatMenuOptionProvider_Ingest", targetMethodName: "GetSingleOptionFor(Thing, FloatMenuContext)", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Manual Mugirl plant-ingest orders use the base menu action, which tries to unforbid non-forbiddable plants."),

            Info(moduleName: "Mounting", patchClassName: "Harmony_MountRendering", targetTypeName: "Verse.Pawn", targetMethodName: "DynamicDrawPhaseAt", patchKind: "Prefix/Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Mounted rider and weapon rendering is skipped."),
            Info(moduleName: "Mounting", patchClassName: "Harmony_MountedPawnGizmos", targetTypeName: "Verse.Pawn", targetMethodName: "GetGizmos", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Mounted-pawn command gizmos fall back to the base pawn list."),
            Info(moduleName: "Mounting", patchClassName: "Harmony_MountedCombatController_WarmupTime", targetTypeName: "Verse.Verb", targetMethodName: "WarmupTime", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Mounted combat warmup override is skipped."),
            Info(moduleName: "Mounting", patchClassName: "Harmony_MountedCombatController_StanceWarmupDraw", targetTypeName: "Verse.Stance_Warmup", targetMethodName: "StanceDraw", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Mounted verbs use the base warmup stance draw path."),
            Info(moduleName: "Mounting", patchClassName: "Harmony_MountedCombatController_EquipmentRemoved", targetTypeName: "RimWorld.Pawn_EquipmentTracker", targetMethodName: "Notify_EquipmentRemoved", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Mounted combat verb cleanup after equipment removal is skipped."),

            Info(moduleName: "WeaponWheel", patchClassName: "Harmony_WeaponWheel_EnemyLoadout", targetTypeName: "Verse.PawnGenerator", targetMethodName: "GeneratePawn(PawnGenerationRequest)", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Hostile generated Mugirls keep only their ordinary primary weapon and cannot start a full-firepower cycle."),
            Info(moduleName: "WeaponWheel", patchClassName: "Harmony_WeaponWheel_EquipmentAdded", targetTypeName: "RimWorld.Pawn_EquipmentTracker", targetMethodName: "Notify_EquipmentAdded", patchKind: "Prefix/Postfix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.Critical, failureBehavior: "Wheel weapon transfers use ordinary equipment callbacks and may reset weapon-specific state."),
            Info(moduleName: "WeaponWheel", patchClassName: "Harmony_WeaponWheel_EquipmentRemoved", targetTypeName: "RimWorld.Pawn_EquipmentTracker", targetMethodName: "Notify_EquipmentRemoved", patchKind: "Prefix/Postfix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.Critical, failureBehavior: "Wheel weapon transfers use ordinary unequip callbacks and may reset persona or unique-weapon state."),
            Info(moduleName: "WeaponWheel", patchClassName: "Harmony_WeaponWheel_BurstCompleted", targetTypeName: "Verse.Verb", targetMethodName: "TryCastNextBurstShot", patchKind: "Prefix/Postfix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.Critical, failureBehavior: "Full-firepower chains do not advance after a burst, or linked sword-dance damage resolves before the pawn visually crosses its target."),
            Info(moduleName: "WeaponWheel", patchClassName: "Harmony_WeaponWheel_MeleeAttack", targetTypeName: "RimWorld.Verb_MeleeAttack", targetMethodName: "TryCastShot", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Critical, failureBehavior: "Linked sword dance does not confirm the delayed melee strike after its original damage routine resolves."),
            Info(moduleName: "WeaponWheel", patchClassName: "Harmony_WeaponWheel_TryStartCastOn", targetTypeName: "Verse.Verb", targetMethodName: "TryStartCastOn(LocalTargetInfo, LocalTargetInfo, bool, bool, bool, bool)", patchKind: "Prefix/Postfix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.Critical, failureBehavior: "Weapon-wheel equip animation and cycle cooldown no longer gate firing."),
            Info(moduleName: "WeaponWheel", patchClassName: "Harmony_WeaponWheel_WarmupTime", targetTypeName: "Verse.Verb", targetMethodName: "WarmupTime", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Chained wheel weapons retain their ordinary aiming delay."),
            Info(moduleName: "WeaponWheel", patchClassName: "Harmony_WeaponWheel_DrawEquipmentAiming", targetTypeName: "Verse.PawnRenderUtility", targetMethodName: "DrawEquipmentAiming", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "The base aiming weapon may overlap the custom equip animation."),
            Info(moduleName: "WeaponWheelCompat", patchClassName: "Harmony_WeaponWheel_MeleeAnimationScope", targetTypeName: "Verse.Pawn_DrawTracker", targetMethodName: "Notify_MeleeAttackOn", patchKind: "Prefix/Postfix/Finalizer", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Melee Animation can overlap its idle strike animation with a linked sword-dance weapon switch."),
            Info(moduleName: "MeleeAnimationCompat", patchClassName: "Harmony_MeleeAnimation_WeaponWheelPrimaryChanged", targetTypeName: "Comp_WeaponWheel", targetMethodName: "MoveActiveWeaponTo / NormalizeToPrimarySlot", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Melee Animation can retain the previous primary weapon's cached animation until its next natural refresh."),
            Info(moduleName: "MeleeAnimationCompat", patchClassName: "Harmony_MeleeAnimation_MountedWeaponDraw", targetTypeName: "MountedPawnMeleeSupport", targetMethodName: "DrawWeapon", patchKind: "Prefix/Postfix/Finalizer", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Melee Animation can overlap its idle weapon animation with the mounted weapon draw."),
            Info(moduleName: "DualWieldCompat", patchClassName: "Harmony_DualWield_WeaponWheelFullFirepower", targetTypeName: "Comp_WeaponWheel", targetMethodName: "IsFullFirepowerEligible", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Full firepower can start while Dual Wield has an active off-hand weapon."),
            Info(moduleName: "DualWieldCompat", patchClassName: "Harmony_DualWield_WeaponWheelCastDisplay", targetTypeName: "Comp_WeaponWheel", targetMethodName: "HandleBeforeTryStartCast", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "The wheel's single-main-hand equip animation can overlap Dual Wield's main/off-hand rendering."),
            Info(moduleName: "DualWieldCompat", patchClassName: "Harmony_DualWield_WeaponWheelEquipmentNotifications", targetTypeName: "Comp_WeaponWheel", targetMethodName: "NotifyExternalEquipmentAdded / NotifyExternalEquipmentRemoved", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "An off-hand weapon can be mistaken for a new wheel primary and displace the displayed first slot."),
            Info(moduleName: "DualWieldCompat", patchClassName: "Harmony_DualWield_WeaponWheelPrimarySwap", targetTypeName: "Comp_WeaponWheel", targetMethodName: "MoveActiveWeaponTo / NormalizeToPrimarySlot", patchKind: "Prefix/Postfix/Finalizer", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.Critical, failureBehavior: "Removing the main hand temporarily promotes the off hand to Primary, preventing the requested wheel weapon from being equipped."),
            Info(moduleName: "RunAndGunCompat", patchClassName: "Harmony_RunAndGun_WeaponWheelFullFirepower", targetTypeName: "Comp_WeaponWheel", targetMethodName: "IsFullFirepowerEligible", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "A moving RunAndGun cast can start a wheel weapon chain and take over its stance."),
            Info(moduleName: "YayoAnimationCompat", patchClassName: "Harmony_YayoAnimation_WeaponWheelDrawPosition", targetTypeName: "WeaponWheelAnimationRenderer", targetMethodName: "Draw", patchKind: "Transpiler", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Weapon-wheel transition graphics do not follow Yayo Animation's adjusted pawn body position."),
            Info(moduleName: "YayoCombatCompat", patchClassName: "Harmony_YayoCombat_WeaponWheelTick", targetTypeName: "Comp_WeaponWheel", targetMethodName: "CompTick", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Yayo's Combat does not automatically reload empty reserve wheel weapons."),
            Info(moduleName: "YayoCombatCompat", patchClassName: "Harmony_YayoCombat_GeneratedWeaponAmmo", targetTypeName: "Comp_WeaponWheel", targetMethodName: "TryAddGeneratedReserveWeapon", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Generated hostile reserve weapons do not receive Yayo's Combat starting ammunition."),
            Info(moduleName: "WeaponWheelCompat", patchClassName: "Harmony_WeaponWheel_ReloadableWearer", targetTypeName: "RimWorld.CompApparelVerbOwner", targetMethodName: "Wearer", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Reloadable reserve weapons cannot resolve their wheel pawn as wearer."),
            Info(moduleName: "WeaponWheelCompat", patchClassName: "Harmony_WeaponWheel_ReloadableOwner", targetTypeName: "RimWorld.Utility.ReloadableUtility", targetMethodName: "OwnerOf", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Reload jobs reject reloadable reserve weapons because their owner cannot be resolved."),
            Info(moduleName: "WeaponWheelCompat", patchClassName: "Harmony_WeaponWheel_FindReloadable", targetTypeName: "RimWorld.Utility.ReloadableUtility", targetMethodName: "FindSomeReloadableComponent", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Yayo's Combat reload discovery omits empty reserve weapons."),
            Info(moduleName: "WeaponWheel", patchClassName: "Harmony_WeaponWheel_TryGetAttackVerb_Diagnostics", targetTypeName: "Verse.Pawn", targetMethodName: "TryGetAttackVerb", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "External combat mods can leave TryGetAttackVerb selecting a reserve wheel weapon that is not the active primary, aborting full-firepower cycling."),
            Info(moduleName: "WeaponWheel", patchClassName: "Harmony_WeaponWheel_AnimationDraw", targetTypeName: "Verse.Pawn", targetMethodName: "DynamicDrawPhaseAt", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Equip, switch and flash animations are not drawn."),
            Info(moduleName: "WeaponWheel", patchClassName: "Harmony_WeaponWheel_SwordDanceDashDrawPosition", targetTypeName: "Verse.Pawn", targetMethodName: "DrawPos.get", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Linked sword dance falls back to the default teleport tween instead of its pause, recoil, rush, overshoot and return animation."),
            Info(moduleName: "WeaponWheel", patchClassName: "Harmony_WeaponWheel_MaintainCombatFacing", targetTypeName: "Verse.Pawn_RotationTracker", targetMethodName: "UpdateRotation", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Active side-facing wheel fire may briefly fall back to the drafted pawn's south-facing idle rotation."),
            Info(moduleName: "WeaponWheel", patchClassName: "Harmony_WeaponWheel_GearMass", targetTypeName: "RimWorld.MassUtility", targetMethodName: "GearMass", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Reserve weapon mass is omitted from gear mass."),
            Info(moduleName: "WeaponWheel", patchClassName: "Harmony_WeaponWheel_GetChildHolders", targetTypeName: "Verse.Pawn", targetMethodName: "GetChildHolders", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Reserve weapons are omitted from recursive holder traversal."),
            Info(moduleName: "WeaponWheel", patchClassName: "Harmony_WeaponWheel_DropAndForbidEverything", targetTypeName: "Verse.Pawn", targetMethodName: "DropAndForbidEverything", patchKind: "Prefix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Reserve weapons are not dropped or destroyed with ordinary pawn gear."),
            Info(moduleName: "WeaponWheel", patchClassName: "Harmony_WeaponWheel_EquipmentPawnSpawned", targetTypeName: "RimWorld.Pawn_EquipmentTracker", targetMethodName: "Notify_PawnSpawned", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Downed pawns spawned onto a map keep their reserve wheel weapons."),

            Info(moduleName: "Newborn", patchClassName: "Harmony_PawnGenerator_NewbornVisuals", targetTypeName: "Verse.PawnGenerator", targetMethodName: "GeneratePawn(PawnGenerationRequest)", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Generated Mugirl newborns keep the base body visual state."),
            Info(moduleName: "Newborn", patchClassName: "Harmony_PawnRenderNode_Hair_MugirlBaby", targetTypeName: "Verse.PawnRenderNode_Hair", targetMethodName: "GraphicFor", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "Mugirl babies keep the base baby hair rendering suppression."),
            Info(moduleName: "Newborn", patchClassName: "Harmony_PawnAgeTracker_RecalculateLifeStageIndex_MugirlBodyType", targetTypeName: "RimWorld.Pawn_AgeTracker", targetMethodName: "RecalculateLifeStageIndex", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Life-stage body normalization waits for later explicit refreshes."),

            Info(moduleName: "Restraints", patchClassName: "PawnGenerator_GeneratePawn_Patch", targetTypeName: "Verse.PawnGenerator", targetMethodName: "GeneratePawn(PawnGenerationRequest)", patchKind: "Manual Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Generated slave apparel is not locked by the post-generation fallback.", registryNameKey: "Mugirl.PatchRegistry.PawnGeneratorGeneratePawn"),
            Info(moduleName: "Restraints", patchClassName: "ITab_Pawn_Gear_DrawThingRow_Transpiler", targetTypeName: "RimWorld.ITab_Pawn_Gear", targetMethodName: "DrawThingRow", patchKind: "Transpiler", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Critical, failureBehavior: "Advanced slave-apparel drop tooltip injection is skipped and a once-warning is emitted."),
            Info(moduleName: "Restraints", patchClassName: "Pawn_ApparelTracker_Unlock_SlaveApparel_Patch", targetTypeName: "RimWorld.Pawn_ApparelTracker", targetMethodName: "Unlock", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Base auto-unlock paths can desynchronize worn slave apparel from its saved lock state."),
            Info(moduleName: "Restraints", patchClassName: "Pawn_ApparelTracker_ExposeData_SlaveApparel_Patch", targetTypeName: "RimWorld.Pawn_ApparelTracker", targetMethodName: "ExposeData", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Older saves can leave worn slave apparel missing from the base locked-apparel list."),
            Info(moduleName: "Restraints", patchClassName: "Pawn_ApparelTracker_TryDropFull_SlaveApparel_Patch", targetTypeName: "Verse.Pawn_ApparelTracker", targetMethodName: "TryDrop(Apparel, out Apparel, IntVec3, bool)", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Locked slave apparel can be removed through the full TryDrop overload."),
            Info(moduleName: "Restraints", patchClassName: "Pawn_ApparelTracker_TryDropWithResult_SlaveApparel_Patch", targetTypeName: "Verse.Pawn_ApparelTracker", targetMethodName: "TryDrop(Apparel, out Apparel)", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Locked slave apparel can be removed through the TryDrop overload that returns the dropped apparel."),
            Info(moduleName: "Restraints", patchClassName: "Pawn_ApparelTracker_TryDropSimple_SlaveApparel_Patch", targetTypeName: "Verse.Pawn_ApparelTracker", targetMethodName: "TryDrop(Apparel)", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Locked slave apparel can be removed through the simple TryDrop overload."),
            Info(moduleName: "Restraints", patchClassName: "Pawn_ApparelTracker_WouldReplaceLockedApparel_SlaveApparel_Patch", targetTypeName: "Verse.Pawn_ApparelTracker", targetMethodName: "WouldReplaceLockedApparel", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Force-wear checks ignore locked slave apparel that would be replaced by the new apparel."),
            Info(moduleName: "Restraints", patchClassName: "Pawn_ApparelTracker_Wear_SlaveApparelAutoStage_Patch", targetTypeName: "Verse.Pawn_ApparelTracker", targetMethodName: "Wear", patchKind: "Prefix + Finalizer", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Replacing apparel can trigger slave-apparel auto-stage callbacks and create complementary restraints from nothing."),
            Info(moduleName: "Restraints", patchClassName: "FloatMenuOptionProvider_Wear_SlaveApparel_Patch", targetTypeName: "RimWorld.FloatMenuOptionProvider_Wear", targetMethodName: "GetSingleOptionFor(Thing, FloatMenuContext)", patchKind: "Postfix", maySkipOriginal: false, compatibilityRisk: MugirlPatchRiskLevel.Medium, failureBehavior: "The wear float menu may offer apparel that would replace locked slave apparel."),
            Info(moduleName: "Restraints", patchClassName: "JobDriver_Wear_TryMakePreToilReservations_SlaveApparel_Patch", targetTypeName: "RimWorld.JobDriver_Wear", targetMethodName: "TryMakePreToilReservations", patchKind: "Prefix", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Wear jobs can reserve apparel that would replace locked slave apparel."),
            Info(moduleName: "Restraints", patchClassName: "JobDriver_Wear_TryUnequipSomething_SlaveApparel_Patch", targetTypeName: "RimWorld.JobDriver_Wear", targetMethodName: "TryUnequipSomething", patchKind: "Prefix + Finalizer", maySkipOriginal: true, compatibilityRisk: MugirlPatchRiskLevel.High, failureBehavior: "Wear jobs can proceed to unequip locked slave apparel or trigger slave-apparel auto-stage callbacks while making room for new apparel."),
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
