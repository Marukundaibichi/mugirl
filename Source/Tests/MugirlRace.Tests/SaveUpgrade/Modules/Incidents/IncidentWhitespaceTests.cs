using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Mugirl.Features.Incidents;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade.Modules.Incidents;

public sealed class IncidentWhitespaceTests
{
    [Fact]
    public void ListIndicesActionsAndLeafTextAreIndependentOfPreservedIndentation()
    {
        string body = $$"""
            <maps><li><components>
              <li Class="{{typeof(MapComponent_Migration).FullName}}">
                <dataVersion>2</dataVersion>
                <migrationPawns xml:space="preserve">
                  <li>Thing_Mugirl31</li>
                  <li>null</li>
                </migrationPawns>
                <nextGrazeTicks>
                  <li>731</li>
                </nextGrazeTicks>
                <playerKilledMigrationPawnIds><li>   </li></playerKilledMigrationPawnIds>
              </li>
              <li Class="{{typeof(MapComponent_Punisher).FullName}}">
                <punishers>
                  <li>Thing_Mugirl32</li>
                  <li>null</li>
                </punishers>
                <punisherSpawnedAtTicks>
                  <li>971</li>
                </punisherSpawnedAtTicks>
                <punisherLeavingFlags>
                  <li>False</li>
                  <li>True</li>
                </punisherLeavingFlags>
              </li>
            </components></li></maps>
            """;
        // xml:space only on the leaf makes its intentional all-whitespace value survive both XML reader modes.
        body = body.Replace("<playerKilledMigrationPawnIds><li>", "<playerKilledMigrationPawnIds><li xml:space=\"preserve\">", StringComparison.Ordinal);
        string raw = Raw(body);
        XmlDocument spaced = Read(raw, true);
        XmlDocument compact = Read(raw, false);
        XmlNode pawnText = spaced.SelectSingleNode("//migrationPawns/li[1]")!.FirstChild!;
        XmlNode whiteLeaf = spaced.SelectSingleNode("//playerKilledMigrationPawnIds/li")!;
        SaveUpgradeSession a = SaveXml.Upgrade(spaced, Tables());
        SaveUpgradeSession b = SaveXml.Upgrade(compact, Tables());
        Assert.False(a.Report.Failed, a.Report.Error);
        Assert.False(b.Report.Failed, b.Report.Error);
        Assert.Equal(a.Report.Steps.Select(s => (s.Id, s.Actions)), b.Report.Steps.Select(s => (s.Id, s.Actions)));
        Assert.True(XNode.DeepEquals(Semantic(spaced.DocumentElement!), Semantic(compact.DocumentElement!)));
        Assert.Same(pawnText, spaced.SelectSingleNode("//migration/members/li[1]/pawn")!.FirstChild);
        Assert.Equal("731", spaced.SelectSingleNode("//migration/members/li[1]/grazeAtTick")!.InnerText);
        Assert.Equal("1", spaced.SelectSingleNode("//initialization/firstUnscheduledIndex")!.InnerText);
        Assert.Equal("null", spaced.SelectSingleNode("//migration/members/li[2]/pawn")!.InnerText);
        Assert.Equal("True", spaced.SelectSingleNode("//activePunishers/li[2]/leaving")!.InnerText);
        Assert.Same(whiteLeaf, spaced.SelectSingleNode("//migration/playerKillIds/li"));
        Assert.Equal("   ", whiteLeaf.InnerText);
        foreach (XmlDocument document in new[] { spaced, compact })
        {
            string once = document.OuterXml;
            SaveUpgradeSession again = SaveXml.Upgrade(document, Tables());
            Assert.False(again.Report.Failed, again.Report.Error);
            Assert.Equal(0, again.Report.TotalActions);
            Assert.Equal(once, document.OuterXml);
        }
    }

    [Theory]
    [InlineData("unexpected")]
    [InlineData("&#160;")]
    [InlineData("<![CDATA[ ]]>")]
    [InlineData("<foreign>keep me</foreign>")]
    [InlineData("<li><nested>keep me</nested></li>")]
    [InlineData("<!--not a list item-->")]
    [InlineData("<?keep data?>")]
    public void RemovedParallelContainersCannotHideUnrecognizedData(string extra)
    {
        foreach (bool preserve in new[] { false, true })
        {
            foreach (bool migration in new[] { false, true })
            {
                string type = (migration ? typeof(MapComponent_Migration) : typeof(MapComponent_Punisher)).FullName!;
                string label = migration ? "migrationPawns" : "punishers";
                XmlDocument document = Read(Raw($"<maps><li><components><li Class=\"{type}\"><{label}>\n<li>null</li>\n{extra}\n</{label}></li></components></li></maps>"), preserve);
                string before = document.OuterXml;
                SaveUpgradeSession result = SaveXml.Upgrade(document, Tables());
                Assert.True(result.Report.Failed);
                Assert.Equal(migration ? MigrationRecordsUpgrade.StepId : PunisherRecordsUpgrade.StepId, result.Report.FailedStep);
                Assert.Equal(before, document.OuterXml);
            }
        }
    }

    [Fact]
    public void NullListWithHiddenEntriesFailsBeforeTheOldContainerIsRemoved()
    {
        XmlDocument document = Read(Raw($"<maps><li><components><li Class=\"{typeof(MapComponent_Migration).FullName}\"><migrationPawns IsNull=\"true\">\n<li>Thing_Mugirl31</li>\n</migrationPawns></li></components></li></maps>"), true);
        string before = document.OuterXml;
        Assert.True(SaveXml.Upgrade(document, Tables()).Report.Failed);
        Assert.Equal(before, document.OuterXml);
    }

    private static UpgradeTables Tables()
    {
        var builder = new UpgradeTableBuilder();
        UpgradeModuleBuilder module = builder.Module("Incidents", 400);
        module.Step(new StorylineStateUpgrade());
        module.Step(new MigrationRecordsUpgrade());
        module.Step(new PunisherRecordsUpgrade());
        return builder.Build();
    }

    private static string Raw(string body) => SaveXml.Save("").OuterXml.Replace("</game>", body + "</game>", StringComparison.Ordinal);

    private static XmlDocument Read(string xml, bool preserve)
    {
        var document = new XmlDocument { PreserveWhitespace = preserve, XmlResolver = null };
        document.LoadXml(xml);
        return document;
    }

    private static XElement Semantic(XmlElement element) => new(element.Name,
        element.Attributes.Cast<XmlAttribute>().Select(a => new XAttribute(a.Name == "xml:space" ? XNamespace.Xml + "space" : a.Name, a.Value)),
        element.ChildNodes.Cast<XmlNode>().Where(n => !DomEditor.HasElementChild(element) || n.NodeType is not (XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace))
            .Select(n => n is XmlElement child ? (object)Semantic(child) : n.OuterXml));
}
