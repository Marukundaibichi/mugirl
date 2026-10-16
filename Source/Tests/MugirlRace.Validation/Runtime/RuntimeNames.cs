using System.Reflection;

namespace MugirlRace.Validation.Runtime;

/// <summary>反射类型与方法的稳定名称（标准 .NET 与游戏运行时上相同）。</summary>
internal static class RuntimeNames
{
    /// <summary><c>Namespace.Outer+Inner</c>，泛型实参放在尖括号中，不含程序集名。</summary>
    public static string Type(Type type)
    {
        if (type.IsByRef)
        {
            return Type(type.GetElementType()!) + "&";
        }

        if (type.IsPointer)
        {
            return Type(type.GetElementType()!) + "*";
        }

        if (type.IsArray)
        {
            return Type(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        string name = type.Name;
        int tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
        {
            name = name[..tick];
        }

        string prefix = type.DeclaringType is { } outer && !type.IsGenericParameter ? Type(outer) + "+" : (type.Namespace is { Length: > 0 } ns ? ns + "." : "");
        if (type.IsGenericType)
        {
            Type[] arguments = type.GetGenericArguments();
            int inherited = type.DeclaringType?.GetGenericArguments().Length ?? 0;
            Type[] own = arguments.Skip(inherited).ToArray();
            if (own.Length > 0)
            {
                name += "<" + string.Join(",", own.Select(Type)) + ">";
            }
        }

        return prefix + name;
    }

    /// <summary><c>Type::Method(ParameterTypes)</c>；泛型方法列出其实参。</summary>
    public static string Method(MethodBase method)
    {
        string generic = method.IsGenericMethod ? "<" + string.Join(",", method.GetGenericArguments().Select(Type)) + ">" : "";
        string parameters = string.Join(", ", method.GetParameters().Select(p => Type(p.ParameterType)));
        return $"{(method.DeclaringType is null ? "" : Type(method.DeclaringType))}::{method.Name}{generic}({parameters})";
    }
}
