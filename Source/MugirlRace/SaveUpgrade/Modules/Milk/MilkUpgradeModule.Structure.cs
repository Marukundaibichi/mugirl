using Mugirl.Features.Appearance;
using Mugirl.Features.Abilities;
using Mugirl.Features.Milk;

namespace Mugirl.SaveUpgrade;

internal sealed partial class MilkUpgradeModule
{
    static partial void RegisterStructure(UpgradeModuleBuilder module)
    {
        OwnerSelector production = OwnerSelector.ThingComp(typeof(CompMilkProduction));
        foreach (var field in MilkProductionLabelsStep.Fields)
        {
            module.ConsumeLabel(production, field.Old, MilkProductionLabelsStep.StepId, "Identify the legacy production block before separating shared milk labels; preserve other components' values.");
        }
        module.Step(new MilkProductionLabelsStep());
        module.ConsumeLabel(production, MilkResourceAccrualStep.OldLabel, MilkResourceAccrualStep.StepId, "Keep deferred first-active-update semantics.");
        module.Step(new MilkResourceAccrualStep());
        module.RenameType("Mugirl.Features.Xenotype.GameComponent_XenotypeRepair", typeof(GameComponent_MilkUpgrade), "Preserve the intermediate renamed schedule when no final schema was written.");
        module.Step(new MilkXenotypeRepairStep());
        OwnerSelector repair = OwnerSelector.GameComponent(typeof(GameComponent_MilkUpgrade));
        foreach (string label in new[] { "xenotypeFixed", "throwGeneFixed", "dunkGeneFixed", "throwSystemGeneFixed", "checkTimer" })
        {
            module.ConsumeLabel(repair, label, MilkXenotypeRepairStep.StepId, "Preserve completion conjunction and remaining active-DLC ticks.");
        }

        module.ClaimJobDriver("Mugirl.JobDriver_GatherMilk", MilkGatherContinuationStep.StepId);
        module.Step(new MilkGatherContinuationStep());
        module.Step(new MilkDunkMotionStep());
        foreach (var field in MilkDunkMotionStep.Fields)
        {
            module.ConsumeLabel(MilkDunkMotionStep.Owner, field.Old, MilkDunkMotionStep.StepId, "Preserve exact motion state in a dedicated object.");
        }

        string dunkPhase = SaveTypeNames.Of(typeof(DunkPhase));
        module.DeclareEnumSite(MilkDunkMotionStep.Owner, "state", dunkPhase);
        module.DeclareEnumSite(OwnerSelector.Path(MilkDunkMotionStep.Owner, "motion"), "phase", dunkPhase);
        module.RenameEnumMember(dunkPhase, "Bounce", "Shattering");
        // 已发布 DLL 的 Toil 序列逐版本审计见 InFlightAudit/policy.csv；不重启当前 Toil。
        foreach (string driver in new[] { "Mugirl.JobDriver_Breastfeed", "Mugirl.JobDriver_DrinkMilkFromMugirl", "Mugirl.JobDriver_FeedMilkToDowned", "Mugirl.JobDriver_CastCharge", "Mugirl.JobDriver_MugirlDunk" })
        {
            module.PreserveInFlightJobs(driver);
        }
        Rename(module, OwnerSelector.ThingComp(typeof(CompMilkingDevice)), "storedCharges", "mugirlDeviceStoredCharges");
        Rename(module, OwnerSelector.ThingComp(typeof(CompMilkingDevice)), "storedMilkAmount", "mugirlDeviceStoredMilkAmount");
        Rename(module, OwnerSelector.ThingComp(typeof(CompMilkingDevice)), "autoReleaseEnabled", "mugirlDeviceAutoReleaseEnabled");
        Rename(module, OwnerSelector.ThingComp(typeof(CompMilkingDevice)), "powerReleaseEnabled", "mugirlDevicePowerReleaseEnabled");
        // 当前构建已无此组件类型：按旧 Def→组件关系中的旧类名选中两件榨乳器。
        OwnerSelector releaseEffect = OwnerSelector.ThingComp("Mugirl.CompMilkingDeviceReleaseEffect");
        foreach (string label in new[] { "effectTicksRemaining", "effectAge", "nextSoundTicks", "nextFleckTicks", "nextTextTicks" })
        {
            module.DropLabel(releaseEffect, label, "释放音效组件已删除，其 30 tick 瞬态状态不再保存");
        }

        Rename(module, OwnerSelector.HediffComp(typeof(HediffComp_Lactation)), "lastBirthTick", "mugirlLactationLastBirthTick");
        Rename(module, OwnerSelector.HediffComp(typeof(HediffComp_WhileHavingThoughts)), "flagAmIThinking", "mugirlThoughtActive");
        Rename(module, OwnerSelector.HediffComp(typeof(HediffComp_WhileHavingThoughts)), "checkingCounter", "mugirlThoughtCheckTicks");
        Rename(module, OwnerSelector.HediffComp(typeof(HediffComp_CureFoodEffects)), "applied", "mugirlCureApplied");
        Rename(module, OwnerSelector.Class(typeof(Thought_MemoryWithHediff)), "added", "mugirlMemoryHediffAdded");
        Rename(module, OwnerSelector.HediffComp(typeof(HediffComp_NurtureProgress)), "completed", "mugirlNurtureCompleted");
        Rename(module, OwnerSelector.ThingComp(typeof(CompBodyAccessory)), "temporaryBodyAccessoryVariant", "mugirlAccessoryVariant");
        Rename(module, OwnerSelector.ThingComp(typeof(CompBodyAccessory)), "temporaryBodyAccessoryVariantExpiresAt", "mugirlAccessoryExpiresAt");
    }

    private static void Rename(UpgradeModuleBuilder module, OwnerSelector owner, string oldLabel, string newLabel)
        => module.RenameLabel(owner, oldLabel, newLabel, "Give the shared component field an unambiguous owner prefix without changing its value.");
}
