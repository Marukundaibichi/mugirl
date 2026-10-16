using System.Linq;
using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 决定删除一个列表项时能做什么：只有读取方能容忍缺项的容器可以直接删除；Scribe 字典的键与值成对删除；
/// 登记的并行列表按下标一起删除；其余位置一律置空以保留下标。
/// </summary>
internal sealed class ContainerRules
{
    /// <summary>
    /// 原版可移除容器（读取方在加载后移除空项、或按需重建缺失项，见 1.7.4977 反编译）：组件列表（<c>FillComponents</c> 重建缺失组件）、
    /// ThingOwner 的 <c>innerList</c>、地图物品、ThingFilter 的两个 HashSet、健康状态、账单、Lord、世界人物的三个 HashSet。
    /// </summary>
    internal static readonly IReadOnlyList<RemovableContainer> VanillaContainers =
    [
        Vanilla("game/components", "Game.FillComponents recreates missing components."),
        Vanilla("maps/li/components", "Map.FillComponents recreates missing components."),
        Vanilla("world/components", "World.FillComponents recreates missing components."),
        Vanilla("innerList", "ThingOwner list; PostLoadInit removes null entries."),
        Vanilla("maps/li/things", "Map things are independent objects."),
        Vanilla("allowedDefs", "ThingFilter HashSet<ThingDef>."),
        Vanilla("disallowedSpecialFilters", "ThingFilter HashSet<SpecialThingFilterDef>."),
        Vanilla("hediffSet/hediffs", "HediffSet removes null hediffs after loading."),
        Vanilla("billStack/bills", "BillStack removes null bills after loading."),
        Vanilla("lordManager/lords", "LordManager list of independent lords."),
        Vanilla("worldPawns/pawnsAlive", "WorldPawns HashSet<Pawn>."),
        Vanilla("worldPawns/pawnsMothballed", "WorldPawns HashSet<Pawn>."),
        Vanilla("worldPawns/pawnsDead", "WorldPawns HashSet<Pawn>."),
        Vanilla("worldPawns/pawnsForcefullyKeptAsWorldPawns", "WorldPawns HashSet<Pawn> of references; null entries log an error after loading."),
    ];

    private readonly IReadOnlyList<RemovableContainer> removable;
    private readonly IReadOnlyList<ParallelGroup> groups;

    internal ContainerRules(IReadOnlyList<RemovableContainer> removable, IReadOnlyList<ParallelGroup> groups)
    {
        this.removable = removable;
        this.groups = groups;
    }

    /// <summary>把删除 <paramref name="item"/> 所需的原语记入 <paramref name="plan"/>，返回采用的方式。</summary>
    /// <param name="leafForm">只能置空时叶元素的写法（见 <see cref="NullForm"/>）。</param>
    internal ItemRemoval PlanRemoval(XmlElement item, EditPlan plan, UpgradeContext context, NullForm leafForm)
    {
        if (item.ParentNode is not XmlElement container)
        {
            return ItemRemoval.Removed;
        }

        if (item.Name == "li" && container.Name is "keys" or "values" && container.ParentNode is XmlElement dictionary
            && dictionary["keys"] is { } keys && dictionary["values"] is { } values)
        {
            if (container != keys)
            {
                // 值所对应的键仍然有效；置空值比连键一起删除保守。
                plan.NullOut(item, leafForm);
                return ItemRemoval.NulledOut;
            }

            plan.Remove(item);
            if (ElementAt(values, IndexOf(keys, item)) is { } value)
            {
                plan.Remove(value);
            }

            return ItemRemoval.DictionaryPair;
        }

        if (item.Name == "li" && container.ParentNode is XmlElement owner)
        {
            foreach (ParallelGroup group in groups)
            {
                if (!group.Labels.Contains(container.Name, StringComparer.Ordinal) || !group.Owner.Matches(owner, context))
                {
                    continue;
                }

                int index = IndexOf(container, item);
                foreach (string label in group.Labels)
                {
                    if (owner[label] is { } list && ElementAt(list, index) is { } sibling)
                    {
                        plan.Remove(sibling);
                    }
                }

                return ItemRemoval.Parallel;
            }
        }

        if (IsRemovable(container))
        {
            plan.Remove(item);
            return ItemRemoval.Removed;
        }

        plan.NullOut(item, leafForm);
        return ItemRemoval.NulledOut;
    }

    /// <summary>删除 <paramref name="item"/> 是否不必置空：它是字典的键、登记并行组的项，或在可移除容器中。</summary>
    internal bool CanRemoveWithoutNulling(XmlElement item, UpgradeContext context)
    {
        if (item.ParentNode is not XmlElement container)
        {
            return false;
        }

        if (item.Name == "li" && container.Name == "keys" && container.ParentNode is XmlElement dictionary && dictionary["values"] is not null)
        {
            return true;
        }

        if (item.Name == "li" && container.ParentNode is XmlElement owner
            && groups.Any(group => group.Labels.Contains(container.Name, StringComparer.Ordinal) && group.Owner.Matches(owner, context)))
        {
            return true;
        }

        return IsRemovable(container);
    }

    /// <summary><paramref name="container"/> 是否匹配一个可移除容器路径。</summary>
    internal bool IsRemovable(XmlElement container)
    {
        foreach (RemovableContainer rule in removable)
        {
            XmlNode? node = container;
            bool matched = true;
            for (int i = rule.Path.Count - 1; i >= 0; i--)
            {
                if (node is not XmlElement element || element.Name != rule.Path[i])
                {
                    matched = false;
                    break;
                }

                node = element.ParentNode;
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    private static RemovableContainer Vanilla(string path, string reason) => new(path.Split('/'), "Framework", reason);

    private static int IndexOf(XmlElement container, XmlElement item)
    {
        int index = 0;
        for (XmlNode? node = container.FirstChild; node is not null; node = node.NextSibling)
        {
            if (node == item)
            {
                return index;
            }

            if (node.NodeType == XmlNodeType.Element)
            {
                index++;
            }
        }

        return -1;
    }

    private static XmlElement? ElementAt(XmlElement container, int index)
    {
        if (index < 0)
        {
            return null;
        }

        int current = 0;
        for (XmlNode? node = container.FirstChild; node is not null; node = node.NextSibling)
        {
            if (node is XmlElement element)
            {
                if (current == index)
                {
                    return element;
                }

                current++;
            }
        }

        return null;
    }
}
