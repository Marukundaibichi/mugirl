using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade;

/// <summary>MooGirl 时代（≤0.4.4）不受支持：识别、剥离组件与可移除容器中的 def 引用，其余交给原版。</summary>
public sealed class MooGirlEraTests
{
    [Fact]
    public void StripsMooGirlComponentsAndFilterEntriesAndReportsTheEra()
    {
        var document = SaveXml.Save(
            "<components><li Class=\"MooGirl.MooGirl_GameComp\"><courierRaidTimer>298799</courierRaidTimer></li><li Class=\"MooGirlSkinFix.GameComponent_MooGirlSkinOnce\" /><li Class=\"Vanilla.Component\" /></components>"
            + "<filter Class=\"ThingFilter\"><allowedDefs><li>MooGirl_Milk</li><li>Steel</li></allowedDefs></filter>"
            + "<storyState Class=\"StoryState\"><lastFireTicks><keys><li>MooGirl_CourierRaid</li><li>RaidEnemy</li></keys><values><li>10</li><li>20</li></values></lastFireTicks></storyState>"
            + "<things><thing><def>MooGirl_Cheese</def><id>MooGirl_Cheese4</id></thing></things>"
            + "<pawn><name><nick>MooGirlFan</nick></name></pawn>");
        SaveUpgradeSession session = SaveXml.Upgrade(document, SaveXml.Empty());

        Assert.Equal(UpgradeState.MooGirlEra, session.Report.State);
        Assert.Equal(
            "<components><li Class=\"Vanilla.Component\" /></components>"
            + "<filter Class=\"ThingFilter\"><allowedDefs><li>Steel</li></allowedDefs></filter>"
            + "<storyState Class=\"StoryState\"><lastFireTicks><keys><li>RaidEnemy</li></keys><values><li>20</li></values></lastFireTicks></storyState>"
            + "<things><thing><def>MooGirl_Cheese</def><id>MooGirl_Cheese4</id></thing></things>"
            + "<pawn><name><nick>MooGirlFan</nick></name></pawn>",
            SaveXml.Game(document));
    }

    [Fact]
    public void DetectsTheEraFromDefReferencesAlone()
    {
        SaveUpgradeSession session = SaveXml.Upgrade(SaveXml.Save("<things><thing><def>MuGirl_Horn</def><id>MuGirl_Horn1</id></thing></things>"), SaveXml.Empty());

        Assert.Equal(UpgradeState.MooGirlEra, session.Report.State);
        Assert.True(session.Data.HasFlag(MooGirlEraStep.DetectedFlag));
    }

    [Fact]
    public void MugirlEraSavesAreNotMistakenForMooGirlSaves()
    {
        SaveUpgradeSession session = SaveXml.Upgrade(
            SaveXml.Save("<components><li Class=\"Mugirl.MugirlStoryState\" /></components><things><thing><def>Mugirl_Milk</def><id>Mugirl_Milk1</id></thing></things><text>MooGirl_Something</text>"),
            SaveXml.Empty());

        Assert.Equal(UpgradeState.Upgraded, session.Report.State);
        Assert.Equal(0, SaveXml.Actions(session, MooGirlEraStep.StepId));
    }
    [Theory]
    [InlineData("AttackMelee_MooGirl")]
    [InlineData("Consumed_MooGirlMilk")]
    [InlineData("Make_MooGirl_Armbinder")]
    [InlineData("Blueprint_MooGirl_MilkPoweredGenerator")]
    [InlineData("Corpse_MooGirl")]
    [InlineData("Meat_MooGirl")]
    public void ExactPublishedNamesDetectTheEraAndStripOnlyRemovableReferences(string legacyDef)
    {
        var document = SaveXml.Save("<filter Class=\"ThingFilter\"><allowedDefs><li>" + legacyDef + "</li><li>Steel</li></allowedDefs></filter>"
            + "<things><thing><def>" + legacyDef + "</def></thing></things>");
        SaveUpgradeSession session = SaveXml.Upgrade(document, SaveXml.Empty());

        Assert.Equal(UpgradeState.MooGirlEra, session.Report.State);
        Assert.Equal("<filter Class=\"ThingFilter\"><allowedDefs><li>Steel</li></allowedDefs></filter>"
            + "<things><thing><def>" + legacyDef + "</def></thing></things>", SaveXml.Game(document));
    }

