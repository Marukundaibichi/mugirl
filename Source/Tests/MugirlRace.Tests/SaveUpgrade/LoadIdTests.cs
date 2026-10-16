using System.Collections.Generic;
using System.Xml;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade;

/// <summary>加载 ID 改写：物品 ID、引用、动词与组件 ID、账单 ID、隐含 Def、以数字结尾的 defName。</summary>
public sealed class LoadIdTests
{
    [Fact]
    public void RewritesThingIdentityButLeavesReferenceTextUntilItIsRead()
    {
        UpgradeTables tables = SaveXml.Tables(m => m.RenameDef("ThingDef", "Mugirl_OldGun", "Mugirl_Gun_Rifle", "naming convention"));
        XmlDocument document = SaveXml.Save(
            "<thing><def>Mugirl_OldGun</def><id>Mugirl_OldGun12</id><verbTracker><verbs><li><loadID>CompEquippable_Mugirl_OldGun12_0</loadID></li></verbs></verbTracker></thing>"
            + "<refs>"
            + "<a>Thing_Mugirl_OldGun12</a>"
            + "<b>Verb_CompEquippable_Mugirl_OldGun12_0</b>"
            + "<c>Thing_Mugirl_OldGun12_barrel_Mugirl_Smash</c>"
            + "<d>NativeVerbs_Mugirl_OldGun12</d>"
            + "<e>Mugirl_OldGun12</e>"
            + "<f>Thing_Mugirl_OldGun123</f>"
            + "<g>Thing_XMugirl_OldGun12</g>"
            + "<h>Mugirl_OldGun12.Destroyed</h>"
            + "</refs>");
        SaveUpgradeSession session = SaveXml.Upgrade(document, tables);
        string result = SaveXml.Game(document);
        Assert.False(session.Report.Failed, session.Report.Error);
        LoadIdRemapper aliases = Assert.IsType<LoadIdRemapper>(session.LoadIds);

        // <def> 文本不在 DOM 中改写：Scribe_Defs 读取时由 BackCompatibleDefName 后缀改名。
        Assert.Equal(
            "<thing><def>Mugirl_OldGun</def><id>Mugirl_Gun_Rifle12</id><verbTracker><verbs><li><loadID>CompEquippable_Mugirl_OldGun12_0</loadID></li></verbs></verbTracker></thing>"
            + "<refs>"
            + "<a>Thing_Mugirl_OldGun12</a>"
            + "<b>Verb_CompEquippable_Mugirl_OldGun12_0</b>"
            + "<c>Thing_Mugirl_OldGun12_barrel_Mugirl_Smash</c>"
            + "<d>NativeVerbs_Mugirl_OldGun12</d>"
            + "<e>Mugirl_OldGun12</e>"
            + "<f>Thing_Mugirl_OldGun123</f>"
            + "<g>Thing_XMugirl_OldGun12</g>"
            + "<h>Mugirl_OldGun12.Destroyed</h>"
            + "</refs>",
            result);
        string[] oldIds = ["Thing_Mugirl_OldGun12", "Verb_CompEquippable_Mugirl_OldGun12_0", "Thing_Mugirl_OldGun12_barrel_Mugirl_Smash", "NativeVerbs_Mugirl_OldGun12", "Mugirl_OldGun12"];
        foreach (string oldId in oldIds)
        {
            Assert.Equal(oldId.Replace("Mugirl_OldGun12", "Mugirl_Gun_Rifle12", StringComparison.Ordinal), aliases.Reference(oldId));
        }
        Assert.Equal("CompEquippable_Mugirl_Gun_Rifle12_0", aliases.VerbIdentity("CompEquippable_Mugirl_OldGun12_0"));
        Assert.Equal("Thing_Mugirl_OldGun123", aliases.Reference("Thing_Mugirl_OldGun123"));
        Assert.Equal("Thing_XMugirl_OldGun12", aliases.Reference("Thing_XMugirl_OldGun12"));
        Assert.Equal("Mugirl_OldGun12.Destroyed", aliases.Reference("Mugirl_OldGun12.Destroyed"));
    }

