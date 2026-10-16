using System.Xml;
using System.Linq;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// MooGirl 时代（0.1.0–0.4.4，命名空间 <c>MooGirl</c>/<c>MooGirlSkinFix</c>，defName <c>MooGirl*</c>/<c>MuGirl*</c>）的内容不受支持：
/// 识别标记，删除其组件元素和可移除容器中的 def 引用，其余交给原版的缺失 Def 处理；玩家只收到一次说明。
/// </summary>
internal sealed class MooGirlEraStep : DomUpgradeStep
{
    internal const string StepId = "Framework.MooGirlEra";

    /// <summary>检测到 MooGirl 时代标记时写入会话数据的标志。</summary>
    internal const string DetectedFlag = "Framework.MooGirlEraDetected";

    internal MooGirlEraStep()
        : base(StepId, "MooGirl-era content has no successor; strip its components and its def references from removable containers.")
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        SaveIndex index = context.Index;
        HashSet<XmlElement> declared = [];
        foreach (ValueContext valueContext in context.Tables.ValueContexts.Where(value => value.HoldsDefNames))
        {
            foreach (XmlElement label in index.ElementsNamed(valueContext.Label))
            {
                if (label.ParentNode is XmlElement owner && valueContext.Owner.Matches(owner, context))
                {
                    declared.UnionWith(ValueLeaves.Of(label, valueContext.Part));
                }
            }
        }
        XmlElement[] leaves = [.. index.MooGirlLeaves.Where(leaf => declared.Contains(leaf) || IsKnownDefLeaf(leaf, context))];
        if (index.MooGirlClassElements.Count == 0 && leaves.Length == 0)
        {
            return;
        }

        plan.SessionFlags.Add(DetectedFlag);
        foreach (XmlElement element in index.MooGirlClassElements)
        {
            if (context.IsLive(element) && element.ParentNode is XmlElement { Name: "components" })
            {
                plan.Remove(element);
            }
        }

        foreach (XmlElement leaf in leaves)
        {
            if (leaf.Name == "li" && context.IsLive(leaf) && context.Containers.CanRemoveWithoutNulling(leaf, context))
            {
                plan.RemoveItem(leaf, NullForm.Reference);
            }
        }
    }

    private static bool IsKnownDefLeaf(XmlElement leaf, UpgradeContext context)
    {
        if (leaf.ParentNode is not XmlElement parent) { return false; }
        if (leaf.Name == "def")
        {
            return context.Index.TryGetThing(parent, out _) || context.Index.TryGetHediffDef(parent, out _);
        }
        if (leaf.Name != "li") { return false; }
        if (TypedDefReferenceScope.IsThingFilterList(parent, context)) { return true; }
        if (parent.Name == "recentRandomIncidents" && parent.ParentNode is XmlElement recentState) { return IsStoryState(recentState, context); }
        return parent.Name == "keys" && parent.ParentNode is XmlElement { Name: "lastFireTicks", ParentNode: XmlElement state }
            && IsStoryState(state, context);
    }

    private static bool IsStoryState(XmlElement state, UpgradeContext context)
    {
        if (state.GetAttribute("Class") is { Length: > 0 } name)
        {
            return name is "StoryState" or "RimWorld.StoryState";
        }
        if (state.Name != "storyState" || state.ParentNode is not XmlElement owner) { return false; }
        if (owner.Name == "world" && ReferenceEquals(owner.ParentNode, context.Document.Game))
        {
            return owner.GetAttribute("Class") is "" or "World" or "RimWorld.Planet.World";
        }
        return owner.Name == "li" && owner.GetAttribute("Class") is "" or "Map" or "Verse.Map"
            && owner.ParentNode is XmlElement { Name: "maps" } maps && !maps.HasAttribute("Class")
            && ReferenceEquals(maps.ParentNode, context.Document.Game);
    }
}
