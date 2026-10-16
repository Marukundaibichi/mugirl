using Mugirl.Features.WeaponWheel;
using Mugirl.Features.Lances;

namespace Mugirl.SaveUpgrade;

internal sealed partial class CombatUpgradeModule
{
    static partial void RegisterStructure(UpgradeModuleBuilder module)
    {
        CombatMountUpgradeRegistration.Register(module);
        module.Step(new CombatWheelSlotRecordsStep());
        OwnerSelector wheel = OwnerSelector.ThingComp(typeof(CompWeaponWheel));
        module.RenameLabel(wheel, "weaponWheelReserveWeapons", "mugirlWheelReserveWeapons", "Use the canonical shared-component prefix without changing the ThingOwner.");
        module.RenameLabel(wheel, "weaponWheelSwordDanceFailureStreak", "mugirlWheelSwordDanceFailureStreak", "Keep the exact saved failure count and the existing post-load clamp.");
        module.ConsumeLabel(wheel, CombatWheelSlotRecordsStep.OldWeapons, CombatWheelSlotRecordsStep.StepId, "Pair each weapon reference with its exact saved unlock flag.");
        module.ConsumeLabel(wheel, CombatWheelSlotRecordsStep.OldUnlocks, CombatWheelSlotRecordsStep.StepId, "Pair each unlock flag with its original slot index.");
        OwnerSelector lance = OwnerSelector.ThingComp(typeof(CompLanceCharge));
        module.RenameLabel(lance, "pointCooldownUntilTick", "mugirlLancePointCooldownUntilTick", "Preserve the absolute cooldown deadline.");
        module.RenameLabel(lance, "lineCooldownUntilTick", "mugirlLanceLineCooldownUntilTick", "Preserve the absolute cooldown deadline.");
        module.Step(new LanceJobContinuationStep());
        module.ClaimJobDriver("Mugirl.Features.Lances.JobDriver_LanceCharge", LanceJobContinuationStep.StepId);

        // 发布 DLL 的 Toil 序列逐版相同；保留当前下标、等待余量、目标与任务身份。
        module.PreserveInFlightJobs("Mugirl.JobDriver_MountMugirl");
        module.PreserveInFlightJobs("Mugirl.JobDriver_DismountMugirl");
        module.PreserveInFlightJobs("Mugirl.JobDriver_LoadWeaponWheel");
    }
}