    [Fact]
    public void KeepsTheNumberVanillaParsesFromDigitEndingDefNames()
    {
        UpgradeTables tables = SaveXml.Tables(m => m.RenameDef("ThingDef", "Bullet_Mugirl_AT23", "Mugirl_Bullet_AT23Autocannon", "category after prefix"));
        string result = SaveXml.Upgraded(
            "<thing><def>Bullet_Mugirl_AT23</def><id>Bullet_Mugirl_AT2345</id></thing><launcher>Thing_Bullet_Mugirl_AT2345</launcher>",
            tables);

        // 原版用 \d+$ 解析编号：旧 ID 的编号是 2345，不是 45。
        Assert.Equal(
            "<thing><def>Bullet_Mugirl_AT23</def><id>Mugirl_Bullet_AT23Autocannon2345</id></thing><launcher>Thing_Bullet_Mugirl_AT2345</launcher>",
            result);
        Assert.Equal("2345", LoadIdTokens.TrailingNumber("Bullet_Mugirl_AT2345").ToString());
        XmlDocument normalized = SaveXml.Save(result);
        SaveUpgradeSession repeated = SaveXml.Upgrade(normalized, tables);
        Assert.Equal(result, SaveXml.Game(normalized));
        Assert.Equal("Thing_Mugirl_Bullet_AT23Autocannon2345", repeated.LoadIds!.Reference("Thing_Bullet_Mugirl_AT2345"));
    }

