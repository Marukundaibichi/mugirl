using System.Xml;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade;

/// <summary>DOM 原语与容器规则：改名、删除、置空、追加、字典成对删除、并行列表与不可移除列表。</summary>
public sealed class DomPrimitiveTests
{
    [Fact]
    public void Rename_KeepsAttributesChildrenAndPosition()
    {
        XmlDocument document = SaveXml.Save("<a /><old Class=\"X\" extra=\"1\"><li>1</li><li><x>2</x></li></old><b />");
        UpgradeContext context = Context(document);
        var plan = new EditPlan(context);
        plan.Rename(Element(document, "old"), "renamed");

        Assert.Equal(1, plan.Apply(context.Index));
        Assert.Equal("<a /><renamed Class=\"X\" extra=\"1\"><li>1</li><li><x>2</x></li></renamed><b />", SaveXml.Game(document));
    }

    [Fact]
    public void NullOut_ReplacesDeepObjectsAndWritesTheLeafFormOfTheReader()
    {
        XmlDocument document = SaveXml.Save(
            "<deep Class=\"X\"><a>1</a></deep><plain><a>1</a></plain><reference>Thing_A1</reference><empty Class=\"Y\" /><text>value</text><done IsNull=\"True\" />");
        UpgradeContext context = Context(document);
        var plan = new EditPlan(context);
        plan.NullOut(Element(document, "deep"), NullForm.Reference);
        plan.NullOut(Element(document, "plain"), NullForm.Value);
        plan.NullOut(Element(document, "reference"), NullForm.Reference);
        plan.NullOut(Element(document, "empty"), NullForm.Reference);
        plan.NullOut(Element(document, "text"), NullForm.Value);
        plan.NullOut(Element(document, "done"), NullForm.Value);

        // 深层对象总是换成 IsNull 元素；引用与 Def 叶元素写文本 null；Scribe_Values 叶元素写 IsNull；已经是 IsNull 的元素不算改动。
        Assert.Equal(5, plan.Apply(context.Index));
        Assert.Equal(
            "<deep IsNull=\"True\" /><plain IsNull=\"True\" /><reference>null</reference><empty IsNull=\"True\" /><text IsNull=\"True\" /><done IsNull=\"True\" />",
            SaveXml.Game(document));
    }

    [Fact]
    public void EditsInsideRemovedSubtreesAreSkippedAndNotCounted()
    {
        XmlDocument document = SaveXml.Save("<list><li><a>1</a><b><c>2</c></b></li><li><a>3</a></li></list>");
        UpgradeContext context = Context(document);
        XmlElement first = (XmlElement)document.SelectSingleNode("//list/li[1]")!;
        var plan = new EditPlan(context);
        plan.SetText((XmlElement)first.SelectSingleNode("a")!, "9");
        plan.Rename((XmlElement)first.SelectSingleNode("b")!, "renamed");
        plan.Remove((XmlElement)first.SelectSingleNode("b/c")!);
        plan.NullOut(first, NullForm.Value);
        plan.SetText((XmlElement)document.SelectSingleNode("//list/li[2]/a")!, "4");

        Assert.Equal(2, plan.Apply(context.Index));
        Assert.Equal("<list><li IsNull=\"True\" /><li><a>4</a></li></list>", SaveXml.Game(document));
    }

    [Fact]
    public void SetText_RejectsElementsWithChildrenWhilePlanning()
    {
        XmlDocument document = SaveXml.Save("<owner><child>1</child></owner>");
        var plan = new EditPlan(Context(document));

        Assert.Throws<InvalidOperationException>(() => plan.SetText(Element(document, "owner"), "x"));
        Assert.Throws<XmlException>(() => plan.Rename(Element(document, "child"), "not valid"));
    }

    [Fact]
    public void Append_AddsElementWithTextAndNoOtherNodes()
    {
        XmlDocument document = SaveXml.Save("<owner><a>1</a></owner>");
        UpgradeContext context = Context(document);
        var plan = new EditPlan(context);
        plan.Append(Element(document, "owner"), "count", "3");
        plan.Apply(context.Index);

        Assert.Equal("<owner><a>1</a><count>3</count></owner>", SaveXml.Game(document));
        SaveXml.AssertNoMixedContent(document.DocumentElement!);
    }

    [Fact]
    public void RemovingTheSameNodeTwiceAppliesOnce()
    {
        XmlDocument document = SaveXml.Save("<list><li><a>1</a></li></list>");
        UpgradeContext context = Context(document);
        XmlElement item = Element(document, "list/li");
        var plan = new EditPlan(context);
        plan.Remove(item);
        plan.Remove(item);

        Assert.Equal(1, plan.Apply(context.Index));
        Assert.Equal("<list></list>", SaveXml.Game(document));
        Assert.False(context.IsLive(item));
    }

