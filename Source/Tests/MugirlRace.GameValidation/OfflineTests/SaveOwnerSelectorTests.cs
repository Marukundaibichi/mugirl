using System.Xml.Linq;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

/// <summary>owner 选择器文本（K1 labels[].owner）的解析与求值。</summary>
public sealed class SaveOwnerSelectorTests
{
    private static readonly SaveNameMap Map = new K1Builder()
        .Type("Mugirl.CorporateNetwork", "Mugirl.Features.Corporate.GameComponent_CorporateNetwork")
        .Type("Mugirl.HediffComp_Shock", "Mugirl.Features.Restraints.HediffComp_Shock")
        .Build();

    [Theory]
    [InlineData("Any")]
    [InlineData("Class(Mugirl.A|Mugirl.B)")]
    [InlineData("GameComponent(Mugirl.X)")]
    [InlineData("MapComponent(Mugirl.X)")]
    [InlineData("WorldComponent(Mugirl.X)")]
    [InlineData("ThingComp(Mugirl.C)")]
    [InlineData("HediffComp(Mugirl.C)")]
    [InlineData("Document(maps/li/storyState)")]
    [InlineData("Path(Path(ThingComp(Mugirl.C), slots/li), inner)")]
    public void ParsesEveryWrittenForm(string text)
    {
        SaveOwnerSelector selector = SaveOwnerSelector.Parse(text);

        Assert.True(selector.IsValid);
        Assert.Equal(text, selector.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Class()")]
    [InlineData("Path(ThingComp(Mugirl.C))")]
    [InlineData("Unknown(Mugirl.C)")]
    public void RejectsUnknownText(string text) => Assert.False(SaveOwnerSelector.Parse(text).IsValid);

    [Fact]
    public void GameComponentMatchesTheMappedClassInTheGameList()
    {
        XDocument save = Saves.Game("<components><li Class=\"Mugirl.CorporateNetwork\"><x>1</x></li></components>"
            + "<maps><li><components><li Class=\"Mugirl.CorporateNetwork\"><x>1</x></li></components></li></maps>");
        SaveOwnerSelector selector = SaveOwnerSelector.Parse("GameComponent(Mugirl.Features.Corporate.GameComponent_CorporateNetwork)");
        XElement[] owners = [.. save.Descendants("li").Where(e => SaveXml.ClassOf(e) is not null)];

        Assert.True(selector.Matches(owners[0], Saves.Catalog(Map)));
        Assert.False(selector.Matches(owners[1], Saves.Catalog(Map)));
    }

    [Fact]
    public void HediffCompUsesTheLegacyDefRelation()
    {
        LegacyCorpus corpus = Saves.Corpus([("HediffDef", "Mugirl_Shocked", "Mugirl.HediffComp_Shock")], []);
        XDocument save = Saves.Game("<pawn><healthTracker><hediffSet><hediffs><li><def>Mugirl_Shocked</def></li><li><def>Flu</def></li></hediffs></hediffSet></healthTracker></pawn>");
        SaveOwnerSelector selector = SaveOwnerSelector.Parse("HediffComp(Mugirl.Features.Restraints.HediffComp_Shock)");
        XElement[] hediffs = [.. save.Descendants("hediffs").Single().Elements("li")];

        Assert.True(selector.Matches(hediffs[0], Saves.Catalog(Map, corpus)));
        Assert.False(selector.Matches(hediffs[1], Saves.Catalog(Map, corpus)));
    }

    [Fact]
    public void PathWalksUpToItsRootSelector()
    {
        LegacyCorpus corpus = Saves.Corpus([("ThingDef", "Mugirl", "Mugirl.CompWheel")], []);
        XDocument save = Saves.Game("<thing><def>Mugirl</def><id>Mugirl1</id><slots><li><x>1</x></li></slots></thing><other><slots><li><x>1</x></li></slots></other>");
        SaveOwnerSelector selector = SaveOwnerSelector.Parse("Path(ThingComp(Mugirl.CompWheel), slots/li)");
        XElement[] items = [.. save.Descendants("slots").Select(s => s.Element("li")!)];

        Assert.True(selector.Matches(items[0], Saves.Catalog(Map, corpus)));
        Assert.False(selector.Matches(items[1], Saves.Catalog(Map, corpus)));
    }

    [Fact]
    public void DocumentPathsStartAtTheGameElement()
    {
        XDocument save = Saves.Game("<maps><li><storyState><x>1</x></storyState></li></maps><world><storyState><x>1</x></storyState></world>");
        SaveOwnerSelector selector = SaveOwnerSelector.Parse("Document(maps/*/storyState)");
        XElement[] states = [.. save.Descendants("storyState")];

        Assert.True(selector.Matches(states[0], Saves.Catalog(Map)));
        Assert.False(selector.Matches(states[1], Saves.Catalog(Map)));
    }
}
