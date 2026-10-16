namespace Mugirl.Features.Mounting;

/// <summary>骑乘绘制的外部观察入口。单一成对提供者，无绘制期分配；嵌套调用由 token 的所有者恢复。</summary>
internal static class MountingHooks
{
    // StaticCacheLifecycle: Mod 构造期登记的进程级委托，不保存角色或设置实例；设置实例仅存在于单次绘制 scope。
    private static Func<CompMount?, object?>? beginDraw;
    private static Action<object>? endDraw;
    private static string? owner;

    internal static void RegisterMeleeDraw(string registrationOwner, Func<CompMount?, object?> begin, Action<object> end)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationOwner);
        ArgumentNullException.ThrowIfNull(begin);
        ArgumentNullException.ThrowIfNull(end);
        if (owner == registrationOwner)
        {
            return;
        }
        if (owner is not null)
        {
            throw new InvalidOperationException("Only one mounted melee draw observer can be registered.");
        }
        owner = registrationOwner;
        beginDraw = begin;
        endDraw = end;
    }

    internal static DrawScope BeginMeleeDraw(CompMount? comp) => new(beginDraw?.Invoke(comp), endDraw);

    internal struct DrawScope(object? token, Action<object>? restore) : IDisposable
    {
        public void Dispose()
        {
            object? current = token;
            token = null;
            if (current is not null)
            {
                restore!(current);
            }
        }
    }
}
