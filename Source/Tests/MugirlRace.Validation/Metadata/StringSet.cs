using System.Collections.Immutable;

namespace MugirlRace.Validation.Metadata;

/// <summary>
/// 字符串流分析的抽象值：栈槽或局部变量可能取的有限字面量集合，或“未知”（任何非编译期字面量：字段、运行时拼接、调用结果）。
/// 恰好是方法某个参数的未知值会记住是哪个参数，因此 <c>Look(ref v, string label) =&gt; Scribe_Values.Look(ref v, label)</c>
/// 这样的辅助方法可以追溯到调用点。<c>typeof(T)</c> 值以同样方式跟踪（<see cref="IsType"/>），用于按名称的反射查找。
/// </summary>
/// <remarks>
/// 存档标签还可能来自本对象的虚属性或字段（<c>Scribe_Values.Look(ref x, SaveKey + "_tick")</c>）。这类值记为模板
/// （<see cref="Template"/>）：字面量与“洞”（<c>this</c> 上的无参字符串 getter 或字符串字段）按顺序拼接。模板对其他检查而言
/// 仍是未知值（<see cref="IsKnown"/> 为 false），只有存档模式提取器按具体子类求值。
/// </remarks>
internal sealed class StringSet : IEquatable<StringSet>
{
    /// <summary>备选值超过这个数量时视为未知（限制分析规模）。</summary>
    public const int MaxAlternatives = 32;

    /// <summary>模板中洞的前缀；其后是 <c>call:</c>/<c>field:</c> 加成员名，或 <c>this</c>。</summary>
    public const char HoleMarker = '\u0001';

    public static readonly StringSet Unknown = new(null, null, isType: false, template: default);

    /// <summary>实例方法中的 <c>this</c>（只用来识别 <c>this.Getter</c> 与 <c>this.field</c>）。</summary>
    public static readonly StringSet This = new(null, null, isType: false, template: [HoleMarker + "this"]);

    private StringSet(ImmutableSortedSet<string>? values, int? parameter, bool isType, ImmutableArray<string> template)
    {
        Values = values;
        Parameter = parameter;
        IsType = isType;
        Template = template;
    }

    /// <summary>可能的字面量；值不是已知字面量时为 <see langword="null"/>。</summary>
    public ImmutableSortedSet<string>? Values { get; }

    /// <summary>未知值恰为方法参数时的参数位置（0 = 第一个声明参数）。</summary>
    public int? Parameter { get; }

    /// <summary>字面量是来自 <c>typeof(T)</c> 的类型名（命名同 <see cref="CecilNames.Type"/>），不是字符串。</summary>
    public bool IsType { get; }

    /// <summary>
    /// 模板各部分（字面量，或以 <see cref="HoleMarker"/> 开头的洞，例如 <c>"\u0001call:Mugirl.CompX::get_SaveKey"</c>）；
    /// 不是模板时为默认值（<c>IsDefault</c>）。
    /// </summary>
    public ImmutableArray<string> Template { get; }

    public bool IsKnown => Values is not null;

    /// <summary>模板中至少有一个 getter 或字段洞（不含单独的 <c>this</c>）。</summary>
    public bool IsTemplate => !Template.IsDefault && !IsThis;

    public bool IsThis => !Template.IsDefault && Template.Length == 1 && Template[0] == HoleMarker + "this";

    public static StringSet Of(string literal) => new(ImmutableSortedSet.Create(StringComparer.Ordinal, literal), null, isType: false, default);

    public static StringSet OfType(string typeName) => new(ImmutableSortedSet.Create(StringComparer.Ordinal, typeName), null, isType: true, default);

    public static StringSet FromParameter(int position) => new(null, position, isType: false, default);

    /// <summary><c>this</c> 上的无参字符串 getter（<paramref name="member"/> 为 <c>Owner::get_X</c>）。</summary>
    public static StringSet ThisCall(string member) => new(null, null, isType: false, [HoleMarker + "call:" + member]);

    /// <summary><c>this</c> 的字符串字段（<paramref name="member"/> 为 <c>Owner::field</c>）。</summary>
    public static StringSet ThisField(string member) => new(null, null, isType: false, [HoleMarker + "field:" + member]);

    public static StringSet Merge(StringSet a, StringSet b)
    {
        if (ReferenceEquals(a, b) || a.Equals(b))
        {
            return a;
        }

        if (a.Values is null || b.Values is null || a.IsType != b.IsType)
        {
            return Unknown;
        }

        ImmutableSortedSet<string> union = a.Values.Union(b.Values);
        return union.Count > MaxAlternatives ? Unknown : new StringSet(union, null, a.IsType, default);
    }

    /// <summary>
    /// 各部分按顺序拼接的所有组合（字面量的 <c>string.Concat</c>）。有洞时，若其余部分都是单个字面量，结果是模板；否则未知。
    /// </summary>
    public static StringSet Concat(IReadOnlyList<StringSet> parts)
    {
        if (parts.Any(part => part.IsTemplate))
        {
            return ConcatTemplate(parts);
        }

        ImmutableSortedSet<string> result = ImmutableSortedSet.Create(StringComparer.Ordinal, "");
        foreach (StringSet part in parts)
        {
            if (part.Values is null || part.IsType)
            {
                return Unknown;
            }

            ImmutableSortedSet<string>.Builder next = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
            foreach (string prefix in result)
            {
                foreach (string suffix in part.Values)
                {
                    next.Add(prefix + suffix);
                }
            }

            if (next.Count > MaxAlternatives)
            {
                return Unknown;
            }

            result = next.ToImmutable();
        }

        return new StringSet(result, null, isType: false, default);
    }

    private static StringSet ConcatTemplate(IReadOnlyList<StringSet> parts)
    {
        ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>();
        foreach (StringSet part in parts)
        {
            if (part.IsTemplate)
            {
                builder.AddRange(part.Template);
            }
            else if (part.Values is { Count: 1 } single && !part.IsType)
            {
                builder.Add(single.Min!);
            }
            else
            {
                return Unknown;
            }
        }

        return new StringSet(null, null, isType: false, builder.ToImmutable());
    }

    public bool Equals(StringSet? other)
        => other is not null && Parameter == other.Parameter && IsType == other.IsType
            && (Values is null ? other.Values is null : other.Values is not null && Values.SetEquals(other.Values))
            && (Template.IsDefault ? other.Template.IsDefault : !other.Template.IsDefault && Template.SequenceEqual(other.Template, StringComparer.Ordinal));

    public override bool Equals(object? obj) => Equals(obj as StringSet);

    public override int GetHashCode() => Values?.Count ?? Parameter ?? (Template.IsDefault ? -1 : Template.Length);

    public override string ToString()
        => Values is not null ? string.Join(" | ", Values.Select(v => IsType ? "typeof(" + v + ")" : "\"" + v + "\""))
            : Parameter is { } position ? $"(parameter {position})"
            : !Template.IsDefault ? string.Concat(Template.Select(part => part.StartsWith(HoleMarker) ? "{" + part[1..] + "}" : part))
            : "(dynamic)";
}
