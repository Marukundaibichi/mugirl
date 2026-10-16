using System.Reflection;
using System.Xml.Linq;
using HarmonyLib;
using Mugirl.Features.Milk;
using Mugirl.Features.Mounting;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

[Collection("A5 real Scribe")]
public sealed class CurrentSerializationDefaultsTests
{
    private static readonly Assembly Production = typeof(CompMilkProduction).Assembly;

    [Fact]
    public void ReviewedCurrentReadersAndContainersMatchAllBoundContracts()
        => new CurrentSerializationDefaults(Production, new Dictionary<string, SerializationPawnDefaults>()).VerifyAllContracts();

    [Fact]
    public void OnlyTheExactFalseSlotFieldIsOmitted()
    {
        XDocument doc = Pawn("<mugirlWheelSlotRecords><li><weapon>null</weapon><unlocked>False</unlocked><nested><unlocked>False</unlocked></nested></li>"
            + "<li><unlocked>True</unlocked></li><li Class=\"Other.Slot\"><unlocked>False</unlocked></li></mugirlWheelSlotRecords><unlocked>False</unlocked>");
        CurrentSerializationDefaults projection = Project(doc, CurrentSerializationDefaults.WheelType);
        Assert.Single(projection.Actions);
        Assert.Equal(4, doc.Descendants("unlocked").Count());
        Assert.Contains(doc.Descendants("unlocked"), e => e.Value == "True");
    }

    [Theory]
    [InlineData("Mugirl", "Other.Record", 1)]
    [InlineData("OtherDef", "Pawn", 1)]
    [InlineData("Mugirl", "Pawn", 0)]
    [InlineData("Mugirl", "Pawn", 2)]
    public void UnprovenPawnOrComponentDoesNotAuthorizeDefaultOmission(string def, string type, int count)
    {
        XDocument doc = Pawn("<mugirlWheelSlotRecords><li><unlocked>False</unlocked></li></mugirlWheelSlotRecords>", def, type);
        var evidence = new Dictionary<string, SerializationPawnDefaults> { ["Mugirl"] = Evidence(CurrentSerializationDefaults.WheelType, count) };
        var projection = new CurrentSerializationDefaults(Production, evidence);
        projection.Apply(doc);
        Assert.Empty(projection.Actions);
        Assert.Single(doc.Descendants("unlocked"));
    }

    [Fact]
    public void CorporateZeroIsBoundToTheActualOrderPath()
    {
        XDocument doc = Saves.Game("""
            <components><li Class="Mugirl.Features.Corporate.CorporateNetwork"><trade><orders>
            <li><creditedTurnover>0</creditedTurnover><nested><creditedTurnover>0</creditedTurnover></nested></li>
            <li><creditedTurnover>1</creditedTurnover></li><li Class="Other.Order"><creditedTurnover>0</creditedTurnover></li>
            </orders><creditedTurnover>0</creditedTurnover></trade></li>
            <li Class="Other.Network"><trade><orders><li><creditedTurnover>0</creditedTurnover></li></orders></trade></li></components>
            """);
        var projection = new CurrentSerializationDefaults(Production, new Dictionary<string, SerializationPawnDefaults>());
        projection.Apply(doc);
        Assert.Single(projection.Actions);
        Assert.Equal(5, doc.Descendants("creditedTurnover").Count());
    }

