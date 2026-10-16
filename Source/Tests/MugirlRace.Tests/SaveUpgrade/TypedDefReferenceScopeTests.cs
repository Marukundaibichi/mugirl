using System.Xml;
using Mugirl.SaveUpgrade;
using RimWorld;

namespace Mugirl.Tests.SaveUpgrade;

public sealed class TypedDefReferenceScopeTests
{
    [Theory]
    [InlineData("<custom Class='ThingFilter'>LIST</custom>")]
    [InlineData("<custom Class='Verse.ThingFilter'>LIST</custom>")]
    [InlineData("<li Class='Bill_Production'><ingredientFilter>LIST</ingredientFilter></li>")]
    [InlineData("<li Class='RimWorld.Bill_Medical'><ingredientFilter>LIST</ingredientFilter></li>")]
    [InlineData("<thing Class='Building_Storage'><settings><filter>LIST</filter></settings></thing>")]
    [InlineData("<thing Class='Building_Sarcophagus'><storageSettings><filter>LIST</filter></storageSettings></thing>")]
    [InlineData("<thing Class='Frame'><storageSettings><filter>LIST</filter></storageSettings></thing>")]
    [InlineData("<thing Class='Blueprint_Storage'><settings><filter>LIST</filter></settings></thing>")]
    [InlineData("<thing Class='Building_Bookcase'><settings><filter>LIST</filter></settings></thing>")]
    [InlineData("<thing Class='Building_OutfitStand'><settings><filter>LIST</filter></settings></thing>")]
    [InlineData("<thing Class='Building_GrowthVat'><allowedNutritionSettings><filter>LIST</filter></allowedNutritionSettings></thing>")]
    [InlineData("<zone Class='Zone_Stockpile'><settings><filter>LIST</filter></settings></zone>")]
    [InlineData("<unknown><settings Class='StorageSettings'><filter>LIST</filter></settings></unknown>")]
    [InlineData("<outfitDatabase><outfits><li><filter>LIST</filter></li></outfits></outfitDatabase>")]
    [InlineData("<foodRestrictionDatabase><foodRestrictions><li><filter>LIST</filter></li></foodRestrictions></foodRestrictionDatabase>")]
    [InlineData("<readingPolicyDatabase><readingPolicies><li><defFilter>LIST</defFilter></li></readingPolicies></readingPolicyDatabase>")]
    [InlineData("<readingPolicyDatabase><readingPolicies><li><effectFilter>LIST</effectFilter></li></readingPolicies></readingPolicyDatabase>")]
    [InlineData("<maps><li><storageGroups><groups><li><settings><filter>LIST</filter></settings></li></groups></storageGroups></li></maps>")]
    public void RecognizesVerifiedEngineWritersAndRootedPolicyPaths(string shape) => CheckBothLists(shape, true);

    [Theory]
    [InlineData("<noteBook Class='ThirdParty.UserNotebook'>LIST</noteBook>")]
    [InlineData("<noteBook Class='ThirdParty.Unknown'><filter>LIST</filter></noteBook>")]
    [InlineData("<thing Class='ThirdParty.Storage'><settings><filter>LIST</filter></settings></thing>")]
    [InlineData("<li Class='ThirdParty.Bill'><ingredientFilter>LIST</ingredientFilter></li>")]
    [InlineData("<filter>LIST</filter>")]
    [InlineData("<memo><outfitDatabase><outfits><li><filter>LIST</filter></li></outfits></outfitDatabase></memo>")]
    [InlineData("<outfitDatabase><outfits><li Class='ThirdParty.Policy'><filter>LIST</filter></li></outfits></outfitDatabase>")]
    [InlineData("<outfitDatabase Class='ThirdParty.Database'><outfits><li><filter>LIST</filter></li></outfits></outfitDatabase>")]
    [InlineData("<thing Class='Building_Storage'><settings Class='ThirdParty.Settings'><filter>LIST</filter></settings></thing>")]
    [InlineData("<thing Class='Building_Storage'><settings><filter Class='ThirdParty.Filter'>LIST</filter></settings></thing>")]
    [InlineData("<thing Class='Building_Storage'><storageSettings><filter>LIST</filter></storageSettings></thing>")]
    [InlineData("<thing Class='Bill_Production'><productFilter>LIST</productFilter></thing>")]
    [InlineData("<thing Class='Unknown['><settings><filter>LIST</filter></settings></thing>")]
    public void UnknownTypesSimilarLabelsAndShadowedRootPathsRemainUntouched(string shape) => CheckBothLists(shape, false);

    [Theory]
    [InlineData("animalFilter", "CompAnimalPenMarker")]
    [InlineData("autoCutFilter", "CompAutoCut")]
    [InlineData("allowedNutritionSettings/filter", "CompBiosculpterPod")]
    [InlineData("allowedShellsSettings/filter", "CompChangeableProjectile")]
    public void SharedCompFieldsRequireTheActualDefToCompRelation(string path, string comp)
    {
        string nested = "<allowedDefs><li>MooGirl_Milk</li></allowedDefs>";
        string[] segments = path.Split('/');
        for (int i = segments.Length - 1; i >= 0; i--) { nested = "<" + segments[i] + ">" + nested + "</" + segments[i] + ">"; }
        XmlDocument xml = SaveXml.Save("<thing Class='ThingWithComps'><def>TestOwner</def><id>TestOwner8</id>" + nested + "</thing>");
        XmlElement list = (XmlElement)xml.SelectSingleNode("//allowedDefs")!;
        Assert.False(TypedDefReferenceScope.IsThingFilterList(list, Context(xml, new TestCatalog())));
        Assert.True(TypedDefReferenceScope.IsThingFilterList(list, Context(xml, new TestCatalog().With("ThingDef", "TestOwner", comp))));
        Assert.False(TypedDefReferenceScope.IsThingFilterList(list, Context(xml, new TestCatalog().With("ThingDef", "TestOwner", "ThirdParty." + comp))));
    }

    private static void CheckBothLists(string shape, bool expected)
    {
        foreach (string label in new[] { "allowedDefs", "disallowedSpecialFilters" })
        {
            XmlDocument xml = SaveXml.Save(shape.Replace("LIST", "<" + label + "><li>MooGirl_Milk</li><li>Player text</li></" + label + ">", StringComparison.Ordinal));
            string before = xml.OuterXml;
            Assert.Equal(expected, TypedDefReferenceScope.IsThingFilterList((XmlElement)xml.SelectSingleNode("//" + label)!, Context(xml, new TestCatalog())));
            Assert.Equal(before, xml.OuterXml);
        }
    }

    private static UpgradeContext Context(XmlDocument xml, TestCatalog catalog)
    {
        var context = new UpgradeContext(SaveDocument.TryCreate(xml)!, SaveXml.Empty(), catalog, new SessionData(), 0);
        context.BuildIndex(["allowedDefs", "disallowedSpecialFilters"]);
        return context;
    }
}
