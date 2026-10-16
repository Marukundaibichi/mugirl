namespace Mugirl.SaveUpgrade;

internal static class CombatMountUpgradeRegistration
{
    internal static void Register(UpgradeModuleBuilder module)
    {
        module.Step(new MountStateUpgrade());
        foreach ((string oldName, _, _) in MountStateUpgrade.Fields)
        {
            module.ConsumeLabel(MountStateUpgrade.Owner, oldName, MountStateUpgrade.StepId,
                "Preserve mount ownership, all accumulated counters and exact turret state.");
        }
    }
}
