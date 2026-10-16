using Mono.Cecil;

namespace MugirlRace.Validation.Metadata;

/// <summary>Cecil 类型与成员的稳定可读名称（嵌套类型用 <c>+</c> 连接，与反射一致）。</summary>
internal static class CecilNames
{
    /// <summary>完整类型名，例如 <c>UnityEngine.Color</c> 或 <c>Mugirl.Outer+Inner</c>。</summary>
    public static string Type(TypeReference type) => type.FullName.Replace('/', '+');

    /// <summary>方法为 <c>Owner::Name(ParameterTypes)</c>，字段为 <c>Owner::Name</c>。</summary>
    public static string Member(MemberReference member) => member switch
    {
        MethodReference method => $"{Type(method.DeclaringType)}::{method.Name}{Arity(method)}({string.Join(",", method.Parameters.Select(p => Type(p.ParameterType)))})",
        _ => $"{Type(member.DeclaringType)}::{member.Name}",
    };

    /// <summary>不带参数的 <c>Owner::Name</c>。</summary>
    public static string MemberShort(MemberReference member) => $"{Type(member.DeclaringType)}::{member.Name}";

    /// <summary>定义 <paramref name="type"/> 的程序集名（按其所在模块的引用）。</summary>
    public static string AssemblyOf(TypeReference type)
    {
        TypeReference element = type;
        while (element is TypeSpecification specification)
        {
            element = specification.ElementType;
        }

        return element.Scope switch
        {
            AssemblyNameReference reference => reference.Name,
            ModuleDefinition module => module.Assembly.Name.Name,
            ModuleReference module => module.Name,
            _ => "",
        };
    }

    /// <summary>类型本身以及构成它的所有类型（元素类型、泛型实参、修饰符）。</summary>
    public static IEnumerable<TypeReference> Components(TypeReference? type)
    {
        if (type is null)
        {
            yield break;
        }

        yield return type;
        switch (type)
        {
            case GenericInstanceType generic:
                foreach (TypeReference component in Components(generic.ElementType))
                {
                    yield return component;
                }

                foreach (TypeReference argument in generic.GenericArguments)
                {
                    foreach (TypeReference component in Components(argument))
                    {
                        yield return component;
                    }
                }

                break;
            case IModifierType modified:
                foreach (TypeReference component in Components(modified.ElementType).Concat(Components(modified.ModifierType)))
                {
                    yield return component;
                }

                break;
            case TypeSpecification specification:
                foreach (TypeReference component in Components(specification.ElementType))
                {
                    yield return component;
                }

                break;
        }
    }

    /// <summary>决定 <paramref name="type"/> 归属的命名空间：自身的，或最外层声明类型的。</summary>
    public static string NamespaceOf(TypeDefinition type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type.Namespace;
    }

    /// <summary>C# 编译器生成的类型（闭包、迭代器等）返回 true。</summary>
    public static bool IsCompilerGenerated(TypeDefinition type)
    {
        for (TypeDefinition? current = type; current is not null; current = current.DeclaringType)
        {
            if (current.Name.Contains('<', StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>包含 <paramref name="type"/> 的手写类型（不是编译器生成时就是它本身）。</summary>
    public static TypeDefinition UserType(TypeDefinition type)
    {
        while (type.DeclaringType is not null && type.Name.Contains('<', StringComparison.Ordinal))
        {
            type = type.DeclaringType;
        }

        return type;
    }

    /// <summary>
    /// 包含 <paramref name="method"/> 的手写方法名：lambda 与迭代器体归到所在方法（<c>&lt;GetGizmos&gt;d__5</c> → <c>GetGizmos</c>）。
    /// </summary>
    public static string UserMethodName(MethodDefinition method)
    {
        foreach (string generated in new[] { method.Name, method.DeclaringType.Name })
        {
            int close = generated.IndexOf('>', StringComparison.Ordinal);
            if (generated.StartsWith('<') && close > 1)
            {
                return generated[1..close];
            }
        }

        return method.Name;
    }

    /// <summary>模块的全部类型，包括嵌套类型。</summary>
    public static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        var stack = new Stack<TypeDefinition>(module.Types.Reverse());
        while (stack.Count > 0)
        {
            TypeDefinition type = stack.Pop();
            yield return type;
            for (int i = type.NestedTypes.Count - 1; i >= 0; i--)
            {
                stack.Push(type.NestedTypes[i]);
            }
        }
    }

    /// <summary>逐级列出基类型名（经游戏程序集解析）；无法解析时停止。</summary>
    public static IEnumerable<string> BaseTypeNames(TypeDefinition type)
    {
        TypeReference? current = type.BaseType;
        while (current is not null)
        {
            yield return Type(current is GenericInstanceType generic ? generic.ElementType : current);
            TypeDefinition? definition;
            try
            {
                definition = current.Resolve();
            }
            catch (AssemblyResolutionException)
            {
                yield break;
            }

            current = definition?.BaseType;
        }
    }

    /// <summary>解析引用；程序集缺失时返回 <see langword="null"/>。</summary>
    public static TypeDefinition? TryResolve(TypeReference? type)
    {
        try
        {
            return type?.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    /// <summary>解析方法引用；程序集缺失时返回 <see langword="null"/>。</summary>
    public static MethodDefinition? TryResolve(MethodReference? method)
    {
        try
        {
            return method?.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    /// <summary>解析字段引用；程序集缺失时返回 <see langword="null"/>。</summary>
    public static FieldDefinition? TryResolve(FieldReference? field)
    {
        try
        {
            return field?.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    private static string Arity(MethodReference method)
        => method is GenericInstanceMethod instance
            ? "<" + string.Join(",", instance.GenericArguments.Select(Type)) + ">"
            : method.HasGenericParameters ? "`" + method.GenericParameters.Count : "";
}
