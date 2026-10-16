using Mono.Cecil;

namespace MugirlRace.Validation.Metadata;

/// <summary>
/// Cecil 类型与成员的文档注释 id（<c>T:</c>、<c>M:</c>、<c>P:</c>、<c>F:</c>、<c>E:</c>），即 BannedSymbols.txt 使用的格式
/// （ECMA-334 附录 D，与 Roslyn <c>DocumentationCommentId</c> 输出一致）。
/// </summary>
internal static class DocIds
{
    public static string ForType(TypeReference type) => "T:" + DeclarationName(type);

    public static string ForMethod(MethodReference method)
    {
        MethodReference element = method is GenericInstanceMethod instance ? instance.ElementMethod : method;
        string name = element.Name.Replace('.', '#');
        string id = "M:" + DeclarationName(element.DeclaringType) + "." + name;
        if (element.HasGenericParameters)
        {
            id += "``" + element.GenericParameters.Count;
        }

        if (element.HasParameters)
        {
            id += "(" + string.Join(",", element.Parameters.Select(p => ParameterName(p.ParameterType))) + ")";
        }

        if (element.Name is "op_Implicit" or "op_Explicit")
        {
            id += "~" + ParameterName(element.ReturnType);
        }

        return id;
    }

    public static string ForField(FieldReference field) => "F:" + DeclarationName(field.DeclaringType) + "." + field.Name;

    public static string ForProperty(PropertyDefinition property)
    {
        string id = "P:" + DeclarationName(property.DeclaringType) + "." + property.Name;
        return property.HasParameters ? id + "(" + string.Join(",", property.Parameters.Select(p => ParameterName(p.ParameterType))) + ")" : id;
    }

    public static string ForEvent(EventDefinition @event) => "E:" + DeclarationName(@event.DeclaringType) + "." + @event.Name;

    /// <summary>带命名空间的名称，嵌套类型用 '.' 连接，保留泛型元数（<c>List`1</c>）。</summary>
    public static string DeclarationName(TypeReference type)
    {
        if (type is GenericInstanceType generic)
        {
            type = generic.ElementType;
        }

        if (type.DeclaringType is not null)
        {
            return DeclarationName(type.DeclaringType) + "." + type.Name;
        }

        return type.Namespace.Length == 0 ? type.Name : type.Namespace + "." + type.Name;
    }

    private static string ParameterName(TypeReference type) => type switch
    {
        GenericParameter parameter => (parameter.Type == GenericParameterType.Method ? "``" : "`") + parameter.Position,
        ByReferenceType reference => ParameterName(reference.ElementType) + "@",
        PointerType pointer => ParameterName(pointer.ElementType) + "*",
        ArrayType array => ParameterName(array.ElementType) + (array.IsVector ? "[]" : "[" + string.Join(",", Enumerable.Repeat("0:", array.Rank)) + "]"),
        GenericInstanceType generic => GenericInstanceName(generic),
        IModifierType modified => ParameterName(modified.ElementType),
        PinnedType pinned => ParameterName(pinned.ElementType),
        SentinelType sentinel => ParameterName(sentinel.ElementType),
        _ => DeclarationName(type),
    };

    private static string GenericInstanceName(GenericInstanceType generic)
    {
        string name = DeclarationName(generic.ElementType);
        int tick = name.LastIndexOf('`');
        if (tick >= 0)
        {
            name = name[..tick];
        }

        return name + "{" + string.Join(",", generic.GenericArguments.Select(ParameterName)) + "}";
    }
}
