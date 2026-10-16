namespace Mugirl.Features.Restraints;

internal static class RestraintPawnTypeParser
{
    // XML 写法 colonist/slave/prisoner/free/hostile，忽略首尾空白与大小写（OrdinalIgnoreCase）；无法识别时为 None。
    // 1.6 每 tick 调用 Trim().ToLowerInvariant()，分配两个字符串；两者只在 'İ'、开尔文符号等非 ASCII 字符上有差别。
    internal static RestraintPawnType Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return RestraintPawnType.None;
        }

        ReadOnlySpan<char> value = text.AsSpan().Trim();
        if (MemoryExtensions.Equals(value, "colonist", StringComparison.OrdinalIgnoreCase))
        {
            return RestraintPawnType.Colonist;
        }

        if (MemoryExtensions.Equals(value, "slave", StringComparison.OrdinalIgnoreCase))
        {
            return RestraintPawnType.Slave;
        }

        if (MemoryExtensions.Equals(value, "prisoner", StringComparison.OrdinalIgnoreCase))
        {
            return RestraintPawnType.Prisoner;
        }

        if (MemoryExtensions.Equals(value, "free", StringComparison.OrdinalIgnoreCase))
        {
            return RestraintPawnType.Free;
        }

        if (MemoryExtensions.Equals(value, "hostile", StringComparison.OrdinalIgnoreCase))
        {
            return RestraintPawnType.Hostile;
        }

        return RestraintPawnType.None;
    }
}
