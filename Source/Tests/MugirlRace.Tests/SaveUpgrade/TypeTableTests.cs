using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade;

/// <summary>类型表：Class 改名（不区分大小写、泛型参数、原版短名）、删除策略与类型值上下文。</summary>
public sealed class TypeTableTests
{
    private static readonly string NewComponent = Fakes.Name<Fakes.NewComponent>();

    [Fact]
    public void GeneratedRowsCanResolveNestedTypesWithoutOpeningTheirVisibility()
    {
        Assert.Equal(typeof(Fakes.NewComponent), SaveTypeNames.RequireNested(typeof(Fakes), nameof(Fakes.NewComponent)));
        Assert.Throws<UpgradeTableException>(() => SaveTypeNames.RequireNested(typeof(Fakes), "MissingSavedState"));
    }

    [Fact]
    public void RenamesClassAttributesCaseInsensitively()
    {
        UpgradeTables tables = SaveXml.Tables(m => m.RenameType("Mugirl.OldComponent", typeof(Fakes.NewComponent), "renamed"));
        string result = SaveXml.Upgraded("<components><li Class=\"mugirl.oldcomponent\"><x>1</x></li><li Class=\"Other.Component\" /></components>", tables);

        Assert.Equal("<components><li Class=\"" + NewComponent + "\"><x>1</x></li><li Class=\"Other.Component\" /></components>", result);
    }

