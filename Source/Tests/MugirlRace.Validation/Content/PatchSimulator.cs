using System.Xml;
using System.Xml.XPath;

namespace MugirlRace.Validation.Content;

/// <summary>
/// 按加载顺序把每个内容包的 XML 补丁应用到统一 Def 文档，复刻 RimWorld 1.7 的原版补丁操作（<c>Verse.PatchOperation*</c>；语义取自
/// 1.7 反编译源码，包括 <c>success</c> 模式和只给 <c>nomatch</c> 时 <c>PatchOperationConditional</c> 的特殊结果）。补丁插入的节点
/// 会被记录，以便在其实际位置做类型检查。
/// </summary>
internal sealed class PatchSimulator
{
    private readonly XmlCorpus corpus;
    private readonly TypeResolver types;
    private readonly ActiveMods active;
    private readonly List<PatchResult> results = [];

    private PatchSimulator(XmlCorpus corpus, TypeResolver types, ActiveMods active)
    {
        this.corpus = corpus;
        this.types = types;
        this.active = active;
    }

    private enum Success
    {
        Normal,
        Invert,
        Always,
        Never,
    }

    /// <summary>每个顶层 <c>&lt;Operation&gt;</c> 的结果。</summary>
    public IReadOnlyList<PatchResult> Results => results;

    public static PatchSimulator ApplyAll(XmlCorpus corpus, TypeResolver types, ActiveMods active)
    {
        var simulator = new PatchSimulator(corpus, types, active);
        foreach ((ContentPack pack, LoadedFile file) in corpus.PatchFiles)
        {
            simulator.ApplyFile(pack, file.FullPath);
        }

        return simulator;
    }

    private void ApplyFile(ContentPack pack, string file)
    {
        XmlDocument? document = corpus.TryParse(pack, file);
        if (document?.DocumentElement is not { } root)
        {
            return;
        }

        if (root.Name != "Patch")
        {
            results.Add(new PatchResult(pack, file, $"<{root.Name}>", Succeeded: false, "Unexpected document element; expected <Patch> (the game skips the file)."));
            return;
        }

        foreach (XmlElement element in root.ChildNodes.OfType<XmlElement>())
        {
            if (element.Name != "Operation")
            {
                results.Add(new PatchResult(pack, file, $"<{element.Name}>", Succeeded: false, "Unexpected element in patch XML; expected <Operation>."));
                continue;
            }

            var context = new Context(pack, file);
            string description = Describe(element);
            bool succeeded;
            try
            {
                succeeded = Apply(element, context);
            }
            catch (XPathException exception)
            {
                context.Errors.Add("Invalid XPath: " + exception.Message);
                succeeded = false;
            }

            results.Add(new PatchResult(pack, file, description, succeeded, context.Errors.Count == 0 ? null : string.Join(" ", context.Errors)));
        }
    }

    // PatchOperation.Apply：先 ApplyWorker，再按 success 模式处理。
    private bool Apply(XmlElement operation, Context context)
    {
        bool result = ApplyWorker(operation, context);
        return SuccessOf(operation, context) switch
        {
            Success.Always => true,
            Success.Never => false,
            Success.Invert => !result,
            _ => result,
        };
    }

