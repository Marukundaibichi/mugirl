using System.Xml;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade.Modules.Core;

public sealed class CoreSettingsTests
{
    [Fact]
    public void ProductionTablesPreserveAllFourDisabledOptionsAndUnknownPlayerText()
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml("<SettingsBlock><ModSettings Class=\"Mugirl.MugirlSettings\">"
            + "<enableStructuralCrashEvent>False</enableStructuralCrashEvent>"
            + "<enableMugirlMigrationEvent>False</enableMugirlMigrationEvent>"
            + "<enableMugirlFusionInvestmentEvent>False</enableMugirlFusionInvestmentEvent>"
            + "<enableRunawayMugirlFarmQuest>False</enableRunawayMugirlFarmQuest>"
            + "<extraOutlineWidth>1.234567</extraOutlineWidth><unknown>  null  </unknown>"
            + "</ModSettings></SettingsBlock>");

        Assert.True(SettingsUpgrade.Upgrade(document, UpgradeTables.Default));
        Assert.Equal("<SettingsBlock><ModSettings Class=\"Mugirl.Core.MugirlSettings\">"
            + "<enableOpeningStoryline>False</enableOpeningStoryline>"
            + "<enableMigrationEvent>False</enableMigrationEvent>"
            + "<enableFusionInvestmentEvent>False</enableFusionInvestmentEvent>"
            + "<enableRunawayFarmQuest>False</enableRunawayFarmQuest>"
            + "<extraOutlineWidth>1.234567</extraOutlineWidth><unknown>  null  </unknown>"
            + "<settingsSchema>1</settingsSchema></ModSettings></SettingsBlock>", document.OuterXml);
        Assert.False(SettingsUpgrade.Upgrade(document, UpgradeTables.Default));
    }
}
