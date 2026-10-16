using System.Text;

namespace Mugirl.SaveUpgrade;

/// <summary>类型在存档 <c>Class</c> 属性中的写法，以及类型名表达式（含泛型参数）的逐项改写。</summary>
/// <remarks>
/// 与 <c>GenTypes.GetTypeNameWithoutIgnoredNamespaces</c> 一致：泛型写 <see cref="Type.ToString"/>，忽略命名空间中的类型写短名，
/// 其余写 <see cref="Type.FullName"/>。读档按名称解析时不区分大小写（<c>GenTypes</c> 的 <c>GetType(name, false, true)</c>），
/// 所以本类的比较器也不区分大小写。
/// </remarks>
internal static class SaveTypeNames
{
    /// <summary>登记可被保存的私有嵌套类型；不为升级器放宽领域类型的可见性。</summary>
    internal static Type RequireNested(Type declaringType, string name) =>
        declaringType.GetNestedType(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
        ?? throw new UpgradeTableException(["Missing current nested save type: " + declaringType.FullName + "+" + name]);

    /// <summary>类型名比较器：与 GenTypes 一样不区分大小写。</summary>
    internal static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    // StaticCacheLifecycle: 进程级常量集合，构造后只读。内容同 GenTypes.IgnoredNamespaceNames（1.7.4977）：这些命名空间中的类型在存档里只写短名。
    private static readonly HashSet<string> IgnoredNamespaces = new(StringComparer.Ordinal)
    {
        "RimWorld", "Verse", "LudeonTK", "Verse.AI", "Verse.AI.Group", "Verse.Sound", "Verse.Grammar", "RimWorld.Planet",
        "RimWorld.BaseGen", "RimWorld.QuestGen", "RimWorld.SketchGen", "System",
    };

    /// <summary><paramref name="type"/> 写入存档 <c>Class</c> 属性时的名称。</summary>
    internal static string Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.IsGenericType)
        {
            return type.ToString();
        }

        return type.Namespace is { } ns && IgnoredNamespaces.Contains(ns) ? type.Name : type.FullName ?? type.Name;
    }

    /// <summary>把完整类型名规范成存档写法：忽略命名空间中的类型去掉命名空间（嵌套类型只留最内层名）。</summary>
    internal static string Normalize(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        if (fullName.Contains('`', StringComparison.Ordinal) || fullName.Contains('[', StringComparison.Ordinal))
        {
            return fullName;
        }

        int dot = fullName.LastIndexOf('.');
        if (dot <= 0 || !IgnoredNamespaces.Contains(fullName[..dot]))
        {
            return fullName;
        }

        string shortName = fullName[(dot + 1)..];
        int plus = shortName.LastIndexOf('+');
        return plus >= 0 ? shortName[(plus + 1)..] : shortName;
    }

    /// <summary>
    /// 逐项改写类型名表达式：非泛型名整体查表；泛型名（<c>Ns.Gen`1[Ns.Arg]</c>）按 <c>[</c>、<c>]</c>、<c>,</c> 切分后逐个参数查表。
    /// 没有任何一项改变时返回 null。
    /// </summary>
    internal static string? Rewrite(string expression, Func<string, string?> map)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(map);
        if (!expression.Contains('[', StringComparison.Ordinal))
        {
            return map(expression.Trim());
        }

        var builder = new StringBuilder(expression.Length + 16);
        bool changed = false;
        int start = 0;
        for (int i = 0; i <= expression.Length; i++)
        {
            if (i < expression.Length && expression[i] is not ('[' or ']' or ','))
            {
                continue;
            }

            ReadOnlySpan<char> raw = expression.AsSpan(start, i - start);
            ReadOnlySpan<char> token = raw.Trim();
            if (token.Length > 0 && map(token.ToString()) is { } replacement)
            {
                int lead = raw.Length - raw.TrimStart().Length;
                builder.Append(raw[..lead]).Append(replacement).Append(raw[(lead + token.Length)..]);
                changed = true;
            }
            else
            {
                builder.Append(raw);
            }

            if (i < expression.Length)
            {
                builder.Append(expression[i]);
            }

            start = i + 1;
        }

        return changed ? builder.ToString() : null;
    }
}
