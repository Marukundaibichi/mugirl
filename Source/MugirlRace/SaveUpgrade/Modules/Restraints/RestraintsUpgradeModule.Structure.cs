using Mugirl.Features.Restraints;

namespace Mugirl.SaveUpgrade;

internal sealed partial class RestraintsUpgradeModule
{
    static partial void RegisterStructure(UpgradeModuleBuilder module)
    {
        module.RenameLabel(OwnerSelector.ThingComp(typeof(CompBrainwashHelmet)), "nonBlockedTicks", "mugirlHelmetNonBlockedTicks", "Preserve exact component state under its unique label.");
        module.RenameLabel(OwnerSelector.ThingComp(typeof(CompBrainwashHelmet)), "currentTicksToChange", "mugirlHelmetTicksToChange", "Preserve exact component state under its unique label.");
        module.RenameLabel(OwnerSelector.ThingComp(typeof(CompBrainwashHelmet)), "lastManualUseTick", "mugirlHelmetLastManualUseTick", "Preserve exact component state under its unique label.");
        module.RenameLabel(OwnerSelector.ThingComp(typeof(CompMagneticShackles)), "lastManualUseTick", "mugirlShacklesLastManualUseTick", "Preserve exact component state under its unique label.");
        module.RenameLabel(OwnerSelector.ThingComp(typeof(CompMagneticShackles)), "isActive", "mugirlShacklesActive", "Preserve exact component state under its unique label.");
        module.RenameLabel(OwnerSelector.ThingComp(typeof(CompMagneticShackles)), "currentCycleInterval", "mugirlShacklesCycleInterval", "Preserve exact component state under its unique label.");
        module.RenameLabel(OwnerSelector.ThingComp(typeof(CompMagneticShackles)), "currentBindDurationTicks", "mugirlShacklesBindDurationTicks", "Preserve exact component state under its unique label.");
        module.RenameLabel(OwnerSelector.ThingComp(typeof(CompMagneticShackles)), "nextStateTick", "mugirlShacklesNextStateTick", "Preserve exact component state under its unique label.");
        module.RenameLabel(OwnerSelector.ThingComp(typeof(CompShockCollar)), "ticks", "mugirlShockCheckTicks", "Preserve exact component state under its unique label.");
        module.RenameLabel(OwnerSelector.ThingComp(typeof(CompShockCollar)), "lastManualUseTick", "mugirlShockLastManualUseTick", "Preserve exact component state under its unique label.");
        module.RenameLabel(OwnerSelector.HediffComp(typeof(HediffComp_ElectricShock)), "jitterTicksLeft", "mugirlShockJitterTicksLeft", "Preserve exact component state under its unique label.");
        module.RenameLabel(OwnerSelector.HediffComp(typeof(HediffComp_ElectricShock)), "filthTimers", "mugirlShockFilthTimers", "Preserve exact component state under its unique label.");
        module.RenameLabel(OwnerSelector.HediffComp(typeof(HediffComp_BrainwashPerformance)), "performancePlayer", "mugirlBrainwashPerformance", "Preserve exact component state under its unique label.");
        module.RenameLabel(OwnerSelector.GameComponent(typeof(GameComponent_BrainwashPerformance)), "activeBrainwashPerformances", "activePerformances", "Keep the same performance list and per-player progress.");
        RegisterStateSteps(module);
    }

    static partial void RegisterStateSteps(UpgradeModuleBuilder module);
}
