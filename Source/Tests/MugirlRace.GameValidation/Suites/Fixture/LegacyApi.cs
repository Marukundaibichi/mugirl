using System.Globalization;
using System.Reflection;

namespace Mugirl.GameValidation.Suites;

/// <summary>
/// 按名字访问被测旧构建的成员（播种套件的“构建专属”补充动作）：只有经由 Mod 自己的入口才能建立的状态（武器轮盘槽位、巨企开户与服务、
/// 束缚服装的锁）。旧构建没有对应成员时返回失败说明，调用方记为 "unavailable"，不算播种失败。
/// </summary>
/// <remarks>不引用任何 Mod 类型：-Plumbing 构建驱动的是任意旧构建的程序集。</remarks>
internal static class LegacyApi
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>程序集中简单名为 <paramref name="name"/> 的类型（任意命名空间）；加载失败的类型跳过。</summary>
    public static Type? FindType(Assembly? assembly, string name)
        => assembly is null ? null : LoadableTypes(assembly).FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

    /// <summary>程序集中可以加载的类型（旧构建在 1.7 上部分类型可能加载失败）。</summary>
    public static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    /// <summary>类型或其基类中名为 <paramref name="name"/> 的字段。</summary>
    public static FieldInfo? Field(Type? type, string name)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            if (current.GetField(name, Members | BindingFlags.DeclaredOnly) is { } field)
            {
                return field;
            }
        }

        return null;
    }

    /// <summary>类型或其基类中名为 <paramref name="name"/>、参数个数为 <paramref name="parameterCount"/> 的方法。</summary>
    public static MethodInfo? Method(Type? type, string name, int parameterCount)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMethods(Members | BindingFlags.DeclaredOnly).FirstOrDefault(m => m.Name == name && m.GetParameters().Length == parameterCount) is { } method)
            {
                return method;
            }
        }

        return null;
    }

    /// <summary>实例字段的值；没有该字段时为 null。</summary>
    public static object? Get(object target, string field) => Field(target.GetType(), field)?.GetValue(target);

    /// <summary>按明确的构建路径查找字段；字段存在但为 null 时不改读另一份状态。</summary>
    public static object? GetAny(object target, params string[] paths)
    {
        foreach (string path in paths)
        {
            if (ResolveField(target, path, out object owner, out FieldInfo? field)) { return field!.GetValue(owner); }
        }
        return null;
    }

    /// <summary>只写第一个实际存在的字段路径，类型不符时不回退到旧构建字段。</summary>
    public static bool SetAny(object target, object value, params string[] paths)
    {
        foreach (string path in paths)
        {
            if (ResolveField(target, path, out object owner, out FieldInfo? field))
            {
                if (!field!.FieldType.IsInstanceOfType(value)) { return false; }
                field.SetValue(owner, value);
                return true;
            }
        }
        return false;
    }

    private static bool ResolveField(object target, string path, out object owner, out FieldInfo? field)
    {
        owner = target;
        field = null;
        string[] parts = path.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            field = Field(owner.GetType(), parts[i]);
            if (field is null) { return false; }
            if (i != parts.Length - 1)
            {
                owner = field.GetValue(owner) ?? throw new InvalidOperationException("The fixture state path is null: " + path);
            }
        }
        return field is not null;
    }

    /// <summary>设置实例字段；没有该字段或类型不符时返回 false。</summary>
    public static bool Set(object target, string field, object value)
    {
        if (Field(target.GetType(), field) is not { } info || !info.FieldType.IsInstanceOfType(value))
        {
            return false;
        }

        info.SetValue(target, value);
        return true;
    }

    /// <summary>Scribe 写法的文本：布尔为 True/False，数按不变区域性。</summary>
    public static string? ScribeText(object? value) => value switch
    {
        null => null,
        bool flag => flag ? "True" : "False",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>运行中的游戏中类型为 <paramref name="type"/> 的 GameComponent。</summary>
    public static GameComponent? GameComponent(Type? type)
        => type is null ? null : Current.Game?.components.FirstOrDefault(c => type.IsInstanceOfType(c));
}
