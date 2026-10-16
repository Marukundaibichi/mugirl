using System.Xml;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade.Modules.Core;

public sealed class CoreRetiredRecordsTests
{
    [Fact]
    public void ValidationDriverCleanupIsLimitedToGameComponentEntries()
    {
        var builder = new UpgradeTableBuilder();
        builder.Module("Core", 100).Step(new CoreValidationComponentsStep());
        UpgradeTables tables = builder.Build();
        const string body = "<components><li Class=\"Mugirl.CorporateRuntimeValidation\"/><item Class=\"Mugirl.CorporateVisualValidation\"/><li Class=\"Other.Component\"/></components>"
            + "<world><components><li Class=\"Mugirl.CorporateVisualValidation\"/></components></world>";
        string result = SaveXml.Upgraded(body, tables);
        Assert.Equal("<components><item Class=\"Mugirl.CorporateVisualValidation\" /><li Class=\"Other.Component\" /></components>"
            + "<world><components><li Class=\"Mugirl.CorporateVisualValidation\" /></components></world>", result);
        Assert.Equal(result, SaveXml.Upgraded(result, tables));
    }

    [Fact]
    public void RetiredArmorAndWorkKeepUnrelatedThingsAndPlayerText()
    {
        XmlDocument document = SaveXml.Save("<maps><li><things>"
            + "<thing><def>Mugirl_NeuralCombatShoulders</def><id>Mugirl_NeuralCombatShoulders7</id></thing>"
            + "<thing><def>UnfinishedApparel</def><id>UnfinishedApparel8</id><recipe>Make_Mugirl_NeuralCombatShoulders</recipe></thing>"
            + "<thing><def>Apparel_Parka</def><id>Apparel_Parka9</id><label>Thing_Mugirl_NeuralCombatShoulders7</label></thing>"
            + "</things><billStack><bills><li><recipe>Make_Mugirl_NeuralCombatShoulders</recipe><loadID>42</loadID></li></bills></billStack></li></maps>");
        SaveUpgradeSession session = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(session.Report.Failed, session.Report.Error);
        Assert.Null(document.SelectSingleNode("//thing[def='Mugirl_NeuralCombatShoulders']"));
        Assert.Null(document.SelectSingleNode("//thing[recipe='Make_Mugirl_NeuralCombatShoulders']"));
        Assert.Empty(document.SelectNodes("//bills/li")!);
        Assert.Equal("Thing_Mugirl_NeuralCombatShoulders7", document.SelectSingleNode("//thing/label")!.InnerText);
    }
}
