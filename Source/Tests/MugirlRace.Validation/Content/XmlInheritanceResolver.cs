using System.Xml;

namespace MugirlRace.Validation.Content;

/// <summary>
/// 针对统一 Def 文档的 <c>Verse.XmlInheritance</c> 复刻：<c>Name</c>/<c>ParentName</c> 注册（跳过 <c>MayRequire</c> 不满足的节点）、
/// 按加载顺序选择最佳父节点，以及 <c>RecursiveNodeCopyOverwriteElements</c>（子节点特性替换父节点的；<c>li</c> 项追加；
/// <c>Inherit="False"</c> 替换整个元素）。
/// </summary>
internal sealed class XmlInheritanceResolver
{
    private readonly Dictionary<string, List<(XmlElement Node, int Order)>> byName = new(StringComparer.Ordinal);
    private readonly Dictionary<XmlElement, int> order = [];
    private readonly Dictionary<string, List<XmlElement>> childrenByParentName = new(StringComparer.Ordinal);
    private readonly Dictionary<XmlElement, XmlElement> resolved = [];
    private readonly HashSet<XmlElement> resolving = [];

    public XmlInheritanceResolver(XmlCorpus corpus, ModCatalog catalog, ActiveMods active)
    {
        foreach (XmlElement node in corpus.Defs)
        {
            ContentPack? pack = corpus.SourceOf(node)?.Pack;
            int loadOrder = pack is null ? -1 : IndexOf(catalog.LoadOrder, pack);
            order[node] = loadOrder;
            string parentName = node.GetAttribute("ParentName");
            if (parentName.Length > 0)
            {
                if (!childrenByParentName.TryGetValue(parentName, out List<XmlElement>? children))
                {
                    childrenByParentName[parentName] = children = [];
                }

                children.Add(node);
            }

            string name = node.GetAttribute("Name");
            if (name.Length == 0 || !DefIndex.MayRequireSatisfied(node, active))
            {
                continue;
            }

            if (!byName.TryGetValue(name, out List<(XmlElement, int)>? list))
            {
                byName[name] = list = [];
            }

            list.Add((node, loadOrder));
        }
    }

    /// <summary>已注册名为 <paramref name="name"/> 的节点时为 true。</summary>
    public bool IsRegistered(string name) => byName.ContainsKey(name);

    /// <summary>游戏会为 <paramref name="node"/> 选择的父节点，或 <see langword="null"/>。</summary>
    public XmlElement? ParentOf(XmlElement node)
    {
        string parentName = node.GetAttribute("ParentName");
        if (parentName.Length == 0 || !byName.TryGetValue(parentName, out List<(XmlElement Node, int Order)>? candidates))
        {
            return null;
        }

        int own = order.GetValueOrDefault(node, int.MaxValue);
        (XmlElement Node, int Order)? best = null;
        foreach ((XmlElement candidate, int candidateOrder) in candidates)
        {
            if (candidate != node && candidateOrder <= own && (best is null || candidateOrder > best.Value.Order))
            {
                best = (candidate, candidateOrder);
            }
        }

        return best?.Node ?? candidates.FirstOrDefault(c => c.Node != node).Node;
    }

    /// <summary>直接或间接以 <paramref name="node"/> 为父节点的节点。</summary>
    public IEnumerable<XmlElement> Descendants(XmlElement node)
    {
        string name = node.GetAttribute("Name");
        if (name.Length == 0)
        {
            yield break;
        }

        var seen = new HashSet<XmlElement>();
        var pending = new Queue<XmlElement>(childrenByParentName.GetValueOrDefault(name) ?? []);
        while (pending.Count > 0)
        {
            XmlElement child = pending.Dequeue();
            if (!seen.Add(child))
            {
                continue;
            }

            yield return child;
            string childName = child.GetAttribute("Name");
            if (childName.Length > 0)
            {
                foreach (XmlElement grandchild in childrenByParentName.GetValueOrDefault(childName) ?? [])
                {
                    pending.Enqueue(grandchild);
                }
            }
        }
    }

    /// <summary>合并了全部继承内容的节点（分离的副本；不要修改）。</summary>
    public XmlElement Resolve(XmlElement node)
    {
        if (resolved.TryGetValue(node, out XmlElement? cached))
        {
            return cached;
        }

        XmlElement? parent = ParentOf(node);
        XmlElement result;
        if (parent is null || !resolving.Add(node))
        {
            result = node;
        }
        else
        {
            result = (XmlElement)Resolve(parent).CloneNode(deep: true);
            CopyOverwrite(node, result);
            resolving.Remove(node);
        }

        resolved[node] = result;
        return result;
    }

    // XmlInheritance.RecursiveNodeCopyOverwriteElements。
    private static void CopyOverwrite(XmlNode child, XmlNode current)
    {
        XmlDocument document = current.OwnerDocument!;
        if (child.Attributes?["Inherit"] is { } inherit && inherit.Value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            while (current.FirstChild is { } first)
            {
                current.RemoveChild(first);
            }

            foreach (XmlNode item in child)
            {
                current.AppendChild(document.ImportNode(item, deep: true));
            }

            foreach (XmlAttribute attribute in child.Attributes!)
            {
                if (attribute.Name != "Inherit")
                {
                    XmlAttribute copy = document.CreateAttribute(attribute.Name);
                    copy.Value = attribute.Value;
                    current.Attributes!.Append(copy);
                }
            }

            return;
        }

        current.Attributes?.RemoveAll();
        foreach (XmlAttribute attribute in child.Attributes?.Cast<XmlAttribute>() ?? [])
        {
            current.Attributes!.Append((XmlAttribute)document.ImportNode(attribute, deep: true));
        }

        XmlNode? text = child.ChildNodes.Cast<XmlNode>().LastOrDefault(n => n.NodeType == XmlNodeType.Text);
        bool hasElements = child.ChildNodes.Cast<XmlNode>().Any(n => n.NodeType == XmlNodeType.Element);
        if (text is not null)
        {
            foreach (XmlNode existing in current.ChildNodes.Cast<XmlNode>().ToList())
            {
                current.RemoveChild(existing);
            }

            current.AppendChild(document.ImportNode(text, deep: true));
            return;
        }

        if (!hasElements)
        {
            if (!current.ChildNodes.Cast<XmlNode>().Any(n => n.NodeType == XmlNodeType.Element))
            {
                foreach (XmlNode existing in current.ChildNodes.Cast<XmlNode>().ToList())
                {
                    current.RemoveChild(existing);
                }
            }

            return;
        }

        foreach (XmlNode item in child)
        {
            if (item.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (item.Name == "li")
            {
                current.AppendChild(document.ImportNode(item, deep: true));
                continue;
            }

            if (current[item.Name] is { } existing)
            {
                CopyOverwrite(item, existing);
            }
            else
            {
                current.AppendChild(document.ImportNode(item, deep: true));
            }
        }
    }

    private static int IndexOf(IReadOnlyList<ContentPack> packs, ContentPack pack)
    {
        for (int i = 0; i < packs.Count; i++)
        {
            if (packs[i] == pack)
            {
                return i;
            }
        }

        return -1;
    }
}
