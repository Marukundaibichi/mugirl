using Mugirl.Features.Restraints;

namespace Mugirl.SaveUpgrade;

internal sealed partial class RestraintsUpgradeModule
{
    static partial void RegisterStateSteps(UpgradeModuleBuilder module)
    {
        module.MaterializeDefault(OwnerSelector.ThingComp(typeof(CompMagneticShackles)), "mugirlShacklesNextStateTick", "-1",
            LegacyEvidence.LegacyClass | LegacyEvidence.LegacyLabel, "Keep the old unscheduled sentinel when the canonical dynamic schedule is always saved.",
            "lastManualUseTick", "isActive", "currentCycleInterval", "currentBindDurationTicks");
        module.ConsumeLabel(RestraintLockStateStep.Owner, "lockCount", RestraintLockStateStep.StepId, "Keep independent count and materialize the old dynamic default.");
        module.ConsumeLabel(RestraintLockStateStep.Owner, "isLocked", RestraintLockStateStep.StepId, "Keep the independent boolean including zero-count states.");
        module.ConsumeLabel(RestraintLockStateStep.Advanced, "isCracked", RestraintLockStateStep.StepId, "Keep cracked state and the existing post-load invariant.");
        module.Step(new RestraintLockStateStep());
        module.Step(new RestraintWornLockListStep());
        foreach (var site in RestraintBrainwashLabelsStep.Sites)
        {
            module.ConsumeLabel(site.Owner, "age", RestraintBrainwashLabelsStep.StepId, "The first shared age is the old Scribe input for every matching component.");
            if (site.Triggered is not null)
            {
                module.ConsumeLabel(site.Owner, "triggered", RestraintBrainwashLabelsStep.StepId, "The first shared triggered flag is the old Scribe input.");
            }
        }

        module.Step(new RestraintBrainwashLabelsStep());
        module.ClaimJobDriver("Mugirl.JobDriver_CrackBondageGear", RestraintItemJobStep.StepId);
        module.ClaimJobDriver("Mugirl.JobDriver_UnlockSlaveApparelGear", RestraintItemJobStep.StepId);
        module.ClaimJobDriver("Mugirl.JobDriver_UseItemOn", RestraintItemJobStep.StepId);
        module.Step(new RestraintItemJobStep());
        foreach (string type in new[] { "Mugirl.JobDriver_ForceUnlockSlaveApparel", "Mugirl.JobDriver_FollowRoper", "Mugirl.JobDriver_RemoveRopeMoo", "Mugirl.JobDriver_RopeMoo", "Mugirl.JobDriver_RopeToWallRopeHitch" })
        {
            module.PreserveInFlightJobs(type);
        }
        module.DropLabel(OwnerSelector.ThingComp(typeof(CompMagneticShackles)), "tickCounter", "The pre-refactor 1.7 reader no longer reads this unused field.");
        module.DropLabel(OwnerSelector.HediffComp(typeof(HediffComp_ElectricShock)), "currentTickCount", "The pre-refactor 1.7 reader no longer reads this unused field.");
        module.DropLabel(OwnerSelector.HediffComp(typeof(HediffComp_ElectricShock)), "remainingCycles", "The pre-refactor 1.7 reader no longer reads this unused field.");
    }
}
