using Mugirl.Features.Abilities;
using RimWorld;
using Verse;

namespace Mugirl.Tests.Abilities;

public sealed class ThrowBuildingConnectionsTests
{
    [Theory]
    [InlineData(LinkFlags.Wall, LinkFlags.Wall, true)]
    [InlineData(LinkFlags.Wall | LinkFlags.Rock, LinkFlags.Rock, true)]
    [InlineData(LinkFlags.Wall, LinkFlags.PowerConduit, false)]
    [InlineData(LinkFlags.Fences, LinkFlags.Fences, true)]
    [InlineData(LinkFlags.Sandbags, LinkFlags.Barricades, false)]
    [InlineData(LinkFlags.Custom1, LinkFlags.Custom1, true)]
    [InlineData(LinkFlags.Custom1, LinkFlags.Custom2, false)]
    [InlineData(LinkFlags.MapEdge, LinkFlags.MapEdge, false)]
    public void Connection_RequiresCompatibleFlags(LinkFlags source, LinkFlags other, bool expected)
        => Assert.Equal(expected, ThrowBuildingConnections.LinksTo(Linked(source), Linked(other)));

    [Fact]
    public void Furniture_DoesNotJoinWallsOrConduits()
    {
        var furniture = new ThingDef();
        foreach (LinkFlags flags in new[] { LinkFlags.Wall, LinkFlags.PowerConduit, LinkFlags.Custom1 })
        {
            Assert.False(ThrowBuildingConnections.LinksTo(Linked(flags), furniture));
            Assert.False(ThrowBuildingConnections.LinksTo(furniture, Linked(flags)));
        }
    }

    [Fact]
    public void UnlinkedGraphic_OnlyContributesFlagsAsTheOtherEnd()
    {
        ThingDef endpoint = Linked(LinkFlags.Fences);
        endpoint.graphicData.linkType = LinkDrawerType.None;
        Assert.False(ThrowBuildingConnections.LinksTo(endpoint, Linked(LinkFlags.Fences)));
        Assert.True(ThrowBuildingConnections.LinksTo(Linked(LinkFlags.Fences), endpoint));
    }

    [Fact]
    public void AsymmetricLink_AcceptsDeclaredFlagsAndDoorsOnly()
    {
        ThingDef fence = Linked(LinkFlags.Fences);
        fence.graphicData.linkType = LinkDrawerType.Asymmetric;
        fence.graphicData.asymmetricLink = new AsymmetricLinkData { linkFlags = LinkFlags.Wall, linkToDoors = true };
        Assert.True(ThrowBuildingConnections.LinksTo(fence, Linked(LinkFlags.Wall)));
        Assert.True(ThrowBuildingConnections.LinksTo(fence, new ThingDef { thingClass = typeof(Building_Door) }));
        Assert.False(ThrowBuildingConnections.LinksTo(fence, new ThingDef { thingClass = typeof(Building) }));
        Assert.False(ThrowBuildingConnections.LinksTo(fence, Linked(LinkFlags.PowerConduit)));
        fence.graphicData.linkType = LinkDrawerType.Basic;
        Assert.False(ThrowBuildingConnections.LinksTo(fence, Linked(LinkFlags.Wall)));
    }

    private static ThingDef Linked(LinkFlags flags) => new()
    {
        graphicData = new GraphicData { linkType = LinkDrawerType.Basic, linkFlags = flags },
    };
}
