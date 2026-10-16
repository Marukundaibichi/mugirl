using System.Diagnostics;
using System.Xml;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

public sealed class ObservationPathTests
{
    [Fact]
    public void PathCountsOnlySameNamedElementsAndReflectsMutation()
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml("<savegame><game><things>\n<li />text<!-- comment --><other /><li><apparel /></li></things></game></savegame>");
        var target = (XmlElement)document.SelectSingleNode("/savegame/game/things/li[2]/apparel")!;
        Assert.Equal("/savegame[1]/game[1]/things[1]/li[2]/apparel[1]", VanillaReadObservation.PathOf(target));
        target.ParentNode!.ParentNode!.PrependChild(document.CreateElement("li"));
        Assert.Equal("/savegame[1]/game[1]/things[1]/li[3]/apparel[1]", VanillaReadObservation.PathOf(target));
    }

    [Fact]
    public void WideListUsesOneForwardSiblingPass()
    {
        var document = new XmlDocument();
        document.LoadXml("<savegame><game><things /></game></savegame>");
        XmlNode things = document.SelectSingleNode("/savegame/game/things")!;
        for (int i = 0; i < 100000; i++) { things.AppendChild(document.CreateElement("li")); }
        var target = (XmlElement)things.LastChild!;
        var watch = Stopwatch.StartNew();
        Assert.Equal("/savegame[1]/game[1]/things[1]/li[100000]", VanillaReadObservation.PathOf(target));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), "A single path through 100,000 siblings must remain linear: " + watch.Elapsed);
    }
}
