using System.Linq;
using System.Xml;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade;

public sealed class DomReparentingTests
{
    [Fact]
    public void RegroupingRetainsObjectIdentityChildrenOrderAndLoadIds()
    {
        XmlDocument document = SaveXml.Save("<old><innerList><li><def>Steel</def><id>Steel1</id><label>  null  </label></li></innerList></old><amount>315.0001</amount>");
        UpgradeContext context = Context(document);
        XmlElement game = context.Document.Game;
        XmlElement old = game["old"]!;
        XmlElement list = old["innerList"]!;
        XmlNode thing = list.FirstChild!;
        string before = document.OuterXml;
        var plan = new EditPlan(context);
        XmlElement state = plan.AppendElement(game, "state");
        plan.SetAttribute(state, "Class", "New.State");
        plan.Move(list, state);
        plan.Move(game["amount"]!, state, "principal");
        plan.Remove(old);
        Assert.Equal(before, document.OuterXml);
        Assert.Null(state.ParentNode);

        plan.Apply(context.Index);
        Assert.Same(thing, state["innerList"]!.FirstChild);
        Assert.Empty(plan.RemovedLoadIds);
        Assert.Equal("<state Class=\"New.State\"><innerList><li><def>Steel</def><id>Steel1</id><label>  null  </label></li></innerList><principal>315.0001</principal></state>", SaveXml.Game(document));
        Assert.Same(state, Assert.Single(context.Index.ElementsWithMappedClass("New.State")));
    }

    [Fact]
    public void NestedNewContainersAndRenamedDestinationRemainIndexed()
    {
        XmlDocument document = SaveXml.Save("<old><value Class=\"A\">text</value></old><destination/>");
        UpgradeContext context = Context(document);
        XmlElement game = context.Document.Game;
        var plan = new EditPlan(context);
        XmlElement wrapper = plan.AppendElement(game["destination"]!, "wrapper");
        XmlElement inner = plan.AppendElement(wrapper, "inner");
        plan.Move(game["old"]!["value"]!, inner, "value2");
        plan.Rename(game["destination"]!, "state");
        plan.Remove(game["old"]!);
        plan.Apply(context.Index);
        Assert.Equal("<state><wrapper><inner><value2 Class=\"A\">text</value2></inner></wrapper></state>", SaveXml.Game(document));
        Assert.Equal("value2", Assert.Single(context.Index.ElementsWithMappedClass("A")).Name);
        Assert.Empty(plan.RemovedLoadIds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidFinalParentGraphFailsBeforeAnyMutation(bool cycle)
    {
        XmlDocument document = SaveXml.Save("<a><text>keep</text></a><b/>");
        UpgradeContext context = Context(document);
        XmlElement game = context.Document.Game;
        var plan = new EditPlan(context);
        plan.Move(game["a"]!, game["b"]!);
        if (cycle)
        {
            plan.Move(game["b"]!, game["a"]!);
        }
        else
        {
            plan.Remove(game["b"]!);
        }

        string before = document.OuterXml;
        Assert.Throws<InvalidOperationException>(() => plan.Apply(context.Index));
        Assert.Equal(before, document.OuterXml);
    }

    [Fact]
    public void RepeatedClassChangesProduceOnlyOneCurrentCandidate()
    {
        XmlDocument document = SaveXml.Save("<state Class=\"A\"/>");
        UpgradeContext context = Context(document);
        XmlElement state = context.Document.Game["state"]!;
        Assert.Single(context.Index.ElementsWithMappedClass("A"));
        var plan = new EditPlan(context);
        plan.SetAttribute(state, "Class", "B");
        plan.SetAttribute(state, "Class", "C");
        plan.Apply(context.Index);
        Assert.Empty(context.Index.ElementsWithMappedClass("A"));
        Assert.Empty(context.Index.ElementsWithMappedClass("B"));
        Assert.Single(context.Index.ElementsWithMappedClass("C"));
    }

    [Fact]
    public void ManyMovesKeepRequestedOrderWithoutDeletingObjects()
    {
        XmlDocument document = SaveXml.Save("<old>" + string.Concat(Enumerable.Range(0, 5000).Select(n => $"<li>{n}</li>")) + "</old>");
        UpgradeContext context = Context(document);
        XmlElement old = context.Document.Game["old"]!;
        var plan = new EditPlan(context);
        XmlElement next = plan.AppendElement(context.Document.Game, "next");
        foreach (XmlElement child in old.ChildNodes.Cast<XmlElement>().Reverse())
        {
            plan.Move(child, next);
        }

        plan.Remove(old);
        plan.Apply(context.Index);
        Assert.Equal(Enumerable.Range(0, 5000).Reverse().Select(n => n.ToString()), next.ChildNodes.Cast<XmlElement>().Select(n => n.InnerText));
        Assert.Empty(plan.RemovedLoadIds);
    }

    [Fact]
    public void MovingGameDataIntoTheMetadataHeaderIsRejectedBeforeMutation()
    {
        XmlDocument document = SaveXml.Save("<state>keep</state>");
        UpgradeContext context = Context(document);
        var plan = new EditPlan(context);
        plan.Move(context.Document.Game["state"]!, document.DocumentElement!["meta"]!);
        string before = document.OuterXml;
        Assert.Throws<InvalidOperationException>(() => plan.Apply(context.Index));
        Assert.Equal(before, document.OuterXml);
    }

    private static UpgradeContext Context(XmlDocument document)
    {
        var context = new UpgradeContext(SaveDocument.TryCreate(document)!, SaveXml.Empty(), new TestCatalog(), new SessionData(), 0);
        context.BuildIndex([]);
        return context;
    }
}
