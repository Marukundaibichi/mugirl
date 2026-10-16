using System.Xml.Linq;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

public sealed class SaveNullContextTests
{
    private static XDocument Thing(string fields) => XDocument.Parse("<savegame><game><things><thing><def>Mugirl_Test</def><id>Mugirl_Test1</id>" + fields + "</thing></things></game></savegame>", LoadOptions.PreserveWhitespace);
    private static SaveDiff Diff(bool apparel = false, bool explosive = false) => new(new SaveDiffScope(
        new HashSet<string>(StringComparer.Ordinal) { "Mugirl_Test" }, new HashSet<string>(StringComparer.Ordinal) { "Mugirl_Test" }, (_, _) => false,
        new HashSet<string>(StringComparer.Ordinal), null,
        apparel ? new HashSet<string>(StringComparer.Ordinal) { "Mugirl_Test" } : null,
        explosive ? new HashSet<string>(StringComparer.Ordinal) { "Mugirl_Test" } : null));

    [Theory]
    [InlineData("abilities", true, false)]
    [InlineData("thingsIgnoredByExplosion", false, true)]
    public void KnownCollectionInitializationNeedsRuntimeOwnerEvidence(string label, bool apparel, bool explosive)
    {
        XDocument before = Thing($"<{label} IsNull=\"True\" />"), after = Thing($"<{label} />");
        SaveDiff known = Diff(apparel, explosive);
        SaveDifference change = Assert.Single(known.Compare(before, after));
        Assert.NotNull(known.ExplainCollectionInitialization(change));
        SaveDiff unknown = Diff();
        Assert.Null(unknown.ExplainCollectionInitialization(Assert.Single(unknown.Compare(before, after))));
        Assert.Null(known.ExplainCollectionInitialization(Assert.Single(known.Compare(after, before))));
    }

    [Theory]
    [InlineData("<playerText IsNull=\"True\" />", "<playerText />")]
    [InlineData("<nested><abilities IsNull=\"True\" /></nested>", "<nested><abilities /></nested>")]
    [InlineData("<abilities IsNull=\"True\" other=\"1\" />", "<abilities />")]
    [InlineData("<abilities IsNull=\"True\">secret</abilities>", "<abilities />")]
    [InlineData("<abilities IsNull=\"True\" />", "<abilities> </abilities>")]
    [InlineData("<abilities IsNull=\"True\" />", "<abilities><li>SomeAbility</li></abilities>")]
    public void TypedRuleCannotExplainOtherFieldsNestedOwnersTextOrNonemptyLists(string before, string after)
    {
        SaveDiff diff = Diff(apparel: true, explosive: true);
        List<SaveDifference> changes = diff.Compare(Thing(before), Thing(after));
        Assert.NotEmpty(changes);
        Assert.All(changes, d => Assert.Null(diff.ExplainCollectionInitialization(d)));
    }

    [Theory]
    [InlineData("null")]
    [InlineData(" null ")]
    [InlineData("")]
    [InlineData("  \n ")]
    public void LosingOrAddingPlayerTextIsReportedEvenForNullAndWhitespace(string text)
    {
        XDocument present = Thing(string.Empty);
        present.Descendants("thing").Single().Add(new XElement("playerText", text));
        SaveDiff diff = Diff();
        Assert.Equal("missing", Assert.Single(diff.Compare(present, Thing(string.Empty))).Kind);
        Assert.Equal("added", Assert.Single(diff.Compare(Thing(string.Empty), present)).Kind);
    }

    [Fact]
    public void NullAttributeMustActuallyBeTrue()
    {
        Assert.True(SaveXml.HasNullAttribute(XElement.Parse("<x IsNull=\"TRUE\" />")));
        Assert.False(SaveXml.HasNullAttribute(XElement.Parse("<x IsNull=\"False\" />")));
        Assert.False(SaveXml.HasNullAttribute(XElement.Parse("<x IsNull=\"invalid\" />")));
        Assert.False(SaveXml.HasNullAttribute(XElement.Parse("<x>null</x>")));
        Assert.True(SaveXml.IsNullReference(XElement.Parse("<x>null</x>")));
        Assert.False(SaveXml.IsNullReference(XElement.Parse("<x> null </x>")));
        Assert.Equal("attribute", Assert.Single(Diff().Compare(Thing("<x IsNull=\"False\">text</x>"), Thing("<x>text</x>"))).Kind);
    }

    [Theory]
    [InlineData("null")]
    [InlineData(" null ")]
    [InlineData(" \n ")]
    public void ScalarFactsDoNotInterpretPlayerTextAsANullReference(string text)
    {
        XDocument save = new(new XElement("savegame", new XElement("game", new XElement("components", new XElement("li",
            new XAttribute("Class", "Mugirl.Story"), new XElement("playerText", text))))));
        Assert.Equal(text, SaveFacts.Read(save, SaveFactQueries.Legacy([], [])).Values["component/Mugirl.Story/playerText"]);
    }

    [Fact]
    public void UpgradeManifestCannotBlanketExplainAnUnknownNullnessChange()
    {
        List<SaveDifference> changes = Diff().Compare(Thing("<playerText IsNull=\"True\" />"), Thing("<playerText />"));
        SaveNameMap map = SaveNameMap.Empty;
        Assert.Null(new SaveDiffExplainer(map, Saves.Catalog(map), schemaZero: true, _ => false).Explain(Assert.Single(changes), changes));
    }
}
