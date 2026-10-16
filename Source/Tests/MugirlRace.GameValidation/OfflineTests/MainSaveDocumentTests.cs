using System.IO;
using System.Xml;
using Mugirl.Features.Milk;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

[Collection("A5 real Scribe")]
public sealed class MainSaveDocumentTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MainIdentityRequiresBothTheActualDomAndActualFileAcrossSchemas(int schema)
    {
        using var scope = new ObservationMainDocumentFixture();
        var main = new XmlDocument();
        main.LoadXml("<savegame><game><components><li Class=\"Mugirl.Core.GameComponent_Mugirl\"><mugirlSchema>" + schema + "</mugirlSchema></li></components></game></savegame>");
        string path = Path.Combine(Path.GetTempPath(), "actual-save.rws");
        ObservationMainDocumentFixture.Bind(main, path);
        Assert.True(MainSaveDocument.Matches(typeof(CompMilkProduction).Assembly, main, path));
        Assert.False(MainSaveDocument.Matches(typeof(CompMilkProduction).Assembly, (XmlDocument)main.CloneNode(true), path));
        Assert.False(MainSaveDocument.Matches(typeof(CompMilkProduction).Assembly, main, Path.Combine(Path.GetTempPath(), "artifact-copy.rws")));
        Assert.False(MainSaveDocument.Matches(typeof(CompMilkProduction).Assembly, null, path));
    }
}
