using System.Reflection;
using System.Text.RegularExpressions;

namespace MugirlRace.Validation.Content;

/// <summary>
/// RimWorld 1.7 <c>Verse.GenTypes.GetTypeInAnyAssembly</c> 的复刻，即每个 XML <c>Class=</c> 特性与 <c>Type</c> 字段背后的查找。
/// 真实方法只看 <c>LoadedModManager.RunningMods</c>，离线时为空，所以由验证程序按游戏顺序提供启用的程序集。
/// </summary>
/// <remarks>
/// 保留的语义：短名只对全局命名空间或 <see cref="IgnoredNamespaces"/> 之一中的类型生效（后加载的程序集覆盖前面的）；全名先在
/// Assembly-CSharp 中忽略大小写查找，然后在每个启用程序集中，再经 <c>Type.GetType</c>；短名最后依次加上各忽略命名空间再试；
/// 结果（包括失败）被缓存。
/// </remarks>
internal sealed partial class TypeResolver
{
    /// <summary><c>GenTypes.IgnoredNamespaceNames</c>，按游戏中的声明顺序。</summary>
    public static readonly string[] IgnoredNamespaces =
    [
        "RimWorld", "Verse", "LudeonTK", "Verse.AI", "Verse.AI.Group", "Verse.Sound", "Verse.Grammar", "RimWorld.Planet",
        "RimWorld.BaseGen", "RimWorld.QuestGen", "RimWorld.SketchGen", "System",
    ];

    private static readonly Dictionary<string, Type> Primitives = new(StringComparer.Ordinal)
    {
        ["int"] = typeof(int), ["uint"] = typeof(uint), ["short"] = typeof(short), ["ushort"] = typeof(ushort),
        ["float"] = typeof(float), ["double"] = typeof(double), ["long"] = typeof(long), ["ulong"] = typeof(ulong),
        ["byte"] = typeof(byte), ["sbyte"] = typeof(sbyte), ["char"] = typeof(char), ["bool"] = typeof(bool),
        ["decimal"] = typeof(decimal), ["string"] = typeof(string), ["int?"] = typeof(int?), ["uint?"] = typeof(uint?),
        ["short?"] = typeof(short?), ["ushort?"] = typeof(ushort?), ["float?"] = typeof(float?), ["double?"] = typeof(double?),
        ["long?"] = typeof(long?), ["ulong?"] = typeof(ulong?), ["byte?"] = typeof(byte?), ["sbyte?"] = typeof(sbyte?),
        ["char?"] = typeof(char?), ["bool?"] = typeof(bool?), ["decimal?"] = typeof(decimal?),
    };

    private readonly IReadOnlyList<Assembly> assemblies;
    private readonly Assembly game;
    private readonly Dictionary<string, Type> shortNames = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Name, string? Namespace), Type?> cache = [];

    /// <param name="activeAssemblies">Assembly-CSharp 在前，然后是按加载顺序的各 Mod 程序集。</param>
    /// <param name="typesOf">程序集中可加载的类型（<c>GenTypes.AllTypes</c> 会列出的那些）。</param>
    public TypeResolver(IReadOnlyList<Assembly> activeAssemblies, Func<Assembly, IEnumerable<Type>> typesOf)
    {
        assemblies = activeAssemblies;
        game = activeAssemblies[0];
        foreach (Type type in activeAssemblies.SelectMany(typesOf))
        {
            if (type.Namespace is null || Array.IndexOf(IgnoredNamespaces, type.Namespace) >= 0)
            {
                shortNames[type.Name] = type;
            }
        }
    }

    public Type? GetTypeInAnyAssembly(string typeName, string? namespaceIfAmbiguous = null)
    {
        if ((namespaceIfAmbiguous is null || Array.IndexOf(IgnoredNamespaces, namespaceIfAmbiguous) >= 0)
            && shortNames.TryGetValue(typeName, out Type? shortMatch))
        {
            return shortMatch;
        }

        if (!cache.TryGetValue((typeName, namespaceIfAmbiguous), out Type? result))
        {
            result = Resolve(typeName, namespaceIfAmbiguous);
            cache.Add((typeName, namespaceIfAmbiguous), result);
        }

        return result;
    }

    private Type? Resolve(string typeName, string? namespaceIfAmbiguous)
    {
        Type? type = Raw(typeName);
        if (type is not null)
        {
            return type;
        }

        if (!string.IsNullOrEmpty(namespaceIfAmbiguous) && Array.IndexOf(IgnoredNamespaces, namespaceIfAmbiguous) >= 0
            && Raw(namespaceIfAmbiguous + "." + typeName) is { } inNamespace)
        {
            return inNamespace;
        }

        foreach (string ns in IgnoredNamespaces)
        {
            if (Raw(ns + "." + typeName) is { } prefixed)
            {
                return prefixed;
            }
        }

        return MixedAssemblyGeneric(typeName);
    }

    private Type? Raw(string typeName)
    {
        if (Primitives.TryGetValue(typeName, out Type? primitive))
        {
            return primitive;
        }

        Type? type = game.GetType(typeName, throwOnError: false, ignoreCase: true);
        foreach (Assembly assembly in assemblies)
        {
            type ??= assembly.GetType(typeName, throwOnError: false, ignoreCase: true);
        }

        // 游戏在 Assembly-CSharp 中调用 Type.GetType；这里等价的搜索是核心库加上程序集限定名。
        type ??= typeof(object).Assembly.GetType(typeName, throwOnError: false, ignoreCase: true);
        if (type is null)
        {
            try
            {
                type = Type.GetType(typeName, throwOnError: false, ignoreCase: true);
            }
            catch (Exception exception) when (exception is ArgumentException or FileLoadException or BadImageFormatException)
            {
                type = null;
            }
        }

        return type;
    }

    // 参数来自不同程序集的 "Namespace.Generic`2[[Arg1],[Arg2]]"（GenTypes.TryGetMixedAssemblyGenericType）。
    private Type? MixedAssemblyGeneric(string typeName)
    {
        Type? direct = Raw(typeName);
        if (direct is not null || !typeName.Contains('`', StringComparison.Ordinal))
        {
            return direct;
        }

        Match match = GenericPattern().Match(typeName);
        if (!match.Success)
        {
            return null;
        }

        var arguments = new List<Type>();
        foreach (Match argument in ArgumentPattern().Matches(match.Groups["Types"].Value))
        {
            if (MixedAssemblyGeneric(argument.Groups["Type"].Value.Trim()) is not { } resolved)
            {
                return null;
            }

            arguments.Add(resolved);
        }

        try
        {
            return MixedAssemblyGeneric(match.Groups["MainType"].Value)?.MakeGenericType([.. arguments]);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"(?<MainType>.+`(?<ParamCount>[0-9]+))(?<Types>\[.*\])")]
    private static partial Regex GenericPattern();

    [GeneratedRegex(@"\[(?<Type>.*?)\],?")]
    private static partial Regex ArgumentPattern();
}