    [Fact]
    public void RewritesImpliedDefsAndBillIds()
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.RenameDef("ThingDef", "Mugirl_OldRace", "Mugirl_NewRace", "renamed race");
            m.RenameDef("RecipeDef", "Mugirl_OldRecipe", "Mugirl_NewRecipe", "renamed recipe");
        });
        string result = SaveXml.Upgraded(
            "<thing Class=\"Corpse\"><def>Corpse_Mugirl_OldRace</def><id>Corpse_Mugirl_OldRace7</id><innerContainer><innerList>"
            + "<li Class=\"Pawn\"><def>Mugirl_OldRace</def><id>Mugirl_OldRace5</id></li></innerList></innerContainer></thing>"
            + "<jobs><a><bill>Bill_Mugirl_OldRecipe_42</bill></a><b><bill>Bill_Make_Mugirl_OldRace_3</bill></b><c><bill>Bill_Make_Other_3</bill></c></jobs>",
            tables);

        Assert.Equal(
            "<thing Class=\"Corpse\"><def>Corpse_Mugirl_OldRace</def><id>Corpse_Mugirl_NewRace7</id><innerContainer><innerList>"
            + "<li Class=\"Pawn\"><def>Mugirl_OldRace</def><id>Mugirl_NewRace5</id></li></innerList></innerContainer></thing>"
            + "<jobs><a><bill>Bill_Mugirl_OldRecipe_42</bill></a><b><bill>Bill_Make_Mugirl_OldRace_3</bill></b><c><bill>Bill_Make_Other_3</bill></c></jobs>",
            result);
        LoadIdRemapper aliases = SaveXml.Upgrade(SaveXml.Save(result), tables).LoadIds!;
        Assert.Equal("Bill_Mugirl_NewRecipe_42", aliases.Reference("Bill_Mugirl_OldRecipe_42"));
        Assert.Equal("Bill_Make_Mugirl_NewRace_3", aliases.Reference("Bill_Make_Mugirl_OldRace_3"));
        Assert.Equal("Bill_Make_Other_3", aliases.Reference("Bill_Make_Other_3"));
    }

    [Fact]
    public void RewritesUnnaturalCorpseIdsAndReferences()
    {
        UpgradeTables tables = SaveXml.Tables(m => m.RenameDef("ThingDef", "Mugirl_OldRace", "Mugirl_NewRace", "renamed race"));
        string result = SaveXml.Upgraded(
            "<thing><def>UnnaturalCorpse_Mugirl_OldRace</def><id>UnnaturalCorpse_Mugirl_OldRace7</id></thing><corpse>Thing_UnnaturalCorpse_Mugirl_OldRace7</corpse>", tables);

        Assert.Equal("<thing><def>UnnaturalCorpse_Mugirl_OldRace</def><id>UnnaturalCorpse_Mugirl_NewRace7</id></thing><corpse>Thing_UnnaturalCorpse_Mugirl_OldRace7</corpse>", result);
        Assert.Equal("Thing_UnnaturalCorpse_Mugirl_NewRace7", SaveXml.Upgrade(SaveXml.Save(result), tables).LoadIds!.Reference("Thing_UnnaturalCorpse_Mugirl_OldRace7"));
        Assert.Equal("UnnaturalCorpse_Mugirl_NewRace", DefNameResolver.Resolve(tables, typeof(Verse.ThingDef), "UnnaturalCorpse_Mugirl_OldRace", "UnnaturalCorpse_Mugirl_OldRace", (_, _) => false));
    }

    [Fact]
    public void LeavesMotesAndUnrelatedTextAlone()
    {
        UpgradeTables tables = SaveXml.Tables(m => m.RenameDef("ThingDef", "Mugirl_OldMote", "Mugirl_Mote_New", "naming convention"));
        const string Body = "<thing><def>Mugirl_OldMote</def><id>Mugirl_OldMote</id></thing><text>Mugirl_OldMote</text><pos>(12, 0, 34)</pos>";

        Assert.Equal(Body, SaveXml.Upgraded(Body, tables));
    }

    [Fact]
    public void ScannerMatchesOnlyUnderscoreBoundedTokens()
    {
        var renames = new Dictionary<string, string>(StringComparer.Ordinal) { ["A_B12"] = "C12" };
        var removed = new HashSet<string>(StringComparer.Ordinal) { "Lord_7" };
        var scanner = new LoadIdScanner(renames, removed);

        Assert.Equal(LoadIdScanner.Result.Rewritten, scanner.Scan("Thing_A_B12_x_A_B12", out string? both));
        Assert.Equal("Thing_C12_x_C12", both);
        Assert.Equal(LoadIdScanner.Result.Unchanged, scanner.Scan("Thing_A_B123", out _));
        Assert.Equal(LoadIdScanner.Result.Unchanged, scanner.Scan("XA_B12", out _));
        Assert.Equal(LoadIdScanner.Result.ReferencesRemoved, scanner.Scan("Lord_7", out _));
        Assert.Equal(LoadIdScanner.Result.Unchanged, scanner.Scan("Lord_77", out _));
        Assert.Equal(LoadIdScanner.Result.Unchanged, scanner.Scan("no digits here", out _));
    }

    [Theory]
    [InlineData("Bill_Make_X_42", true, "Make_X", "42")]
    [InlineData("Bill_X_1", true, "X", "1")]
    [InlineData("Bill_X_", false, "", "")]
    [InlineData("Bill_42", false, "", "")]
    [InlineData("Thing_X_1", false, "", "")]
    public void ParsesBillLoadIds(string text, bool ok, string recipe, string number)
    {
        Assert.Equal(ok, LoadIdTokens.TryParseBillId(text, out string parsedRecipe, out string parsedNumber));
        Assert.Equal(recipe, parsedRecipe);
        Assert.Equal(number, parsedNumber);
    }

    [Fact]
    public void RenameTargetsOfThingAndRecipeDefsMustNotEndInADigit()
    {
        var builder = new UpgradeTableBuilder();
        builder.Module(SaveXml.ModuleName, 1).RenameDef("ThingDef", "Mugirl_Old", "Mugirl_New2", "bad target");

        UpgradeTableException exception = Assert.Throws<UpgradeTableException>(() => builder.Build());
        Assert.Contains("ends in a digit", exception.Message, StringComparison.Ordinal);
    }
}