    [Fact]
    public void RenamesGenericArgumentsAndUsesShortNamesForVanillaTargets()
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.RenameType("Mugirl.OldComponent", typeof(Fakes.NewComponent), "renamed");
            m.RenameType("Mugirl.OldThing", typeof(Verse.ThingWithComps), "replaced by vanilla");
        });
        string result = SaveXml.Upgraded("<a Class=\"Verse.Holder`1[Mugirl.OldComponent]\" /><b Class=\"Mugirl.OldThing\" />", tables);

        Assert.Equal("<a Class=\"Verse.Holder`1[" + NewComponent + "]\" /><b Class=\"ThingWithComps\" />", result);
    }

    [Fact]
    public void DroppedTypesAreRemovedFromComponentListsAndNulledElsewhere()
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.DropType("Mugirl.RetiredComponent", TypeDropPolicy.RemoveElement, "retired");
            m.DropType("Mugirl.RetiredPart", TypeDropPolicy.NullOut, "keep list indices");
            m.DropType("Mugirl.RetiredHolder", TypeDropPolicy.RemoveElement, "retired");
        });
        string result = SaveXml.Upgraded(
            "<components><li Class=\"Mugirl.RetiredComponent\"><x>1</x></li><li Class=\"Keep\" /></components>"
            + "<parts><li Class=\"Mugirl.RetiredPart\" /><li Class=\"Keep\" /></parts><owner><holder Class=\"Mugirl.RetiredHolder\"><y>2</y></holder></owner>",
            tables);

        Assert.Equal(
            "<components><li Class=\"Keep\" /></components><parts><li IsNull=\"True\" /><li Class=\"Keep\" /></parts><owner><holder IsNull=\"True\" /></owner>",
            result);
    }

    [Fact]
    public void ADroppedLordJobTypeRemovesTheWholeLordAndItsReferences()
    {
        // Lord.ExposeData_StateGraph 在 PostLoadInit 解引用 lordJob：置空会抛出空引用，只能删除整个 Lord。
        // loadID 为 0 时 Scribe 不写这个标签，引用仍是 Lord_0。
        UpgradeTables tables = SaveXml.Tables(m => m.DropType("Mugirl.RetiredLordJob", TypeDropPolicy.NullOut, "retired"));
        string result = UpgradeWithAliases(
            "<maps><li><lordManager><lords>"
            + "<li><lordJob Class=\"Mugirl.RetiredLordJob\"><y>2</y></lordJob><curLordToilIdx>1</curLordToilIdx></li>"
            + "<li><loadID>3</loadID><lordJob Class=\"Mugirl.RetiredLordJob\" /></li>"
            + "<li><loadID>4</loadID><lordJob Class=\"LordJob_DefendBase\" /></li>"
            + "</lords></lordManager></li></maps>"
            + "<pawn><mindState><duty><lord>Lord_0</lord></duty></mindState><a>Lord_3</a><b>Lord_4</b><c>Lord_30</c></pawn>",
            tables, out LoadIdRemapper aliases);

        Assert.Equal(
            "<maps><li><lordManager><lords><li><loadID>4</loadID><lordJob Class=\"LordJob_DefendBase\" /></li></lords></lordManager></li></maps>"
            + "<pawn><mindState><duty><lord>Lord_0</lord></duty></mindState><a>Lord_3</a><b>Lord_4</b><c>Lord_30</c></pawn>",
            result);
        Assert.Equal("null", aliases.Reference("Lord_0"));
        Assert.Equal("null", aliases.Reference("Lord_3"));
        Assert.Equal("Lord_4", aliases.Reference("Lord_4"));
        Assert.Equal("Lord_30", aliases.Reference("Lord_30"));
    }

    [Fact]
    public void ReferencesToThingsRemovedWithTheirTypeAreNulled()
    {
        UpgradeTables tables = SaveXml.Tables(m => m.DropType("Mugirl.Building_RetiredBed", TypeDropPolicy.RemoveElement, "retired"));
        string result = UpgradeWithAliases(
            "<maps><li><things><thing Class=\"Mugirl.Building_RetiredBed\"><def>Mugirl_Bed</def><id>Mugirl_Bed1</id></thing></things></li></maps>"
            + "<pawn><ownership><ownedBed>Thing_Mugirl_Bed1</ownedBed></ownership></pawn>",
            tables, out LoadIdRemapper aliases);

        Assert.Equal("<maps><li><things></things></li></maps><pawn><ownership><ownedBed>Thing_Mugirl_Bed1</ownedBed></ownership></pawn>", result);
        Assert.Equal("null", aliases.Reference("Thing_Mugirl_Bed1"));
    }

    [Fact]
    public void ReferencesToBillsRemovedWithTheirTypeAreNulled()
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.DropType("Mugirl.Bill_Retired", TypeDropPolicy.RemoveElement, "retired");
            m.RenameDef("RecipeDef", "Mugirl_OldRecipe", "Mugirl_NewRecipe", "renamed");
        });
        string result = UpgradeWithAliases(
            "<billStack><bills><li Class=\"Mugirl.Bill_Retired\"><loadID>5</loadID><recipe>Mugirl_OldRecipe</recipe></li>"
            + "<li Class=\"Bill_Production\"><loadID>6</loadID><recipe>Mugirl_OldRecipe</recipe></li></bills></billStack>"
            + "<jobs><a><bill>Bill_Mugirl_OldRecipe_5</bill></a><b><bill>Bill_Mugirl_OldRecipe_6</bill></b></jobs>",
            tables, out LoadIdRemapper aliases);

        // 被删账单的 ID 按存档中的原配方名判断，先于配方改名；留下的账单引用改成新配方名。
        Assert.Equal(
            "<billStack><bills><li Class=\"Bill_Production\"><loadID>6</loadID><recipe>Mugirl_OldRecipe</recipe></li></bills></billStack>"
            + "<jobs><a><bill>Bill_Mugirl_OldRecipe_5</bill></a><b><bill>Bill_Mugirl_OldRecipe_6</bill></b></jobs>",
            result);
        Assert.Equal("null", aliases.Reference("Bill_Mugirl_OldRecipe_5"));
        Assert.Equal("Bill_Mugirl_NewRecipe_6", aliases.Reference("Bill_Mugirl_OldRecipe_6"));
    }

    [Fact]
    public void RewritesTypeValuedContexts()
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.RenameType("Mugirl.OldComponent", typeof(Fakes.NewComponent), "renamed");
            m.DropType("Mugirl.RetiredDef", TypeDropPolicy.RemoveElement, "retired");
            m.DeclareValueContext("TaleDefType", OwnerSelector.Document("tales/li/defData"), "defType", ValueContextKind.Type);
        });
        string result = SaveXml.Upgraded(
            "<tales><li><defData><defType>Mugirl.OldComponent</defType></defData></li><li><defData><defType>Mugirl.RetiredDef</defType><defName>X</defName></defData></li></tales>"
            + "<other><defType>Mugirl.OldComponent</defType></other>",
            tables);

        // 删除的类型在类型值上下文中删除标签（读档时为 null）。
        Assert.Equal(
            "<tales><li><defData><defType>" + NewComponent + "</defType></defData></li><li><defData><defName>X</defName></defData></li></tales>"
            + "<other><defType>Mugirl.OldComponent</defType></other>",
            result);
    }

    [Fact]
    public void DefaultCompositionMapsUnchangedAndRenamedClasses()
    {
        UpgradeTables tables = SaveXml.Tables(m => m.RenameType("Mugirl.OldComponent", typeof(Fakes.NewComponent), "renamed"));

        Assert.Equal(NewComponent, tables.MapClass("MUGIRL.OLDCOMPONENT"));
        Assert.Equal("Pawn", tables.MapClass("Pawn"));
        Assert.Equal("Pawn", tables.MapClass("Verse.Pawn"));
        Assert.Equal("Some.Mod.Type", tables.MapClass("Some.Mod.Type"));
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
