using System.Xml;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade;

public sealed class ValidationReadTracePathTests
{
    [Fact]
    public void PathsCountOnlyMatchingElementsAndReflectCurrentPositions()
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml("<savegame><game><things>\n<thing/><other/><!-- format --><thing><id>Mugirl2</id></thing></things></game></savegame>");
        XmlElement node = (XmlElement)document.SelectSingleNode("//thing[id]/id")!;
        Assert.Equal("/savegame[1]/game[1]/things[1]/thing[2]/id[1]", ValidationReadTrace.PathOf(node));
        node.ParentNode!.ParentNode!.RemoveChild(document.SelectSingleNode("//thing[1]")!);
        Assert.Equal("/savegame[1]/game[1]/things[1]/thing[1]/id[1]", ValidationReadTrace.PathOf(node));
    }

    [Fact]
    public void APathNearTheEndOfAWideMapRetainsItsExactPosition()
    {
        var document = new XmlDocument();
        document.LoadXml("<savegame><game><things/></game></savegame>");
        XmlElement things = (XmlElement)document.SelectSingleNode("//things")!;
        for (int i = 0; i < 50000; i++) { things.AppendChild(document.CreateElement("thing")); }
        XmlElement last = (XmlElement)things.LastChild!;
        XmlElement tracker = document.CreateElement("psychicEntropy");
        last.AppendChild(tracker);
        Assert.Equal("/savegame[1]/game[1]/things[1]/thing[50000]/psychicEntropy[1]", ValidationReadTrace.PathOf(tracker));
    }
}
