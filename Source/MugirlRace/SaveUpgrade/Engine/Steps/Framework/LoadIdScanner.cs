using System.Text;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 在实际加载 ID 中查找以开头、结尾或 <c>_</c> 为界的旧 ThingID 与已删除的加载 ID（见 <see cref="LoadIdTokens"/>）。
/// 只按 <c>_</c> 切分位置比较子串，查表不分配字符串。
/// 调用方必须已确认是身份或引用；玩家文字即使符合相同格式也不能调用本扫描器。
/// </summary>
internal sealed class LoadIdScanner
{
    // 加载 ID 中的 '_' 不会多到这个数；超过的文本不是加载 ID。
    private const int MaxSeparators = 64;

    private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> renames;
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> removed;
    private readonly int minLength;
    private readonly bool active;

    /// <param name="renames">旧 ThingID → 新 ThingID；比较器必须是 <see cref="StringComparer.Ordinal"/>。</param>
    /// <param name="removed">已删除对象的加载 ID；比较器必须是 <see cref="StringComparer.Ordinal"/>。</param>
    internal LoadIdScanner(Dictionary<string, string> renames, HashSet<string> removed)
    {
        ArgumentNullException.ThrowIfNull(renames);
        ArgumentNullException.ThrowIfNull(removed);
        this.renames = renames.GetAlternateLookup<ReadOnlySpan<char>>();
        this.removed = removed.GetAlternateLookup<ReadOnlySpan<char>>();
        int min = int.MaxValue;
        foreach (string key in renames.Keys)
        {
            min = Math.Min(min, key.Length);
        }

        foreach (string key in removed)
        {
            min = Math.Min(min, key.Length);
        }

        minLength = min;
        active = renames.Count > 0 || removed.Count > 0;
    }

    internal enum Result
    {
        Unchanged,
        Rewritten,
        ReferencesRemoved,
    }

    internal Result Scan(string text, out string? rewritten)
    {
        rewritten = null;
        ReadOnlySpan<char> span = text.AsSpan();
        if (!active || span.Length < minLength || span.IndexOfAnyInRange('0', '9') < 0)
        {
            return Result.Unchanged;
        }

        Span<int> separators = stackalloc int[MaxSeparators];
        int separatorCount = 0;
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == '_')
            {
                if (separatorCount == MaxSeparators)
                {
                    return Result.Unchanged;
                }

                separators[separatorCount++] = i;
            }
        }

        StringBuilder? builder = null;
        int copied = 0;
        for (int s = -1; s < separatorCount; s++)
        {
            int start = s < 0 ? 0 : separators[s] + 1;
            if (start < copied)
            {
                continue;
            }

            // 结尾候选从长到短：文本末尾，然后各个 '_' 位置（由后往前）。
            for (int e = separatorCount; e >= 0; e--)
            {
                int end = e == separatorCount ? span.Length : separators[e];
                int length = end - start;
                if (length < minLength)
                {
                    break;
                }

                if (!char.IsAsciiDigit(span[end - 1]))
                {
                    continue;
                }

                ReadOnlySpan<char> token = span.Slice(start, length);
                if (removed.Contains(token))
                {
                    return Result.ReferencesRemoved;
                }

                if (renames.TryGetValue(token, out string? newId))
                {
                    builder ??= new StringBuilder(text.Length + 16);
                    builder.Append(span[copied..start]).Append(newId);
                    copied = end;
                    break;
                }
            }
        }

        if (builder is null)
        {
            return Result.Unchanged;
        }

        builder.Append(span[copied..]);
        rewritten = builder.ToString();
        return Result.Rewritten;
    }
}
