using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Mugirl.Features.Corporate;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade.Modules.Corporate;

public sealed class CorporateWhitespaceTests
{
    [Fact]
    public void IndentedAndCompactReadersHaveTheSameActionsWithoutChangingPlayerWhitespace()
    {
        string body = $$"""
            <components>
              <li Class="{{typeof(CorporateNetwork).FullName}}">
                <corporateOrders xml:space="preserve">
                  <li><quantity>3</quantity><paid>10</paid><deliveredQuantity>1</deliveredQuantity><savedLabel xml:space="preserve">   </savedLabel></li>
                  <li IsNull="true" />
                </corporateOrders>
                <corporatePeopleOffers>
                  <li><paid>True</paid><price>317</price><priceVersion>1</priceVersion></li>
                </corporatePeopleOffers>
                <corporateMissions>
                  <li><kind>Distribution</kind><count>7</count><salesGoal>-1</salesGoal></li>
                </corporateMissions>
                <corporateRecords>
                  <li><detail xml:space="preserve">   </detail><key><![CDATA[  任意原文  ]]></key></li>
                </corporateRecords>
              </li>
            </components>
            """;
        string raw = Raw(body);
        XmlDocument spaced = Read(raw, true);
        XmlDocument compact = Read(raw, false);
        XmlNode label = spaced.SelectSingleNode("//savedLabel")!;
        XmlNode detail = spaced.SelectSingleNode("//detail")!;
        XmlNode cdata = spaced.SelectSingleNode("//key")!.FirstChild!;
        UpgradeTables tables = Tables();
        SaveUpgradeSession a = SaveXml.Upgrade(spaced, tables);
        SaveUpgradeSession b = SaveXml.Upgrade(compact, tables);
        Assert.False(a.Report.Failed, a.Report.Error);
        Assert.False(b.Report.Failed, b.Report.Error);
        Assert.Equal(a.Report.Steps.Select(s => (s.Id, s.Actions)), b.Report.Steps.Select(s => (s.Id, s.Actions)));
        Assert.True(XNode.DeepEquals(Semantic(spaced.DocumentElement!), Semantic(compact.DocumentElement!)));
        Assert.Same(label, spaced.SelectSingleNode("//trade/orders/li/savedLabel"));
        Assert.Same(detail, spaced.SelectSingleNode("//history/entries/li/detail"));
        Assert.Same(cdata, spaced.SelectSingleNode("//history/entries/li/key")!.FirstChild);
        Assert.Equal("   ", label.InnerText);
        Assert.Equal("   ", detail.InnerText);
        Assert.Equal("  任意原文  ", cdata.Value);
        Assert.Equal("3", spaced.SelectSingleNode("//creditedTurnover")!.InnerText);
        Assert.Equal("317", spaced.SelectSingleNode("//paidAmount")!.InnerText);
        Assert.Equal("6", spaced.SelectSingleNode("//salesGoal")!.InnerText);
        Assert.Equal("<li IsNull=\"true\" />", spaced.SelectSingleNode("//trade/orders/li[2]")!.OuterXml);
        foreach (XmlDocument document in new[] { spaced, compact })
        {
            string once = document.OuterXml;
            SaveUpgradeSession again = SaveXml.Upgrade(document, tables);
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
    [InlineData("<!--not a list item-->")]
    [InlineData("<?keep data?>")]
    public void UnsupportedListDataFailsAtomicallyAndIsNeverFilteredAway(string extra)
    {
        foreach (bool preserve in new[] { false, true })
        {
            XmlDocument document = Read(Raw($"<components><li Class=\"{typeof(CorporateNetwork).FullName}\"><corporateOrders>\n<li><paid>3</paid><quantity>1</quantity></li>\n{extra}\n</corporateOrders></li></components>"), preserve);
            string before = document.OuterXml;
            SaveUpgradeSession result = SaveXml.Upgrade(document, Tables());
            Assert.True(result.Report.Failed);
            Assert.Equal(CorporateReceiptDefaults.StepId, result.Report.FailedStep);
            Assert.Equal(before, document.OuterXml);
        }
    }

    [Fact]
    public void NullListWithHiddenContentFailsInsteadOfActivatingTheRecords()
    {
        XmlDocument document = Read(Raw($"<components><li Class=\"{typeof(CorporateNetwork).FullName}\"><corporateOrders IsNull=\"True\">\n<li><paid>3</paid></li>\n</corporateOrders></li></components>"), true);
        string before = document.OuterXml;
        Assert.True(SaveXml.Upgrade(document, Tables()).Report.Failed);
        Assert.Equal(before, document.OuterXml);
    }

    private static UpgradeTables Tables()
    {
        var builder = new UpgradeTableBuilder();
        UpgradeModuleBuilder module = builder.Module("Corporate", 300);
        module.Step(new CorporateReceiptDefaults());
        module.Step(new CorporateNetworkStructure());
        module.Step(new CorporateContactStructure());
        module.Step(new CorporateSupportGraph());
        return builder.Build();
    }

    private static string Raw(string body) => SaveXml.Save("").OuterXml.Replace("</game>", body + "</game>", StringComparison.Ordinal);

    private static XmlDocument Read(string xml, bool preserve)
    {
        var document = new XmlDocument { PreserveWhitespace = preserve, XmlResolver = null };
        document.LoadXml(xml);
        return document;
    }

    // 比较器只省略有子元素的结构容器内的排版节点；纯空白叶值也按原文比较。
    private static XElement Semantic(XmlElement element) => new(element.Name,
        element.Attributes.Cast<XmlAttribute>().Select(a => new XAttribute(a.Name == "xml:space" ? XNamespace.Xml + "space" : a.Name, a.Value)),
        element.ChildNodes.Cast<XmlNode>().Where(n => !DomEditor.HasElementChild(element) || n.NodeType is not (XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace))
            .Select(n => n is XmlElement child ? (object)Semantic(child) : n.OuterXml));
}
