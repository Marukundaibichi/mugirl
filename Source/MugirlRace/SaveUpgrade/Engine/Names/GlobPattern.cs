namespace Mugirl.SaveUpgrade;

/// <summary>只支持 <c>*</c> 通配符、区分大小写的名称模式，例如 <c>MooGirl*</c>。</summary>
internal sealed class GlobPattern
{
    private readonly string[] parts;
    private readonly bool anchoredStart;
    private readonly bool anchoredEnd;

    internal GlobPattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        Text = pattern;
        anchoredStart = !pattern.StartsWith('*');
        anchoredEnd = !pattern.EndsWith('*');
        parts = pattern.Split('*', StringSplitOptions.RemoveEmptyEntries);
    }

    internal string Text { get; }

    internal bool IsMatch(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (parts.Length == 0)
        {
            return true;
        }

        int position = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            bool first = i == 0;
            bool last = i == parts.Length - 1;
            if (first && anchoredStart)
            {
                if (!value.StartsWith(part, StringComparison.Ordinal))
                {
                    return false;
                }

                position = part.Length;
            }
            else if (last && anchoredEnd)
            {
                return value.Length - part.Length >= position && value.EndsWith(part, StringComparison.Ordinal);
            }
            else
            {
                int found = value.IndexOf(part, position, StringComparison.Ordinal);
                if (found < 0)
                {
                    return false;
                }

                position = found + part.Length;
            }

            if (last && anchoredEnd)
            {
                return position == value.Length;
            }
        }

        return true;
    }

    public override string ToString() => Text;
}
