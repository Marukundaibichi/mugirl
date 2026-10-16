namespace Mugirl.Core;

/// <summary>
/// 把 <c>static void M()</c> 方法登记为生命周期事件的处理器。同一事件的处理器按 (<see cref="Order"/>, 完整方法名) 排序调用，
/// 与反射顺序无关；处理器之间不得依赖调用顺序以外的约定，单个处理器抛出不影响其余处理器。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
internal sealed class GameLifecycleHookAttribute(GameLifecycleEvent lifecycleEvent) : Attribute
{
    internal GameLifecycleEvent Event { get; } = lifecycleEvent;

    /// <summary>同一事件内的显式顺序，小的先调用；默认 0。</summary>
    public int Order { get; set; }
}
