using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 应用计划中的 DOM 编辑。结构性编辑（删除、改名、置空成 <c>IsNull</c> 元素）按父元素分组，每个父元素只重建一次子节点链表；
/// 其余编辑（属性、叶文本、引用置空、追加）随后按计划顺序应用，目标沿改名替换关系解析到当前元素。
/// </summary>
/// <remarks>
/// <para>
/// <c>XmlNode.RemoveChild</c> 与 <c>ReplaceChild</c> 要从第一个子节点开始查找前一个兄弟节点（子节点是单向循环链表），
/// 逐个删除大列表（地图物品、世界人物）中的项是 O(n²)：60 MB 存档删除一万多个物品要十几秒。删除首个子节点和追加到末尾都是 O(1)，
/// 所以重建一次是 O(n)。分组处理不改变结果：同一节点上删除优先于其他结构编辑；非结构编辑只作用于元素自身或其后代，
/// 与兄弟顺序无关；追加总在末尾。
/// </para>
/// <para>
/// 位于同一计划删除或置空的子树内的编辑没有意义，直接跳过，不计入生效数。
/// </para>
/// </remarks>
internal static class DomEditor
{
    /// <summary>应用 <paramref name="edits"/>，返回实际生效的编辑数。</summary>
    /// <param name="removed">实际删除或置空（换成 <c>IsNull</c> 元素）的原元素；它们的子树已离开文档，调用方据此收集失效的加载 ID。</param>
    internal static int Apply(IReadOnlyList<DomEdit> edits, SaveIndex index, List<XmlElement> removed)
    {
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(removed);
        int applied = DomReparenting.Apply(edits, index);
        var structural = new bool[edits.Count];
        var groups = new Dictionary<XmlNode, Dictionary<XmlElement, DomEdit>>();
        var parents = new List<XmlNode>();
        HashSet<XmlElement>? dropped = null;
        for (int i = 0; i < edits.Count; i++)
        {
            DomEdit edit = edits[i] with { Target = index.Resolve(edits[i].Target) };
            structural[i] = edit.Kind is DomEditKind.Remove or DomEditKind.Rename or DomEditKind.NullOut;
            if (!structural[i] || edit.Target.ParentNode is not { } parent)
            {
                continue;
            }

            if (edit.Kind is DomEditKind.Remove or DomEditKind.NullOut)
            {
                (dropped ??= []).Add(edit.Target);
            }

            if (!groups.TryGetValue(parent, out Dictionary<XmlElement, DomEdit>? group))
            {
                group = [];
                groups.Add(parent, group);
                parents.Add(parent);
            }

            if (!group.TryGetValue(edit.Target, out DomEdit existing) || (edit.Kind == DomEditKind.Remove && existing.Kind != DomEditKind.Remove))
            {
                group[edit.Target] = edit;
            }
        }

        foreach (XmlNode parent in parents)
        {
            XmlNode current = parent is XmlElement element ? index.Resolve(element) : parent;
            if (!IsInside(current, dropped))
            {
                applied += Rebuild(current, groups[parent], index, removed);
            }
        }

        for (int i = 0; i < edits.Count; i++)
        {
            if (structural[i] || edits[i].Kind is DomEditKind.AppendElement or DomEditKind.Move)
            {
                continue;
            }

            XmlElement target = index.Resolve(edits[i].Target);
            if (!IsInside(target, dropped) && ApplyInPlace(edits[i], target, index))
            {
                applied++;
            }
        }

        return applied;
    }

    /// <summary>元素是否有子元素（不是叶元素）。</summary>
    internal static bool HasElementChild(XmlElement element)
    {
        for (XmlNode? child = element.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child.NodeType == XmlNodeType.Element)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>深层对象：有子元素，或带 <c>Class</c>/<c>IsNull</c> 属性；置空时总是换成 <c>IsNull</c> 元素。</summary>
    internal static bool IsDeep(XmlElement element) =>
        HasElementChild(element) || element.HasAttribute("Class") || element.HasAttribute("IsNull");

    // 节点自身或某个祖先是本计划删除/置空的目标。
    private static bool IsInside(XmlNode node, HashSet<XmlElement>? dropped)
    {
        if (dropped is null)
        {
            return false;
        }

        for (XmlNode? current = node; current is not null; current = current.ParentNode)
        {
            if (current is XmlElement element && dropped.Contains(element))
            {
                return true;
            }
        }

        return false;
    }

    private static int Rebuild(XmlNode parent, Dictionary<XmlElement, DomEdit> group, SaveIndex index, List<XmlElement> removed)
    {
        var kept = new List<XmlNode>();
        int changed = 0;
        for (XmlNode? child = parent.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is not XmlElement element || !group.TryGetValue(element, out DomEdit edit))
            {
                kept.Add(child);
                continue;
            }

            switch (edit.Kind)
            {
                case DomEditKind.Remove:
                    removed.Add(element);
                    index.OnRemoved();
                    changed++;
                    break;
                case DomEditKind.Rename when element.Name != edit.Name:
                    XmlElement renamed = CopyWithName(element, edit.Name!);
                    kept.Add(renamed);
                    index.OnReplaced(element, renamed);
                    changed++;
                    break;
                case DomEditKind.NullOut when !IsNullElement(element):
                    // Scribe 对空对象写 IsNull="True"、不带子节点。
                    XmlElement empty = element.OwnerDocument.CreateElement(element.Name);
                    empty.SetAttribute("IsNull", "True");
                    kept.Add(empty);
                    removed.Add(element);
                    index.OnRemoved();
                    changed++;
                    break;
                default:
                    kept.Add(element);
                    break;
            }
        }

        if (changed == 0)
        {
            return 0;
        }

        while (parent.FirstChild is { } first)
        {
            parent.RemoveChild(first);
        }

        foreach (XmlNode node in kept)
        {
            parent.AppendChild(node);
        }

        return changed;
    }

    private static bool IsNullElement(XmlElement element) =>
        element.GetAttribute("IsNull") == "True" && !element.HasChildNodes && element.Attributes.Count == 1;

    private static bool ApplyInPlace(DomEdit edit, XmlElement target, SaveIndex index)
    {
        switch (edit.Kind)
        {
            case DomEditKind.SetAttribute:
                if (target.GetAttribute(edit.Name!) == edit.Value)
                {
                    return false;
                }

                target.SetAttribute(edit.Name!, edit.Value);
                if (edit.Name == "Class")
                {
                    index.OnClassChanged(target);
                }
                return true;
            case DomEditKind.SetText:
                if (HasElementChild(target) || target.InnerText == edit.Value)
                {
                    return false;
                }

                target.InnerText = edit.Value!;
                return true;
            case DomEditKind.NullReference:
                if (target.ParentNode is null || target.InnerText == "null")
                {
                    return false;
                }

                target.InnerText = "null";
                return true;
            case DomEditKind.Append:
                XmlElement created = target.OwnerDocument.CreateElement(edit.Name!);
                created.InnerText = edit.Value!;
                target.AppendChild(created);
                index.OnAppended(created);
                return true;
            default:
                throw new InvalidOperationException("Edit kind " + edit.Kind + " is structural and cannot be applied in place.");
        }
    }

    internal static XmlElement CopyWithName(XmlElement source, string name)
    {
        XmlElement created = source.OwnerDocument.CreateElement(name);
        foreach (XmlAttribute attribute in source.Attributes)
        {
            created.SetAttribute(attribute.Name, attribute.Value);
        }

        while (source.FirstChild is { } child)
        {
            created.AppendChild(child);
        }

        return created;
    }
}
