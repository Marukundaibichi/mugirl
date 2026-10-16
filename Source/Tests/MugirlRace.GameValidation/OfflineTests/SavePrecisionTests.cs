using System.Xml.Linq;
using System.IO;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

public sealed class SavePrecisionTests
{
    [Theory]
    [InlineData("315.0001", "315.5")]
    [InlineData("315.0001", "315.0002")]
    [InlineData("0.00001", "0.00002")]
    [InlineData("0.5000001", "0.5")]
    [InlineData("1.000000001", "1.000000002")]
    [InlineData("9007199254740992.0", "9007199254740993.0")]
    [InlineData("(100000, 0, 200000)", "(100001, 0, 200000)")]
    [InlineData("(0.001, 0, 0)", "(0.002, 0, 0)")]
    [InlineData("RGBA(0.824, 0.733, 0.412, 1.000)", "RGBA(0.824, 0.732, 0.412, 1.000)")]
    [InlineData("  玩家自定义  315.00001\n", "玩家自定义  315.00001")]
    [InlineData("001.00000", "1")]
    public void UnscopedValuesCannotHideNumericOrPlayerTextDifferences(string before, string after)
    {
        Assert.False(SaveXml.SameValue(before, after));
        Assert.Equal(before, SaveXml.Normalize(before));
        Assert.Equal(after, SaveXml.Normalize(after));
    }

    [Theory]
    [InlineData("corporateLoanPrincipal", "315.0001", "315.5")]
    [InlineData("milkFullness", "0.00001", "0.00002")]
    [InlineData("position", "(100000, 0, 200000)", "(100001, 0, 200000)")]
    [InlineData("fractionalCount", "1.000000001", "1.000000002")]
    [InlineData("playerName", " leading space ", "leading space")]
    public void SaveDiffReportsTheExactChange(string label, string before, string after)
    {
        static XDocument Save(string label, string value) => new(new XElement("savegame", new XElement("game", new XElement("components",
            new XElement("li", new XAttribute("Class", "Mugirl.CorporateNetwork"), new XElement(label, value))))));
        var scope = new SaveDiffScope(new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), (_, _) => false, new HashSet<string>(StringComparer.Ordinal));
        SaveDifference difference = Assert.Single(new SaveDiff(scope).Compare(Save(label, before), Save(label, after)));
        Assert.Equal("changed", difference.Kind);
        Assert.Equal(before, difference.BeforeElement!.Value);
        Assert.Equal(after, difference.AfterElement!.Value);
    }

    [Fact]
    public void ScalarFactsPreserveEveryDigitAndPlayerWhitespace()
    {
        XDocument save = new(new XElement("savegame", new XElement("game", new XElement("components", new XElement("li",
            new XAttribute("Class", "Mugirl.CorporateNetwork"), new XElement("principal", "315.000123456789"), new XElement("playerText", "  0001.00000 \n "))))));
        SaveFacts facts = SaveFacts.Read(save, SaveFactQueries.Legacy([], []));
        Assert.Equal("315.000123456789", facts.Values["component/Mugirl.CorporateNetwork/principal"]);
        Assert.Equal("  0001.00000 \n ", facts.Values["component/Mugirl.CorporateNetwork/playerText"]);
    }

    [Fact]
    public void LoadingAndFactExtractionRetainWhitespaceOnlyPlayerText()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "<savegame><game><components><li Class=\"Mugirl.CorporateNetwork\"><playerText>  \n </playerText></li></components></game></savegame>");
            SaveFacts facts = SaveFacts.Read(SaveXml.Load(path), SaveFactQueries.Legacy([], []));
            Assert.Equal("  \n ", facts.Values["component/Mugirl.CorporateNetwork/playerText"]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
