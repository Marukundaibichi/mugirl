using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade;

/// <summary>Def 删除：物品（含嵌套与隐含 Def）、账单、健康状态、过滤器与登记上下文中的引用，以及随子树删除的对象的引用。</summary>
public sealed class DefDropTests
{
    [Fact]
    public void UnknownWriterDoesNotLoseAStringListNamedLikeAThingFilter()
    {
        UpgradeTables tables = SaveXml.Tables(module => module.DropDef("ThingDef", "Mugirl_OldArmor", DefDropPolicy.RemoveThings, "retired"));
        var document = SaveXml.Save("<noteBook Class='ThirdParty.UserNotebook'><allowedDefs><li>Mugirl_OldArmor</li><li>Player text</li></allowedDefs></noteBook>");
        string before = document.OuterXml;
        SaveUpgradeSession session = SaveXml.Upgrade(document, tables);
        Assert.False(session.Report.Failed, session.Report.Error);
        Assert.Equal(before, document.OuterXml);
    }

    [Fact]
    public void RemovingThingsNullsEveryReferenceToTheirIds()
    {
        UpgradeTables tables = SaveXml.Tables(m => m.DropDef("ThingDef", "Mugirl_OldArmor", DefDropPolicy.RemoveThings, "merged into the main armor"));
        string result = UpgradeWithAliases(
            "<maps><li><things>"
            + "<thing><def>Mugirl_OldArmor</def><id>Mugirl_OldArmor12</id></thing>"
            + "<thing Class=\"Pawn\"><def>Human</def><id>Human3</id><apparel><wornApparel><innerList>"
            + "<li><def>Mugirl_OldArmor</def><id>Mugirl_OldArmor13</id></li><li><def>Apparel_Parka</def><id>Apparel_Parka4</id></li>"
            + "</innerList></wornApparel><lockedApparel><li>Thing_Mugirl_OldArmor13</li><li>Thing_Apparel_Parka4</li></lockedApparel></apparel>"
            + "<jobs><curJob><targetA>Thing_Mugirl_OldArmor12</targetA><verbToUse>Verb_CompEquippable_Mugirl_OldArmor12_0</verbToUse></curJob></jobs>"
            + "<memory><keys><li>Thing_Mugirl_OldArmor12</li><li>Thing_Human3</li></keys><values><li>1</li><li>2</li></values></memory></thing>"
            + "</things><zone><settings><filter Class=\"ThingFilter\"><allowedDefs><li>Mugirl_OldArmor</li><li>Steel</li></allowedDefs></filter></settings></zone></li></maps>",
            tables, out LoadIdRemapper aliases);

        Assert.Equal(
            "<maps><li><things>"
            + "<thing Class=\"Pawn\"><def>Human</def><id>Human3</id><apparel><wornApparel><innerList>"
            + "<li><def>Apparel_Parka</def><id>Apparel_Parka4</id></li>"
            + "</innerList></wornApparel><lockedApparel><li>Thing_Mugirl_OldArmor13</li><li>Thing_Apparel_Parka4</li></lockedApparel></apparel>"
            + "<jobs><curJob><targetA>Thing_Mugirl_OldArmor12</targetA><verbToUse>Verb_CompEquippable_Mugirl_OldArmor12_0</verbToUse></curJob></jobs>"
            + "<memory><keys><li>Thing_Mugirl_OldArmor12</li><li>Thing_Human3</li></keys><values><li>1</li><li>2</li></values></memory></thing>"
            + "</things><zone><settings><filter Class=\"ThingFilter\"><allowedDefs><li>Steel</li></allowedDefs></filter></settings></zone></li></maps>",
            result);
        Assert.Equal("null", aliases.Reference("Thing_Mugirl_OldArmor12"));
        Assert.Equal("null", aliases.Reference("Thing_Mugirl_OldArmor13"));
        Assert.Equal("null", aliases.Reference("Verb_CompEquippable_Mugirl_OldArmor12_0"));
        Assert.Equal(new[] { "null", "Thing_Human3" }, aliases.References(["Thing_Mugirl_OldArmor12", "Thing_Human3"]));
    }

    [Fact]
    public void DroppingAThingDefDropsItsImpliedDefs()
    {
        UpgradeTables tables = SaveXml.Tables(m => m.DropDef("ThingDef", "Mugirl_OldBeast", DefDropPolicy.RemoveThings, "retired race"));
        string result = UpgradeWithAliases(
            "<maps><li><things><thing Class=\"Corpse\"><def>Corpse_Mugirl_OldBeast</def><id>Corpse_Mugirl_OldBeast7</id></thing><thing><def>Meat_Mugirl_OldBeast</def><id>Meat_Mugirl_OldBeast8</id></thing></things></li></maps>"
            + "<billStack><bills><li><loadID>42</loadID><recipe>Make_Mugirl_OldBeast</recipe></li><li><loadID>43</loadID><recipe>Make_Parka</recipe></li></bills></billStack>"
            + "<job><bill>Bill_Make_Mugirl_OldBeast_42</bill></job>",
            tables, out LoadIdRemapper aliases);

        Assert.Equal(
            "<maps><li><things></things></li></maps><billStack><bills><li><loadID>43</loadID><recipe>Make_Parka</recipe></li></bills></billStack><job><bill>Bill_Make_Mugirl_OldBeast_42</bill></job>",
            result);
        Assert.Equal("null", aliases.Reference("Bill_Make_Mugirl_OldBeast_42"));
        Assert.Equal("null", aliases.Reference("Thing_Corpse_Mugirl_OldBeast7"));
        Assert.Equal("null", aliases.Reference("Thing_Meat_Mugirl_OldBeast8"));
    }