    [Fact]
    public void MissingMilkDeepFieldBecomesNullButAnExistingContinuationIsUntouched()
    {
        XDocument doc = Pawn("<nested><mugirlMilkAccrualContinuation>player text</mugirlMilkAccrualContinuation></nested>");
        Assert.Single(Project(doc, CurrentSerializationDefaults.MilkType).Actions);
        XElement pawn = doc.Descendants("thing").Single();
        Assert.Equal("True", pawn.Element("mugirlMilkAccrualContinuation")!.Attribute("IsNull")!.Value);
        Assert.Equal("player text", pawn.Element("nested")!.Value);
        XDocument existing = Pawn("<mugirlMilkAccrualContinuation Class=\"Mugirl.SaveUpgrade.LegacyMilkAccrualContinuation\"><anchorTick>17</anchorTick></mugirlMilkAccrualContinuation>");
        string before = existing.ToString(SaveOptions.DisableFormatting);
        Assert.Empty(Project(existing, CurrentSerializationDefaults.MilkType).Actions);
        Assert.Equal(before, existing.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void MapInitializationNullIsLimitedToTheActualTwoMapComponents()
    {
        XDocument doc = Saves.Game("""
            <components><li Class="Mugirl.Features.Incidents.MapComponent_Migration" /></components>
            <maps><li><components><li Class="Mugirl.Features.Incidents.MapComponent_Migration"><nested><initialization>text</initialization></nested></li>
            <li Class="Mugirl.Features.Incidents.MapComponent_Punisher" /><li Class="Other.Component" /></components></li></maps>
            """);
        var projection = new CurrentSerializationDefaults(Production, new Dictionary<string, SerializationPawnDefaults>());
        projection.Apply(doc);
        Assert.Equal(2, projection.Actions.Count);
        Assert.Equal(2, doc.Descendants("initialization").Count(e => SaveXml.HasNullAttribute(e)));
        Assert.Equal("text", doc.Descendants("nested").Single().Value);
        Assert.Empty(doc.Descendants("heldLoot"));
        Assert.Empty(doc.Descendants("lootBursts"));
    }

    [Theory]
    [InlineData(true, "True")]
    [InlineData(false, "False")]
    public void MountUsesTheUniqueActualTypedDefProperty(bool configured, string expected)
    {
        var def = new ThingDef { defName = "Mugirl", thingClass = typeof(Pawn), comps = [new CompProperties_Mount { turretFireAtWillDefault = configured }] };
        Dictionary<string, SerializationPawnDefaults> inputs = SerializationPawnDefaults.Read([def], Production);
        XDocument doc = Pawn("<nested><mugirlMountFireAtWill>text</mugirlMountFireAtWill></nested>");
        var projection = new CurrentSerializationDefaults(Production, inputs);
        projection.Apply(doc);
        Assert.Single(projection.Actions);
        Assert.Equal(expected, doc.Descendants("thing").Single().Element("mugirlMountFireAtWill")!.Value);
        Assert.Equal("text", doc.Descendants("nested").Single().Value);
        XDocument explicitValue = Pawn("<mugirlMountFireAtWill>" + (!configured ? "True" : "False") + "</mugirlMountFireAtWill>");
        projection = new CurrentSerializationDefaults(Production, inputs);
        projection.Apply(explicitValue);
        Assert.Empty(projection.Actions);
    }

    [Fact]
    public void WrongOrDuplicateMountPropertiesNeverAssumeTrue()
    {
        var wrong = new ThingDef { defName = "Mugirl", thingClass = typeof(Pawn), comps = [new CompProperties { compClass = typeof(CompMount) }] };
        var projection = new CurrentSerializationDefaults(Production, SerializationPawnDefaults.Read([wrong], Production));
        Assert.Throws<InvalidOperationException>(() => projection.Apply(Pawn(string.Empty)));
        var duplicate = new ThingDef { defName = "Mugirl", thingClass = typeof(Pawn), comps = [new CompProperties_Mount(), new CompProperties_Mount { turretFireAtWillDefault = false }] };
        projection = new CurrentSerializationDefaults(Production, SerializationPawnDefaults.Read([duplicate], Production));
        XDocument doc = Pawn(string.Empty);
        projection.Apply(doc);
        Assert.Empty(projection.Actions);
        Assert.Empty(doc.Descendants("mugirlMountFireAtWill"));
    }

    [Fact]
    public void RepeatedFieldsAndAdditionalSerializerPatchesFailClosed()
    {
        Assert.Throws<InvalidOperationException>(() => Project(Pawn("<mugirlWheelSlotRecords><li><unlocked>False</unlocked><unlocked>True</unlocked></li></mugirlWheelSlotRecords>"), CurrentSerializationDefaults.WheelType));
        MethodInfo method = Production.GetType("Mugirl.Features.WeaponWheel.WeaponWheelSlot")!.GetMethod("ExposeData")!;
        var harmony = new Harmony("A5.SerializationDefaults.NegativeControl");
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(CurrentSerializationDefaultsTests), nameof(Noop)));
        try
        {
            var projection = new CurrentSerializationDefaults(Production, new Dictionary<string, SerializationPawnDefaults> { ["Mugirl"] = Evidence(CurrentSerializationDefaults.WheelType, 1) });
            for (int i = 0; i < 2; i++)
            {
                Assert.Throws<InvalidOperationException>(() => projection.Apply(Pawn("<mugirlWheelSlotRecords><li><unlocked>False</unlocked></li></mugirlWheelSlotRecords>")));
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    [Fact]
    public void ANewPatchAfterSuccessfulVerificationInvalidatesTheReusedProjection()
    {
        var projection = new CurrentSerializationDefaults(Production, new Dictionary<string, SerializationPawnDefaults> { ["Mugirl"] = Evidence(CurrentSerializationDefaults.WheelType, 1) });
        projection.Apply(Pawn("<mugirlWheelSlotRecords><li><unlocked>False</unlocked></li></mugirlWheelSlotRecords>"));
        var harmony = new Harmony("A5.SerializationDefaults.LatePatch");
        harmony.Patch(Production.GetType("Mugirl.Features.WeaponWheel.WeaponWheelSlot")!.GetMethod("ExposeData")!, prefix: new HarmonyMethod(typeof(CurrentSerializationDefaultsTests), nameof(Noop)));
        try
        {
            Assert.Throws<InvalidOperationException>(() => projection.Apply(Pawn("<mugirlWheelSlotRecords><li><unlocked>False</unlocked></li></mugirlWheelSlotRecords>")));
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    [Fact]
    public void PawnShapedMetadataOutsideTheActualGameIsUntouched()
    {
        XElement pawn = Pawn("<mugirlWheelSlotRecords><li><unlocked>False</unlocked></li></mugirlWheelSlotRecords>").Descendants("thing").Single();
        var doc = new XDocument(new XElement("savegame", new XElement("meta", new XElement(pawn)), new XElement("game"), new XElement("customSnapshot", new XElement(pawn))));
        string before = doc.ToString(SaveOptions.DisableFormatting);
        Assert.Empty(Project(doc, CurrentSerializationDefaults.WheelType).Actions);
        Assert.Equal(before, doc.ToString(SaveOptions.DisableFormatting));
    }

    [Theory]
    [InlineData("pawnsAlive")]
    [InlineData("pawnsDead")]
    [InlineData("pawnsMothballed")]
    public void ImplicitPawnRequiresAnExactWorldPawnsDeepReader(string list)
    {
        XElement pawn = Pawn("<mugirlWheelSlotRecords><li><unlocked>False</unlocked></li></mugirlWheelSlotRecords>").Descendants("thing").Single();
        pawn.Name = "li";
        pawn.Attribute("Class")!.Remove();
        var doc = Saves.Game("<world><worldPawns><" + list + " /></worldPawns><customSnapshot /></world><customSnapshot />");
        doc.Descendants(list).Single().Add(new XElement(pawn));
        foreach (XElement wrong in doc.Descendants("customSnapshot")) { wrong.Add(new XElement(pawn)); }
        var projection = Project(doc, CurrentSerializationDefaults.WheelType);
        Assert.Single(projection.Actions);
        Assert.Equal(2, doc.Descendants("unlocked").Count());
    }

    [Theory]
    [InlineData("world")]
    [InlineData("worldPawns")]
    [InlineData("pawnsAlive")]
    public void ImplicitPawnRejectsAnUnknownAncestorClass(string ancestor)
    {
        XElement pawn = Pawn("<mugirlWheelSlotRecords><li><unlocked>False</unlocked></li></mugirlWheelSlotRecords>").Descendants("thing").Single();
        pawn.Name = "li";
        pawn.Attribute("Class")!.Remove();
        XDocument doc = Saves.Game("<world><worldPawns><pawnsAlive /></worldPawns></world>");
        doc.Descendants("pawnsAlive").Single().Add(pawn);
        doc.Descendants(ancestor).Single().SetAttributeValue("Class", "ThirdParty.X");
        Assert.Empty(Project(doc, CurrentSerializationDefaults.WheelType).Actions);
        Assert.Single(doc.Descendants("unlocked"));
    }

    [Theory]
    [InlineData("pawnsDead", "world")]
    [InlineData("pawnsDead", "worldPawns")]
    [InlineData("pawnsDead", "pawnsDead")]
    [InlineData("pawnsMothballed", "world")]
    [InlineData("pawnsMothballed", "worldPawns")]
    [InlineData("pawnsMothballed", "pawnsMothballed")]
    [InlineData("pawnsForcefullyKeptAsWorldPawns", "")]
    public void OtherDeepListsStillRequireExactOwnerAndReferenceListIsNeverDeep(string list, string ancestor)
    {
        XElement pawn = Pawn("<mugirlWheelSlotRecords><li><unlocked>False</unlocked></li></mugirlWheelSlotRecords>").Descendants("thing").Single();
        pawn.Name = "li";
        pawn.Attribute("Class")!.Remove();
        XDocument doc = Saves.Game("<world><worldPawns><" + list + " /></worldPawns></world>");
        doc.Descendants(list).Single().Add(pawn);
        if (ancestor.Length > 0) { doc.Descendants(ancestor).Single().SetAttributeValue("Class", "ThirdParty.X"); }
        Assert.Empty(Project(doc, CurrentSerializationDefaults.WheelType).Actions);
        Assert.Single(doc.Descendants("unlocked"));
    }

    private static void Noop() { }

    [Theory]
    [InlineData("prefix")]
    [InlineData("postfix")]
    [InlineData("finalizer")]
    public void InnerReaderHooksInvalidateTheSerializationContract(string role)
    {
        MethodInfo target = Production.GetType(CurrentSerializationDefaults.WheelType)!.GetMethod("PostExposeData")!;
        MethodInfo inner = new CurrentScribeRoots(Production).DirectCalls(target).First(m => m.Name == "EnsureCollections");
        var other = new Harmony("A5.Defaults.Inner.Negative");
        try
        {
            var hook = new HarmonyMethod(typeof(CurrentSerializationDefaultsTests), nameof(Noop)) { innerMethod = new InnerMethod(inner, []) };
            PatchProcessor processor = other.CreateProcessor(target);
            if (role == "prefix") { processor.AddInnerPrefix(hook); } else if (role == "postfix") { processor.AddInnerPostfix(hook); } else { processor.AddInnerFinalizer(hook); }
            processor.Patch();
            Assert.Throws<InvalidOperationException>(() => new CurrentSerializationDefaults(Production, new Dictionary<string, SerializationPawnDefaults>()).VerifyAllContracts());
        }
        finally { other.UnpatchAll(other.Id); }
    }

    private static CurrentSerializationDefaults Project(XDocument document, string comp)
    {
        var result = new CurrentSerializationDefaults(Production, new Dictionary<string, SerializationPawnDefaults> { ["Mugirl"] = Evidence(comp, 1) });
        result.Apply(document);
        return result;
    }

    private static SerializationPawnDefaults Evidence(string comp, int count)
        => new(["Pawn", "Verse.Pawn"], new Dictionary<string, int> { [comp] = count }, null, null);

    private static XDocument Pawn(string fields, string def = "Mugirl", string type = "Pawn")
        => Saves.Game("<things><thing Class=\"" + type + "\"><def>" + def + "</def><id>Mugirl1</id><kindDef>MugirlColonist</kindDef>" + fields + "</thing></things>");
}
