using System.IO;
using System.Linq;
using System.Reflection;

namespace Mugirl.Core;

/// <summary>按事件分组、显式排序的生命周期处理器表。</summary>
internal sealed class GameLifecycleRegistry
{
    private const BindingFlags DeclaredMethods = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly Dictionary<GameLifecycleEvent, GameLifecycleHook[]> byEvent;

    private GameLifecycleRegistry(IEnumerable<GameLifecycleHook> hooks, IReadOnlyList<string> problems)
    {
        byEvent = hooks
            .GroupBy(hook => hook.Event)
            .ToDictionary(group => group.Key, group => group.OrderBy(hook => hook.Order).ThenBy(hook => hook.Id, StringComparer.Ordinal).ToArray());
        Problems = problems;
    }

    /// <summary>发现时被拒绝的处理器（签名不对、同一方法重复订阅同一事件）。</summary>
    internal IReadOnlyList<string> Problems { get; }

    /// <summary>扫描 <paramref name="assembly"/> 中带 <see cref="GameLifecycleHookAttribute"/> 的方法。</summary>
    internal static GameLifecycleRegistry FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        List<string> problems = [];
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types;
            problems.Add("Some types of " + assembly.GetName().Name + " could not be loaded: " + exception.Message);
        }

        IEnumerable<MethodInfo> methods = types
            .Where(type => type is not null)
            .SelectMany(type => type!.GetMethods(DeclaredMethods));
        return FromMethods(methods, problems);
    }

    /// <summary>由给定方法建立处理器表；没有 <see cref="GameLifecycleHookAttribute"/> 的方法忽略。</summary>
    internal static GameLifecycleRegistry FromMethods(IEnumerable<MethodInfo> methods, List<string>? problems = null)
    {
        ArgumentNullException.ThrowIfNull(methods);
        problems ??= [];
        List<GameLifecycleHook> hooks = [];
        var seen = new HashSet<(GameLifecycleEvent, string)>();
        foreach (MethodInfo method in methods)
        {
            string id = (method.DeclaringType?.FullName ?? "?") + "." + method.Name;
            GameLifecycleHookAttribute[] attributes;
            try
            {
                attributes = [.. method.GetCustomAttributes<GameLifecycleHookAttribute>(inherit: false)];
            }
            catch (Exception exception) when (exception is TypeLoadException or FileNotFoundException or CustomAttributeFormatException)
            {
                // 读不出特性的方法可能是处理器，不能静默跳过。
                problems.Add(id + " attributes could not be read: " + exception.Message);
                continue;
            }

            if (attributes.Length == 0)
            {
                continue;
            }

            if (!method.IsStatic || method.ReturnType != typeof(void) || method.GetParameters().Length != 0 || method.ContainsGenericParameters)
            {
                problems.Add(id + " must be 'static void " + method.Name + "()' to be a lifecycle hook.");
                continue;
            }

            Action callback = method.CreateDelegate<Action>();
            foreach (GameLifecycleHookAttribute attribute in attributes)
            {
                if (!seen.Add((attribute.Event, id)))
                {
                    problems.Add(id + " subscribes to " + attribute.Event + " more than once.");
                    continue;
                }

                hooks.Add(new GameLifecycleHook(attribute.Event, attribute.Order, id, callback));
            }
        }

        return new GameLifecycleRegistry(hooks, problems);
    }

    /// <summary><paramref name="lifecycleEvent"/> 的处理器，按调用顺序排列。</summary>
    internal IReadOnlyList<GameLifecycleHook> HooksFor(GameLifecycleEvent lifecycleEvent) =>
        byEvent.TryGetValue(lifecycleEvent, out GameLifecycleHook[]? hooks) ? hooks : [];

    /// <summary>依次调用处理器；抛出的异常交给 <paramref name="onError"/>，不影响其余处理器。返回成功调用的个数。</summary>
    internal int Raise(GameLifecycleEvent lifecycleEvent, Action<GameLifecycleHook, Exception> onError)
    {
        ArgumentNullException.ThrowIfNull(onError);
        int succeeded = 0;
        foreach (GameLifecycleHook hook in HooksFor(lifecycleEvent))
        {
            try
            {
                hook.Callback();
                succeeded++;
            }
            catch (Exception exception)
            {
                onError(hook, exception);
            }
        }

        return succeeded;
    }
}