    [Fact]
    public void RemoveItem_RemovesFromRemovableContainers()
    {
        XmlDocument document = SaveXml.Save("<innerContainer><innerList><li><def>A</def></li><li><def>B</def></li></innerList></innerContainer>");
        UpgradeContext context = Context(document);
        var plan = new EditPlan(context);

        Assert.Equal(ItemRemoval.Removed, plan.RemoveItem((XmlElement)document.SelectSingleNode("//li[def='A']")!, NullForm.Value));
        plan.Apply(context.Index);
        Assert.Equal("<innerContainer><innerList><li><def>B</def></li></innerList></innerContainer>", SaveXml.Game(document));
    }

    [Fact]
    public void RemoveItem_RemovesDictionaryKeysPairwiseAndNullsValues()
    {
        XmlDocument document = SaveXml.Save("<dict><keys><li>K1</li><li>K2</li><li>K3</li></keys><values><li>V1</li><li>V2</li><li>V3</li></values></dict>");
        UpgradeContext context = Context(document);
        var plan = new EditPlan(context);

        Assert.Equal(ItemRemoval.DictionaryPair, plan.RemoveItem((XmlElement)document.SelectSingleNode("//keys/li[.='K2']")!, NullForm.Value));
        Assert.Equal(ItemRemoval.NulledOut, plan.RemoveItem((XmlElement)document.SelectSingleNode("//values/li[.='V3']")!, NullForm.Value));
        Assert.Equal(ItemRemoval.NulledOut, plan.RemoveItem((XmlElement)document.SelectSingleNode("//values/li[.='V1']")!, NullForm.Reference));
        plan.Apply(context.Index);

        Assert.Equal("<dict><keys><li>K1</li><li>K3</li></keys><values><li>null</li><li IsNull=\"True\" /></values></dict>", SaveXml.Game(document));
    }

    [Fact]
    public void RemoveItem_RemovesTheSameIndexFromEveryListOfAParallelGroup()
    {
        UpgradeTables tables = SaveXml.Tables(m => m.DeclareParallelGroup(OwnerSelector.Class("Mugirl.Punisher"), "punishers", "spawnTicks", "leaving"));
        XmlDocument document = SaveXml.Save(
            "<li Class=\"Mugirl.Punisher\"><punishers><li>Thing_P1</li><li>Thing_P2</li></punishers>"
            + "<spawnTicks><li>10</li><li>20</li></spawnTicks><leaving><li>False</li><li>True</li></leaving></li>");
        UpgradeContext context = Context(document, tables);
        var plan = new EditPlan(context);

        Assert.Equal(ItemRemoval.Parallel, plan.RemoveItem((XmlElement)document.SelectSingleNode("//punishers/li[2]")!, NullForm.Reference));
        plan.Apply(context.Index);

        Assert.Equal(
            "<li Class=\"Mugirl.Punisher\"><punishers><li>Thing_P1</li></punishers><spawnTicks><li>10</li></spawnTicks><leaving><li>False</li></leaving></li>",
            SaveXml.Game(document));
    }

    [Fact]
    public void RemoveItem_NullsOutItemsOfOtherLists()
    {
        XmlDocument document = SaveXml.Save("<parts><li Class=\"A\"><x>1</x></li><li>Thing_B2</li></parts>");
        UpgradeContext context = Context(document);
        var plan = new EditPlan(context);

        Assert.Equal(ItemRemoval.NulledOut, plan.RemoveItem((XmlElement)document.SelectSingleNode("//parts/li[1]")!, NullForm.Reference));
        Assert.Equal(ItemRemoval.NulledOut, plan.RemoveItem((XmlElement)document.SelectSingleNode("//parts/li[2]")!, NullForm.Reference));
        plan.Apply(context.Index);

        Assert.Equal("<parts><li IsNull=\"True\" /><li>null</li></parts>", SaveXml.Game(document));
    }

    [Fact]
    public void ModuleContainersAreRemovableByPathSuffix()
    {
        UpgradeTables tables = SaveXml.Tables(m => m.DeclareRemovableContainer("storyState/recentRandomIncidents", "StoryState list of defs."));
        XmlDocument document = SaveXml.Save("<storyState><recentRandomIncidents><li>A</li><li>B</li></recentRandomIncidents></storyState><other><recentRandomIncidents><li>A</li></recentRandomIncidents></other>");
        UpgradeContext context = Context(document, tables);
        var plan = new EditPlan(context);

        Assert.Equal(ItemRemoval.Removed, plan.RemoveItem((XmlElement)document.SelectSingleNode("//storyState/recentRandomIncidents/li[1]")!, NullForm.Value));
        Assert.Equal(ItemRemoval.NulledOut, plan.RemoveItem((XmlElement)document.SelectSingleNode("//other/recentRandomIncidents/li[1]")!, NullForm.Value));
    }

    private static UpgradeContext Context(XmlDocument document, UpgradeTables? tables = null)
    {
        var context = new UpgradeContext(SaveDocument.TryCreate(document)!, tables ?? SaveXml.Empty(), new TestCatalog(), new SessionData(), 0);
        context.BuildIndex([]);
        return context;
    }

    private static XmlElement Element(XmlDocument document, string name) => (XmlElement)document.SelectSingleNode("//" + name)!;
}