    private bool ApplyWorker(XmlElement operation, Context context)
    {
        string? className = operation.GetAttribute("Class");
        Type? type = string.IsNullOrEmpty(className) ? null : types.GetTypeInAnyAssembly(className, "Verse");
        if (type is null)
        {
            context.Errors.Add($"Class '{className}' of a patch operation does not resolve to a type.");
            return false;
        }

        XmlDocument xml = corpus.Document;
        switch (type.FullName)
        {
            case "Verse.PatchOperationSequence":
                foreach (XmlElement item in Child(operation, "operations")?.ChildNodes.OfType<XmlElement>() ?? [])
                {
                    if (!MayRequireSatisfied(item))
                    {
                        continue;
                    }

                    if (!Apply(item, context))
                    {
                        return false;
                    }
                }

                return true;
            case "Verse.PatchOperationAdd":
            {
                XmlElement? value = Child(operation, "value");
                bool prepend = Text(operation, "order") == "Prepend";
                bool result = false;
                foreach (XmlNode target in Select(xml, operation, context))
                {
                    result = true;
                    if (value is null)
                    {
                        continue;
                    }

                    if (prepend)
                    {
                        for (int i = value.ChildNodes.Count - 1; i >= 0; i--)
                        {
                            Insert(context, target, xml.ImportNode(value.ChildNodes[i]!, deep: true), (parent, node) => parent.PrependChild(node));
                        }
                    }
                    else
                    {
                        foreach (XmlNode child in value.ChildNodes)
                        {
                            Insert(context, target, xml.ImportNode(child, deep: true), (parent, node) => parent.AppendChild(node));
                        }
                    }
                }

                return result;
            }

            case "Verse.PatchOperationInsert":
            {
                XmlElement? value = Child(operation, "value");
                bool append = Text(operation, "order") == "Append";
                bool result = false;
                foreach (XmlNode target in Select(xml, operation, context))
                {
                    result = true;
                    XmlNode parent = target.ParentNode!;
                    if (value is null)
                    {
                        continue;
                    }

                    if (append)
                    {
                        foreach (XmlNode child in value.ChildNodes)
                        {
                            Insert(context, parent, xml.ImportNode(child, deep: true), (p, node) => p.InsertAfter(node, target));
                        }
                    }
                    else
                    {
                        for (int i = value.ChildNodes.Count - 1; i >= 0; i--)
                        {
                            Insert(context, parent, xml.ImportNode(value.ChildNodes[i]!, deep: true), (p, node) => p.InsertBefore(node, target));
                        }
                    }

                    if (target.NodeType == XmlNodeType.Text)
                    {
                        parent.Normalize();
                    }
                }

                return result;
            }

            case "Verse.PatchOperationReplace":
            {
                XmlElement? value = Child(operation, "value");
                bool result = false;
                foreach (XmlNode target in Select(xml, operation, context).ToArray())
                {
                    result = true;
                    XmlNode parent = target.ParentNode!;
                    foreach (XmlNode child in value?.ChildNodes.Cast<XmlNode>() ?? [])
                    {
                        Insert(context, parent, xml.ImportNode(child, deep: true), (p, node) => p.InsertBefore(node, target));
                    }

                    parent.RemoveChild(target);
                }

                return result;
            }

            case "Verse.PatchOperationRemove":
            {
                bool result = false;
                foreach (XmlNode target in Select(xml, operation, context).ToArray())
                {
                    result = true;
                    target.ParentNode!.RemoveChild(target);
                }

                return result;
            }

            case "Verse.PatchOperationAttributeAdd":
            case "Verse.PatchOperationAttributeSet":
            case "Verse.PatchOperationAttributeRemove":
                return ApplyAttribute(type.FullName, operation, context);
            case "Verse.PatchOperationAddModExtension":
            {
                XmlElement? value = Child(operation, "value");
                bool result = false;
                foreach (XmlNode target in Select(xml, operation, context))
                {
                    XmlNode extensions = target["modExtensions"] ?? target.AppendChild(xml.CreateElement("modExtensions"))!;
                    foreach (XmlNode child in value?.ChildNodes.Cast<XmlNode>() ?? [])
                    {
                        Insert(context, extensions, xml.ImportNode(child, deep: true), (p, node) => p.AppendChild(node));
                    }

                    result = true;
                }

                return result;
            }

            case "Verse.PatchOperationSetName":
            {
                string name = Text(operation, "name") ?? "";
                bool result = false;
                foreach (XmlNode target in Select(xml, operation, context).ToArray())
                {
                    result = true;
                    XmlElement renamed = xml.CreateElement(name);
                    renamed.InnerXml = target.InnerXml;
                    target.ParentNode!.InsertBefore(renamed, target);
                    target.ParentNode!.RemoveChild(target);
                }

                return result;
            }

            case "Verse.PatchOperationConditional":
            {
                XmlElement? match = Child(operation, "match");
                XmlElement? nomatch = Child(operation, "nomatch");
                if (SelectSingle(xml, operation, context) is not null)
                {
                    if (match is not null)
                    {
                        return Apply(match, context);
                    }
                }
                else if (nomatch is not null)
                {
                    return Apply(nomatch, context);
                }

                return match is null ? nomatch is not null : true;
            }

            case "Verse.PatchOperationTest":
                return SelectSingle(xml, operation, context) is not null;
            case "Verse.PatchOperationFindMod":
            {
                bool found = (Child(operation, "mods")?.ChildNodes.OfType<XmlElement>() ?? []).Any(li => active.HasModNamed(li.InnerText));
                XmlElement? match = Child(operation, "match");
                XmlElement? nomatch = Child(operation, "nomatch");
                if (found)
                {
                    if (match is not null)
                    {
                        return Apply(match, context);
                    }
                }
                else if (nomatch is not null)
                {
                    return Apply(nomatch, context);
                }

                return true;
            }

            default:
                context.Errors.Add($"Patch operation {type.FullName} is not a vanilla operation; the suite cannot simulate it.");
                return false;
        }
    }

