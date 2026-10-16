using System.Reflection;
using HarmonyLib;

namespace Mugirl.GameValidation.Core;

/// <summary>统计界面方法（窗口的 DoWindowContents、ITab 的 FillTab、Mod 的设置页）实际被游戏调用的次数。</summary>
/// <remarks>
/// 打开窗口只说明它进了 WindowStack；只有绘制方法在 OnGUI 中被调用、且调用中没有抛异常，才说明界面真的画出来了。
/// 用测试自己的 Harmony 后缀计数，不改变绘制；计数按实例区分，所以给基类方法打补丁也能只数自己打开的那个窗口。
/// Harmony 只能给声明方法的类型上的方法打补丁（经由派生类反射得到的 MethodInfo 会被拒绝），所以总是改用实际声明它的那个类型上的方法。
/// 只在主线程使用（OnGUI 与会话都在主线程）。
/// </remarks>
internal static class DrawProbe
{
    private const BindingFlags DeclaredInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    // StaticCacheLifecycle: 进程级；整次运行一份，键是被绘制的实例，运行结束随进程退出。
    private static readonly Dictionary<object, int> Counts = new(ReferenceEqualityComparer.Instance);

    // StaticCacheLifecycle: 进程级；已经挂上计数后缀的方法，避免重复打补丁。
    private static readonly HashSet<MethodBase> Watched = [];

    /// <summary>给 <paramref name="method"/>（换成声明它的类型上的同一方法）挂计数后缀；同一方法只挂一次。返回实际打补丁的方法。</summary>
    public static MethodBase Watch(ValidationContext context, MethodBase method)
    {
        MethodBase declared = Declared(method);
        if (Watched.Add(declared))
        {
            context.Harmony.Patch(declared, postfix: new HarmonyMethod(typeof(DrawProbe), nameof(AfterDraw)));
        }

        return declared;
    }

    /// <summary>
    /// 在 <paramref name="type"/> 及其基类中查找名为 <paramref name="methodName"/> 的实例方法，取最派生的那个有方法体的声明，
    /// 并挂计数后缀（例如窗口类自己没有覆写 DoWindowContents 时，补丁落在声明它的基类上）。找不到时返回 null。
    /// </summary>
    public static MethodInfo? Watch(ValidationContext context, Type type, string methodName)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            MethodInfo? method = current.GetMethods(DeclaredInstance)
                .Where(m => m.Name == methodName && !m.IsAbstract)
                .OrderBy(m => m.GetParameters().Length)
                .FirstOrDefault();
            if (method is not null)
            {
                Watch(context, method);
                return method;
            }
        }

        return null;
    }

    public static int Count(object instance) => Counts.GetValueOrDefault(instance);

    /// <summary>
    /// <paramref name="method"/> 在声明它的类型上的 MethodInfo。经由派生类反射得到的 MethodInfo（ReflectedType 是派生类）
    /// 会被 Harmony 拒绝（"You can only patch implemented methods"）。
    /// </summary>
    private static MethodBase Declared(MethodBase method)
    {
        if (method.DeclaringType is not { } declaring || method.ReflectedType == declaring)
        {
            return method;
        }

        Type[] parameters = [.. method.GetParameters().Select(p => p.ParameterType)];
        BindingFlags flags = (method.IsStatic ? BindingFlags.Static : BindingFlags.Instance) | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        return method is ConstructorInfo
            ? declaring.GetConstructor(flags, binder: null, parameters, modifiers: null) ?? method
            : declaring.GetMethod(method.Name, flags, binder: null, parameters, modifiers: null) ?? method;
    }

    private static void AfterDraw(object __instance)
    {
        Counts[__instance] = Counts.GetValueOrDefault(__instance) + 1;
    }
}
