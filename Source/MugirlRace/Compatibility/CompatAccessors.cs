using System.Reflection;
using HarmonyLib;

namespace Mugirl.Compatibility;

/// <summary>
/// 把按名字找到的第三方成员变成强类型委托，在兼容层初始化时解析一次。
/// </summary>
/// <remarks>
/// 代替热路径上的 <c>MethodInfo.Invoke</c>、<c>FieldInfo.GetValue</c> 与 <c>PropertyInfo.GetValue</c>：
/// 不再每次分配参数数组或装箱返回值，对方抛出的异常也不再包一层 <see cref="TargetInvocationException"/>。
/// 签名不符或无法绑定时一律返回 null，调用方按“对方 Mod 不可用”降级。
/// </remarks>
internal static class CompatAccessors
{
    /// <summary>静态方法的委托；不是静态方法或签名不符时为 null。</summary>
    internal static TDelegate? StaticMethod<TDelegate>(MethodInfo? method)
        where TDelegate : Delegate
    {
        if (method is null || !method.IsStatic)
        {
            return null;
        }

        try
        {
            return (TDelegate?)Delegate.CreateDelegate(typeof(TDelegate), method, throwOnBindFailure: false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 无参实例方法的开放委托，实例以基类 <typeparamref name="TBase"/> 传入，委托内部转换为声明类型。
    /// 调用方须先确认实例确实是该类型，否则转换抛出 <see cref="InvalidCastException"/>。
    /// </summary>
    internal static Action<TBase>? InstanceAction<TBase>(MethodInfo? method)
        where TBase : class
    {
        if (method is null || method.IsStatic || method.GetParameters().Length != 0)
        {
            return null;
        }

        MethodInfo? factory = typeof(CompatAccessors).GetMethod(nameof(OpenAction), BindingFlags.NonPublic | BindingFlags.Static);
        return CreateTyped<Action<TBase>>(factory, typeof(TBase), method.DeclaringType, method);
    }

    /// <summary>实例属性的读取委托，实例以基类 <typeparamref name="TBase"/> 传入；属性类型必须正好是 <typeparamref name="TValue"/>。</summary>
    internal static Func<TBase, TValue>? InstanceGetter<TBase, TValue>(PropertyInfo? property)
        where TBase : class
    {
        MethodInfo? getter = property?.GetGetMethod(nonPublic: true);
        if (getter is null || getter.IsStatic || getter.ReturnType != typeof(TValue))
        {
            return null;
        }

        MethodInfo? factory = typeof(CompatAccessors).GetMethod(nameof(OpenGetter), BindingFlags.NonPublic | BindingFlags.Static);
        return CreateTyped<Func<TBase, TValue>>(factory, typeof(TBase), getter.DeclaringType, getter, typeof(TValue));
    }

    /// <summary>实例字段的读取委托（Harmony <see cref="AccessTools.FieldRefAccess{T, F}(FieldInfo)"/>，带类型转换）；字段类型必须正好是 <typeparamref name="TValue"/>。</summary>
    internal static Func<TBase, TValue>? InstanceGetter<TBase, TValue>(FieldInfo? field)
        where TBase : class
    {
        if (field is null || field.IsStatic || field.FieldType != typeof(TValue)
            || field.DeclaringType is null || !typeof(TBase).IsAssignableFrom(field.DeclaringType))
        {
            return null;
        }

        try
        {
            AccessTools.FieldRef<TBase, TValue> fieldRef = AccessTools.FieldRefAccess<TBase, TValue>(field);
            return instance => fieldRef(instance);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>静态字段的引用访问器；字段类型必须正好是 <typeparamref name="TValue"/>。</summary>
    internal static AccessTools.FieldRef<TValue>? StaticField<TValue>(FieldInfo? field)
    {
        if (field is null || !field.IsStatic || field.FieldType != typeof(TValue))
        {
            return null;
        }

        try
        {
            return AccessTools.StaticFieldRefAccess<TValue>(field);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 对方 Mod 已启用、但某个成员无法解析或绑定时记录一条警告，帮助定位对方改版后的兼容失效。
    /// 只在各兼容类的反射缓存初始化时调用，每个进程一次。
    /// </summary>
    internal static void ReportUnbound(string compatibility, string member)
        => MugirlLog.Warning(compatibility + " compatibility could not bind " + member + "; that part of the integration is disabled.");

    // 声明类型只在运行时知道，经 MakeGenericMethod 实例化一次泛型工厂；引用类型共享同一份 JIT 代码，不生成动态方法。
    private static TDelegate? CreateTyped<TDelegate>(MethodInfo? factory, Type baseType, Type? declaringType, MethodInfo method, Type? valueType = null)
        where TDelegate : Delegate
    {
        if (factory is null || declaringType is null || declaringType.IsValueType || !baseType.IsAssignableFrom(declaringType))
        {
            return null;
        }

        try
        {
            Type[] typeArguments = valueType is null ? [baseType, declaringType] : [baseType, declaringType, valueType];
            return factory.MakeGenericMethod(typeArguments).Invoke(null, [method]) as TDelegate;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Action<TBase> OpenAction<TBase, TDeclaring>(MethodInfo method)
        where TBase : class
        where TDeclaring : class, TBase
    {
        Action<TDeclaring> typed = method.CreateDelegate<Action<TDeclaring>>();
        return instance => typed((TDeclaring)instance);
    }

    private static Func<TBase, TValue> OpenGetter<TBase, TDeclaring, TValue>(MethodInfo getter)
        where TBase : class
        where TDeclaring : class, TBase
    {
        Func<TDeclaring, TValue> typed = getter.CreateDelegate<Func<TDeclaring, TValue>>();
        return instance => typed((TDeclaring)instance);
    }
}