    private bool ApplyAttribute(string kind, XmlElement operation, Context context)
    {
        string attribute = Text(operation, "attribute") ?? "";
        string value = Text(operation, "value") ?? "";
        bool result = false;
        foreach (XmlNode target in Select(corpus.Document, operation, context))
        {
            XmlAttribute? existing = target.Attributes?[attribute];
            switch (kind)
            {
                case "Verse.PatchOperationAttributeAdd" when existing is null:
                    XmlAttribute added = corpus.Document.CreateAttribute(attribute);
                    added.Value = value;
                    target.Attributes!.Append(added);
                    result = true;
                    break;
                case "Verse.PatchOperationAttributeSet":
                    if (existing is not null)
                    {
                        existing.Value = value;
                    }
                    else
                    {
                        XmlAttribute set = corpus.Document.CreateAttribute(attribute);
                        set.Value = value;
                        target.Attributes!.Append(set);
                    }

                    result = true;
                    break;
                case "Verse.PatchOperationAttributeRemove" when existing is not null:
                    target.Attributes!.Remove(existing);
                    result = true;
                    break;
            }
        }

        return result;
    }

    private void Insert(Context context, XmlNode parent, XmlNode node, Func<XmlNode, XmlNode, XmlNode?> insert)
    {
        insert(parent, node);
        corpus.MarkInserted(node, context.Pack, context.File);
    }

    private static IEnumerable<XmlNode> Select(XmlDocument xml, XmlElement operation, Context context)
    {
        string? xpath = Text(operation, "xpath");
        if (string.IsNullOrEmpty(xpath))
        {
            context.Errors.Add("Missing <xpath>.");
            return [];
        }

        return xml.SelectNodes(xpath)?.Cast<XmlNode>() ?? [];
    }

    private static XmlNode? SelectSingle(XmlDocument xml, XmlElement operation, Context context)
    {
        string? xpath = Text(operation, "xpath");
        if (string.IsNullOrEmpty(xpath))
        {
            context.Errors.Add("Missing <xpath>.");
            return null;
        }

        return xml.SelectSingleNode(xpath);
    }

    private static Success SuccessOf(XmlElement operation, Context context)
    {
        string? text = Text(operation, "success");
        if (text is null)
        {
            return Success.Normal;
        }

        if (Enum.TryParse(text, ignoreCase: false, out Success success))
        {
            return success;
        }

        context.Errors.Add($"Unknown success mode '{text}'.");
        return Success.Normal;
    }

    private bool MayRequireSatisfied(XmlElement item)
    {
        string mayRequire = item.GetAttribute("MayRequire");
        string mayRequireAnyOf = item.GetAttribute("MayRequireAnyOf");
        return (mayRequire.Length == 0 || mayRequire.Split(',').All(active.IsActive))
            && (mayRequireAnyOf.Length == 0 || mayRequireAnyOf.Split(',').Any(active.IsActive));
    }

    private static XmlElement? Child(XmlElement element, string name) => element[name];

    private static string? Text(XmlElement element, string name) => element[name]?.InnerText.Trim();

    private static string Describe(XmlElement operation)
    {
        string? xpath = Text(operation, "xpath");
        return operation.GetAttribute("Class") + (xpath is null ? "" : $"({xpath})");
    }

    private sealed class Context
    {
        public Context(ContentPack pack, string file)
        {
            Pack = pack;
            File = file;
        }

        public ContentPack Pack { get; }

        public string File { get; }

        public List<string> Errors { get; } = [];
    }
}
