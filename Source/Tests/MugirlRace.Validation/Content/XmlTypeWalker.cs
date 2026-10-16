using System.Reflection;
using System.Xml;
using Verse;

namespace MugirlRace.Validation.Content;

/// <summary>
/// 按 RimWorld 1.7 默认加载器（<c>DirectXmlToObjectNew</c>）的方式遍历 Def XML，报告游戏会拒绝的内容：无法解析或不能赋给声明类型
/// 的 <c>Class</c>（1.7 加载器抛出异常并丢弃整个 Def）、没有对应字段的元素（<c>XmlToObjectUtils.DoFieldSearch</c>）、只靠忽略大小写
/// 才匹配的字段，以及无法解析的 <c>Type</c> 值。回调把带类型的对象与字符串字段提供给贴图、Shader 等检查。
/// </summary>
internal sealed class XmlTypeWalker
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly TypeResolver types;
    private readonly ActiveMods active;

    public XmlTypeWalker(TypeResolver types, ActiveMods active)
    {
        this.types = types;
        this.active = active;
    }

    /// <summary>游戏会记为 XML 错误或异常的问题（节点、消息）。</summary>
    public Action<XmlElement, string>? Problem { get; set; }

    /// <summary>解析不到任何类型的 <c>Class</c> 或 <c>Type</c> 值（节点、消息）；未设置时经 <see cref="Problem"/> 报告。</summary>
    public Action<XmlElement, string>? UnresolvedType { get; set; }

    /// <summary>解析成功的 <c>Type</c> 字段（节点、字段或列表项时为 null、类型）。</summary>
    public Action<XmlElement, FieldInfo?, Type>? TypeValue { get; set; }

    /// <summary>复合对象（节点、运行时类型）。</summary>
    public Action<XmlElement, Type>? ObjectVisited { get; set; }

    /// <summary>字符串字段或字符串列表项（节点、字段、值）。</summary>
    public Action<XmlElement, FieldInfo, string>? StringValue { get; set; }

    /// <summary>Def 交叉引用（节点、字段或 null、Def 类型、defName）。</summary>
    public Action<XmlElement, FieldInfo?, Type, string>? DefReference { get; set; }

    /// <summary>由类型自己的 <c>LoadDataFromXmlCustom</c> 读取的节点（节点、类型），例如 <c>costList</c> 中的 <c>ThingDefCountClass</c>。</summary>
    public Action<XmlElement, Type>? CustomLoaded { get; set; }

    /// <summary>
    /// 遍历顶层 Def 节点。对抽象节点，自身类型缺少但继承它的类型（<paramref name="inheritorTypes"/>）声明了的字段视为合法。
    /// </summary>
    public void WalkDef(XmlElement node, IReadOnlyCollection<Type> inheritorTypes)
    {
        Type? type = DefTypeOf(node);
        if (type is null)
        {
            string text = node.GetAttribute("Class") is { Length: > 0 } cls ? cls : node.Name;
            Problem?.Invoke(node, $"Type {text} is not a Def type or could not be found; the game skips this def.");
            return;
        }

        VisitObject(node, type, inheritorTypes);
    }

    /// <summary>把 <paramref name="node"/> 当作 <paramref name="declared"/>（字段或列表元素类型）的值遍历。</summary>
    public void Walk(XmlElement node, Type declared, FieldInfo? field)
    {
        if (node.GetAttribute("IsNull").Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Type type = declared;
        string className = node.GetAttribute("Class");
        if (className.Length > 0)
        {
            Type inner = Nullable.GetUnderlyingType(declared) ?? declared;
            Type? resolved = types.GetTypeInAnyAssembly(className, inner.Namespace);
            if (resolved is null)
            {
                (UnresolvedType ?? Problem)?.Invoke(node, $"Class '{className}' does not resolve to a type; RimWorld 1.7's loader throws and drops the whole Def.");
                return;
            }

            if (!inner.IsAssignableFrom(resolved))
            {
                Problem?.Invoke(node, $"Class '{className}' resolves to {resolved.FullName}, which is not a {inner.FullName}; the Def fails to load.");
                return;
            }

            type = resolved;
        }

        Dispatch(node, type, field);
    }

    /// <summary>顶层节点的 Def 类型：<c>Class</c> 特性，否则为元素名。</summary>
    public Type? DefTypeOf(XmlElement node)
    {
        string text = node.GetAttribute("Class") is { Length: > 0 } cls ? cls : node.Name;
        Type? type = types.GetTypeInAnyAssembly(text);
        return type is not null && typeof(Def).IsAssignableFrom(type) ? type : null;
    }

    /// <summary>
    /// <paramref name="element"/> 在其位置上的声明类型（字段类型或列表元素类型），从顶层 Def 向下计算；顶层节点或无法确定时为
    /// <see langword="null"/>。
    /// </summary>
    public (Type? Declared, FieldInfo? Field) DeclaredTypeOf(XmlElement element, XmlElement root)
    {
        if (element.ParentNode is not XmlElement parent || parent == root)
        {
            return (null, null);
        }

        Type? parentType = EffectiveTypeOf(parent, root);
        if (parentType is null)
        {
            return (null, null);
        }

        Type inner = Nullable.GetUnderlyingType(parentType) ?? parentType;
        if (GenTypes.IsList(inner))
        {
            return (inner.GetGenericArguments()[0], null);
        }

        if (IsLeafType(inner) || GenTypes.IsDictionary(inner))
        {
            return (null, null);
        }

        FieldInfo? field = FindField(inner, element, out _);
        return (field?.FieldType, field);
    }

    /// <summary>
    /// <c>XmlToObjectUtils.DoFieldSearch</c> 的复刻：沿类型层次按精确名查找，再忽略大小写（除非类型有 <c>[CaseInsensitiveXMLParsing]</c>
    /// 否则报错），再按 <c>[LoadAlias]</c>（字段为 <c>[Unsaved]</c> 时报错）；都找不到时报错，除非节点有
    /// <c>IgnoreIfNoMatchingField="True"</c> 或类型对它有 <c>[IgnoreSavedElement]</c>。
    /// </summary>
    public static FieldInfo? FindField(Type type, XmlElement node, out string? problem)
    {
        problem = null;
        string name = node.Name;
        FieldInfo? field = SearchHierarchy(type, name, ignoreCase: false);
        if (field is not null)
        {
            return field;
        }

        field = SearchHierarchy(type, name, ignoreCase: true);
        if (field is not null)
        {
            if (!Attribute.IsDefined(type, typeof(CaseInsensitiveXMLParsing), inherit: true))
            {
                problem = $"<{name}> refers to field {field.Name} of {type.Name} only by ignoring case; XML tags are case-sensitive (the game logs an error).";
            }

            return field;
        }

        foreach (FieldInfo candidate in type.GetFields(InstanceFields))
        {
            if (candidate.GetCustomAttributes<LoadAliasAttribute>(inherit: true).Any(alias => string.Equals(alias.alias, name, StringComparison.OrdinalIgnoreCase)))
            {
                field = candidate;
                break;
            }
        }

        if (field is not null)
        {
            if (field.GetCustomAttribute<UnsavedAttribute>() is { allowLoading: false })
            {
                problem = $"<{name}> maps to field {field.Name} of {type.Name}, which is [Unsaved] and cannot be loaded.";
                return null;
            }

            return field;
        }

        bool ignored = node.GetAttribute("IgnoreIfNoMatchingField").Equals("true", StringComparison.OrdinalIgnoreCase)
            || type.GetCustomAttributes<IgnoreSavedElementAttribute>(inherit: true).Any(a => string.Equals(a.elementToIgnore, name, StringComparison.OrdinalIgnoreCase));
        if (!ignored)
        {
            problem = $"<{name}> doesn't correspond to any field in type {type.Name} (the game logs an XML error).";
        }

        return null;
    }

    private Type? EffectiveTypeOf(XmlElement element, XmlElement root)
    {
        if (element.ParentNode == root)
        {
            return DefTypeOf(element);
        }

        (Type? declared, _) = DeclaredTypeOf(element, root);
        string className = element.GetAttribute("Class");
        if (declared is null || className.Length == 0)
        {
            return declared;
        }

        Type inner = Nullable.GetUnderlyingType(declared) ?? declared;
        return types.GetTypeInAnyAssembly(className, inner.Namespace);
    }

    private void Dispatch(XmlElement node, Type type, FieldInfo? field)
    {
        Type inner = Nullable.GetUnderlyingType(type) ?? type;
        if (inner == typeof(string))
        {
            if (field is not null)
            {
                StringValue?.Invoke(node, field, node.InnerText);
            }

            return;
        }

        if (XmlToObjectUtils.CustomDataLoadMethodOf(inner) is not null)
        {
            CustomLoaded?.Invoke(node, inner);
            return;
        }

        if (GenTypes.IsSlateRef(inner) || GenTypes.HasFlagsAttribute(inner))
        {
            return;
        }

        if (GenTypes.IsList(inner))
        {
            Type item = inner.GetGenericArguments()[0];
            foreach (XmlElement child in node.ChildNodes.OfType<XmlElement>())
            {
                if (!DefIndex.MayRequireSatisfied(child, active))
                {
                    continue;
                }

                if (item == typeof(string))
                {
                    if (field is not null)
                    {
                        StringValue?.Invoke(child, field, child.InnerText);
                    }
                }
                else if (item == typeof(Type))
                {
                    CheckTypeValue(child, field);
                }
                else if (GenTypes.IsDef(item))
                {
                    DefReference?.Invoke(child, field, item, child.InnerText.Trim());
                }
                else
                {
                    Walk(child, item, null);
                }
            }

            return;
        }

        if (GenTypes.IsDictionary(inner))
        {
            Type value = inner.GetGenericArguments()[1];
            if (!IsLeafType(value) && !GenTypes.IsDef(value))
            {
                foreach (XmlElement entry in node.ChildNodes.OfType<XmlElement>())
                {
                    if (entry["value"] is { } valueNode)
                    {
                        Walk(valueNode, value, null);
                    }
                }
            }

            return;
        }

        if (inner == typeof(Type))
        {
            CheckTypeValue(node, field);
            return;
        }

        if (GenTypes.IsDef(inner))
        {
            DefReference?.Invoke(node, field, inner, node.InnerText.Trim());
            return;
        }

        if (IsLeafType(inner))
        {
            return;
        }

        VisitObject(node, inner, []);
    }

    private void VisitObject(XmlElement node, Type type, IReadOnlyCollection<Type> alternates)
    {
        ObjectVisited?.Invoke(node, type);
        foreach (XmlElement child in node.ChildNodes.OfType<XmlElement>())
        {
            FieldInfo? field = FindField(type, child, out string? problem);
            if (field is null && alternates.Count > 0)
            {
                foreach (Type alternate in alternates)
                {
                    if (FindField(alternate, child, out string? alternateProblem) is { } found && alternateProblem is null)
                    {
                        field = found;
                        problem = null;
                        break;
                    }
                }
            }

            if (problem is not null)
            {
                Problem?.Invoke(child, problem);
            }

            if (field is not null)
            {
                Walk(child, field.FieldType, field);
            }
        }
    }

    // ParseHelper.FromString(str, typeof(Type)) = ParseType："null" 允许，其他值必须能解析。
    private void CheckTypeValue(XmlElement node, FieldInfo? field)
    {
        string text = node.InnerText.Trim();
        if (text.Length == 0 || text is "null" or "Null")
        {
            return;
        }

        Type? resolved = types.GetTypeInAnyAssembly(text);
        if (resolved is null)
        {
            (UnresolvedType ?? Problem)?.Invoke(node, $"Could not find a type named {text} (the field stays null and the game logs an error).");
            return;
        }

        TypeValue?.Invoke(node, field, resolved);
    }

    private static bool IsLeafType(Type type)
        => type.IsPrimitive || type.IsEnum || type == typeof(string) || ParseHelper.HandlesType(type) || XmlToObjectUtils.CustomDataLoadMethodOf(type) is not null || GenTypes.IsSlateRef(type);

    private static FieldInfo? SearchHierarchy(Type type, string name, bool ignoreCase)
    {
        BindingFlags flags = InstanceFields | (ignoreCase ? BindingFlags.IgnoreCase : 0);
        for (Type? current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (current.GetField(name, flags) is { } field)
            {
                return field;
            }
        }

        return null;
    }
}
