using System.Linq;
using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>先完成保存结构重挂，再处理原容器删除；搬走的对象不属于被删除子树。</summary>
internal static class DomReparenting
{
    internal static int Apply(IReadOnlyList<DomEdit> edits, SaveIndex index)
    {
        DomEdit[] structural = [.. edits.Where(e => e.Kind is DomEditKind.AppendElement or DomEditKind.Move)];
        if (structural.Length == 0)
        {
            return 0;
        }

        Validate(structural, edits, index);
        var movedByParent = new Dictionary<XmlNode, HashSet<XmlElement>>();
        foreach (DomEdit edit in structural)
        {
            if (edit.Kind == DomEditKind.Move && index.Resolve(edit.Target) is { ParentNode: { } parent } source)
            {
                if (!movedByParent.TryGetValue(parent, out HashSet<XmlElement>? moved))
                {
                    movedByParent.Add(parent, moved = []);
                }

                moved.Add(source);
            }
        }

        // XmlNode.RemoveChild 随机位置是线性的；每个来源列表只重建一次。
        foreach ((XmlNode parent, HashSet<XmlElement> moved) in movedByParent)
        {
            List<XmlNode> kept = [.. parent.ChildNodes.Cast<XmlNode>().Where(n => n is not XmlElement element || !moved.Contains(element))];
            while (parent.FirstChild is { } child)
            {
                parent.RemoveChild(child);
            }

            foreach (XmlNode child in kept)
            {
                parent.AppendChild(child);
            }
        }

        foreach (DomEdit edit in structural)
        {
            if (edit.Kind == DomEditKind.AppendElement)
            {
                XmlElement created = edit.Related!;
                index.Resolve(edit.Target).AppendChild(created);
                index.OnAppended(created);
            }
            else
            {
                XmlElement source = index.Resolve(edit.Target);
                if (edit.Name is { } name && source.Name != name)
                {
                    XmlElement renamed = DomEditor.CopyWithName(source, name);
                    index.OnReplaced(source, renamed);
                    source = renamed;
                }

                index.Resolve(edit.Related!).AppendChild(source);
            }
        }

        return structural.Length;
    }

    private static void Validate(IReadOnlyList<DomEdit> structural, IReadOnlyList<DomEdit> edits, SaveIndex index)
    {
        var parents = new Dictionary<XmlElement, XmlElement>();
        var removed = new HashSet<XmlElement>(edits.Where(e => e.Kind is DomEditKind.Remove or DomEditKind.NullOut).Select(e => index.Resolve(e.Target)));
        foreach (DomEdit edit in structural)
        {
            XmlElement node = edit.Kind == DomEditKind.Move ? index.Resolve(edit.Target) : edit.Related!;
            XmlElement parent = index.Resolve(edit.Kind == DomEditKind.Move ? edit.Related! : edit.Target);
            if (!ReferenceEquals(node.OwnerDocument, parent.OwnerDocument)
                || node == node.OwnerDocument.DocumentElement?["game"] || !parents.TryAdd(node, parent))
            {
                throw new InvalidOperationException("A structural edit must move a node once within its original save document.");
            }
        }

        // 在第一项改动之前验证最终父链，包含尚未挂入的新容器和计划之间形成的环。
        foreach (XmlElement node in parents.Keys)
        {
            var seen = new HashSet<XmlElement>();
            XmlElement? current = node;
            while (current is not null)
            {
                if (!seen.Add(current) || removed.Contains(current))
                {
                    throw new InvalidOperationException("A structural edit creates a cycle or moves into a removed subtree.");
                }

                if (parents.TryGetValue(current, out XmlElement? parent))
                {
                    current = parent;
                }
                else if (current.ParentNode is XmlElement existing)
                {
                    current = existing;
                }
                else
                {
                    if (current.ParentNode is not XmlDocument)
                    {
                        throw new InvalidOperationException("A structural edit leaves data outside the save document.");
                    }

                    if (node.OwnerDocument.DocumentElement?["game"] is not { } game || !seen.Contains(game))
                    {
                        throw new InvalidOperationException("A structural edit leaves the game subtree.");
                    }

                    current = null;
                }
            }
        }
    }
}
