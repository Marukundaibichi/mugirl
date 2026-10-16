using System.IO;
using System.Runtime.CompilerServices;
using System.Xml;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade.Modules.Incidents;

public sealed class IncidentGoldenTests
{
    [Theory]
    [InlineData("Incidents.StorylineState")]
    [InlineData("Incidents.MigrationRecords")]
    [InlineData("Incidents.PunisherRecords")]
    public void ProductionPipelineMatchesGoldenAndRunsIdempotently(string step)
    {
        string directory = Path.Combine(GoldenRoot(), step);
        var actual = new XmlDocument();
        var expected = new XmlDocument();
        actual.Load(Path.Combine(directory, "before.xml"));
        expected.Load(Path.Combine(directory, "after.xml"));
        SaveUpgradeSession result = SaveXml.Upgrade(actual, UpgradeTables.Default);
        Assert.False(result.Report.Failed, result.Report.Error);
        Assert.Equal(SaveXml.Game(expected), SaveXml.Game(actual));
        SaveXml.Upgrade(actual, UpgradeTables.Default);
        Assert.Equal(SaveXml.Game(expected), SaveXml.Game(actual));
    }

    private static string GoldenRoot([CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "Golden"));
}
