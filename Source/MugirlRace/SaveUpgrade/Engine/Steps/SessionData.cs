using System.Diagnostics.CodeAnalysis;

namespace Mugirl.SaveUpgrade;

/// <summary>同一次读档内从 DOM 步骤传给读档后步骤的标志与数据；读档结束时随会话一起丢弃。</summary>
internal sealed class SessionData
{
    private readonly HashSet<string> flags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> payload = new(StringComparer.Ordinal);

    internal IReadOnlyCollection<string> Flags => flags;

    /// <summary>DOM 全部成功后发布的本次读档引用映射；不写入存档，也不跨读档复用。</summary>
    internal LoadIdRemapper? LoadIds { get; set; }

    /// <summary>DOM 中重置了状态图的 Lord 的 <c>loadID</c>；阶段 B 让它们重新进入起始 LordToil。</summary>
    internal HashSet<int> ResetLordIds { get; } = [];

    internal void SetFlag(string flag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flag);
        flags.Add(flag);
    }

    internal bool HasFlag(string flag) => flags.Contains(flag);

    internal void Put(string key, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        payload[key] = value;
    }

    internal bool TryGet<T>(string key, [NotNullWhen(true)] out T? value)
        where T : class
    {
        if (payload.TryGetValue(key, out object? stored) && stored is T typed)
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }
}