    [Fact]
    public void DroppingARaceRemovesUnnaturalCorpsesAndNullsTheirReferences()
    {
        UpgradeTables tables = SaveXml.Tables(m => m.DropDef("ThingDef", "Mugirl_OldBeast", DefDropPolicy.RemoveThings, "retired race"));
        string result = UpgradeWithAliases(
            "<maps><li><things><thing><def>UnnaturalCorpse_Mugirl_OldBeast</def><id>UnnaturalCorpse_Mugirl_OldBeast7</id></thing></things></li></maps><corpse>Thing_UnnaturalCorpse_Mugirl_OldBeast7</corpse>", tables, out LoadIdRemapper aliases);

        Assert.Equal("<maps><li><things></things></li></maps><corpse>Thing_UnnaturalCorpse_Mugirl_OldBeast7</corpse>", result);
        Assert.Equal("null", aliases.Reference("Thing_UnnaturalCorpse_Mugirl_OldBeast7"));
    }

    [Fact]
    public void RemovesHediffsOfDroppedHediffDefsInTheGamesFieldOrder()
    {
        // Hediff.ExposeData（1.7.4977）先写 loadID、ageTicks、tickAdded、visible、severity 等值，def 在其后。
        UpgradeTables tables = SaveXml.Tables(m => m.DropDef("HediffDef", "Mugirl_OldBuff", DefDropPolicy.RemoveHediffs, "retired"));
        string result = SaveXml.Upgraded(
            "<healthTracker><hediffSet><hediffs>"
            + "<li Class=\"HediffWithComps\"><loadID>1</loadID><ageTicks>500</ageTicks><visible>True</visible><severity>0.4</severity><def>Mugirl_OldBuff</def></li>"
            + "<li><loadID>2</loadID><ageTicks>9</ageTicks><def>Flu</def><severity>0.2</severity></li>"
            + "</hediffs></hediffSet></healthTracker>"
            + "<thing><def>Mugirl_OldBuff</def><id>Mugirl_OldBuff5</id></thing>",
            tables);

        // 同名的 ThingDef 物品不受 HediffDef 删除影响：<def> 文本没有类型，只按结构判断。
        Assert.Equal(
            "<healthTracker><hediffSet><hediffs><li><loadID>2</loadID><ageTicks>9</ageTicks><def>Flu</def><severity>0.2</severity></li></hediffs></hediffSet></healthTracker>"
            + "<thing><def>Mugirl_OldBuff</def><id>Mugirl_OldBuff5</id></thing>",
            result);
    }

    [Fact]
    public void DropsDefReferencesInDeclaredContextsWithTheReadersNullForm()
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.DropDef("TaleDef", "Mugirl_OldTale", DefDropPolicy.RemoveReferences, "retired");
            m.DeclareValueContext("StoryTales", OwnerSelector.Document("storyState"), "tales", ValueContextKind.Def, "TaleDef");
            m.DeclareValueContext("StoryLastTale", OwnerSelector.Document("storyState"), "lastTale", ValueContextKind.Def, "TaleDef");
            m.DeclareValueContext("Unlisted", OwnerSelector.Document("other"), "tales", ValueContextKind.Def, "TaleDef");
            m.DeclareValueContext("TaleRefs", OwnerSelector.Document("refs"), "tales", ValueContextKind.DefReference, "TaleDef");
            m.DeclareRemovableContainer("storyState/tales", "List<string> without positional meaning.");
        });
        string result = SaveXml.Upgraded(
            "<storyState><tales><li>Mugirl_OldTale</li><li>Vanilla_Tale</li></tales><lastTale>Mugirl_OldTale</lastTale></storyState>"
            + "<other><tales><li>Mugirl_OldTale</li></tales></other><refs><tales><li>Mugirl_OldTale</li><li>Vanilla_Tale</li></tales></refs>",
            tables);

        // 登记为可移除的列表删除该项；单值字符串上下文删除标签（回到默认值）；未登记的列表置空以保留下标：
        // 字符串（Scribe_Values）写 IsNull，Def 引用（Scribe_Defs）写文本 null。
        Assert.Equal(
            "<storyState><tales><li>Vanilla_Tale</li></tales></storyState><other><tales><li IsNull=\"True\" /></tales></other>"
            + "<refs><tales><li>null</li><li>Vanilla_Tale</li></tales></refs>",
            result);
    }

    [Fact]
    public void ReferencesToThingsInsideRemovedThingsAreNulled()
    {
        UpgradeTables tables = SaveXml.Tables(m => m.DropDef("ThingDef", "Mugirl_OldCrate", DefDropPolicy.RemoveThings, "retired"));
        string result = UpgradeWithAliases(
            "<maps><li><things><thing><def>Mugirl_OldCrate</def><id>Mugirl_OldCrate1</id><innerContainer><innerList>"
            + "<li><def>Steel</def><id>Steel2</id></li></innerList></innerContainer></thing></things></li></maps>"
            + "<refs><a>Thing_Steel2</a><b>Thing_Steel22</b></refs>",
            tables, out LoadIdRemapper aliases);

        Assert.Equal("<maps><li><things></things></li></maps><refs><a>Thing_Steel2</a><b>Thing_Steel22</b></refs>", result);
        Assert.Equal("null", aliases.Reference("Thing_Steel2"));
        Assert.Equal("Thing_Steel22", aliases.Reference("Thing_Steel22"));
    }

    private static string UpgradeWithAliases(string body, UpgradeTables tables, out LoadIdRemapper aliases)
    {
        var document = SaveXml.Save(body);
        SaveUpgradeSession session = SaveXml.Upgrade(document, tables);
        Assert.False(session.Report.Failed, session.Report.Error);
        aliases = Assert.IsType<LoadIdRemapper>(session.LoadIds);
        return SaveXml.Game(document);
    }
}
