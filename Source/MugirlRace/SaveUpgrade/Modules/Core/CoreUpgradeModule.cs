namespace Mugirl.SaveUpgrade;

internal sealed partial class CoreUpgradeModule
{
    static partial void RegisterStructure(UpgradeModuleBuilder module)
    {
        LegacyComponentRelations.Register(module);
        module.RenameSetting("enableStructuralCrashEvent", "enableOpeningStoryline", SaveSchema.SettingsCurrent);
        module.RenameSetting("enableMugirlMigrationEvent", "enableMigrationEvent", SaveSchema.SettingsCurrent);
        module.RenameSetting("enableMugirlFusionInvestmentEvent", "enableFusionInvestmentEvent", SaveSchema.SettingsCurrent);
        module.RenameSetting("enableRunawayMugirlFarmQuest", "enableRunawayFarmQuest", SaveSchema.SettingsCurrent);
        module.DropDef("ThingDef", "Mugirl_NeuralCombatShoulders", DefDropPolicy.RemoveThings, "Retired shoulder armor has been incorporated into the main armor.");
        module.DropDef("RecipeDef", "Make_Mugirl_NeuralCombatShoulders", DefDropPolicy.RemoveBills, "The retired shoulder recipe no longer exists.");
        module.Step(new CoreValidationComponentsStep());
        module.Step(new CoreRetiredShoulderWorkStep());
    }
}