    [Theory]
    [InlineData("GiveSlaveApperalGear")]
    [InlineData("UnlockSlaveApperal")]
    [InlineData("HumanlikeConstant")]
    [InlineData("GiveSlaveApparelGear")]
    [InlineData("UnlockSlaveApparel")]
    [InlineData("AI_GC_Courier")]
    [InlineData("AttackMelee_MooGirlExtended")]
    [InlineData("PrefixAttackMelee_MooGirl")]
    [InlineData("Make_MooGirl_Armbinder_Custom")]
    [InlineData("Corpse_MooGirlFake")]
    public void SharedGenericAndMerelySimilarNamesNeverIdentifyTheUnsupportedEra(string defName)
    {
        string body = "<filter Class=\"ThingFilter\"><allowedDefs><li>" + defName + "</li></allowedDefs></filter>";
        var document = SaveXml.Save(body);
        SaveUpgradeSession session = SaveXml.Upgrade(document, SaveXml.Empty());

        Assert.Equal(UpgradeState.Upgraded, session.Report.State);
        Assert.False(session.Data.HasFlag(MooGirlEraStep.DetectedFlag));
        Assert.Equal(body, SaveXml.Game(document));
    }

    [Fact]
    public void DetectingAnExactOldNameDoesNotAuthorizeStrippingSharedNames()
    {
        var document = SaveXml.Save("<filter Class=\"ThingFilter\"><allowedDefs><li>AttackMelee_MooGirl</li><li>HumanlikeConstant</li>"
            + "<li>GiveSlaveApperalGear</li><li>UnlockSlaveApperal</li><li>GiveSlaveApparelGear</li></allowedDefs></filter>");
        SaveUpgradeSession session = SaveXml.Upgrade(document, SaveXml.Empty());

        Assert.Equal(UpgradeState.MooGirlEra, session.Report.State);
        Assert.Equal("<filter Class=\"ThingFilter\"><allowedDefs><li>HumanlikeConstant</li><li>GiveSlaveApperalGear</li>"
            + "<li>UnlockSlaveApperal</li><li>GiveSlaveApparelGear</li></allowedDefs></filter>", SaveXml.Game(document));
    }

    [Fact]
    public void FreeTextListsAndDictionariesDoNotIdentifyAnEraOrLosePlayerText()
    {
        const string Body = "<notes><li>MooGirl_Milk</li></notes><memo><keys><li>MooGirl_CourierRaid</li></keys><values><li>Keep me</li></values></memo><text><def>MuGirl_Horn</def></text>";
        var document = SaveXml.Save(Body);
        SaveUpgradeSession session = SaveXml.Upgrade(document, SaveXml.Empty());
        Assert.Equal(UpgradeState.Upgraded, session.Report.State);
        Assert.Equal(Body, SaveXml.Game(document));
        Assert.Equal(0, SaveXml.Actions(session, MooGirlEraStep.StepId));
    }

    [Fact]
    public void IndentedThingStillProvidesTypedEraEvidenceWithoutChangingText()
    {
        var document = new System.Xml.XmlDocument { PreserveWhitespace = true };
        document.LoadXml("<savegame><meta><modIds><li>har.mugirlrace</li></modIds></meta><game><things><thing>\n  <def>MooGirl_Cheese</def>\n  <tickDelta>0</tickDelta>\n  <id>MooGirl_Cheese4</id>\n</thing></things></game></savegame>");
        string before = document.OuterXml;
        SaveUpgradeSession session = SaveXml.Upgrade(document, SaveXml.Empty());
        Assert.Equal(UpgradeState.MooGirlEra, session.Report.State);
        Assert.Equal(before, document.OuterXml);
    }

    [Theory]
    [InlineData("<noteBook Class='ThirdParty.UserNotebook'><allowedDefs><li>MooGirl_Milk</li><li>Player text</li></allowedDefs></noteBook>")]
    [InlineData("<noteBook Class='ThirdParty.UserNotebook'><storyState><lastFireTicks><keys><li>MooGirl_Event</li></keys><values><li>Player text</li></values></lastFireTicks></storyState></noteBook>")]
    [InlineData("<maps><li Class='ThirdParty.NotAMap'><storyState><recentRandomIncidents><li>MooGirl_Event</li></recentRandomIncidents></storyState></li></maps>")]
    public void FamiliarFieldNamesOnUnknownWritersRemainPlayerData(string body)
    {
        var document = SaveXml.Save(body);
        string before = document.OuterXml;
        SaveUpgradeSession session = SaveXml.Upgrade(document, SaveXml.Empty());
        Assert.Equal(UpgradeState.Upgraded, session.Report.State);
        Assert.Equal(before, document.OuterXml);
        Assert.Equal(0, SaveXml.Actions(session, MooGirlEraStep.StepId));
    }
}
