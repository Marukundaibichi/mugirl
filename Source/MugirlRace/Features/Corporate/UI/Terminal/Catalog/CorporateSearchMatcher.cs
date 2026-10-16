using System.Globalization;

namespace Mugirl.Features.Corporate;

/// <summary>
/// 目录搜索的包含判断，语义与 1.6 的 <c>label.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) &gt;= 0</c> 相同。
/// </summary>
/// <remarks>
/// <para>
/// 1.7 的文化相关比较经 Windows ICU 实现：标签含中文时每次约 1 µs，两万条订购目录要数十帧才能完成筛选。
/// 标签与查询都只含 ASCII 字母、数字、空格与 CJK 统一表意文字（U+3400–4DBF、U+4E00–9FFF）时，
/// 忽略大小写的文化比较等于 ASCII 大小写折叠，改用 <see cref="StringComparison.OrdinalIgnoreCase"/>（迁移指南“运行时库与字符串比较”，
/// 研究搜索优化用同一字符集）。
/// </para>
/// <para>
/// 1.6 使用当前区域性：捷克语、匈牙利语等的排序规则把 “ch”“cs” 之类当作一个字母，土耳其语的 I/i 不互为大小写，
/// 这些区域性中 ASCII 文本的文化比较与序数比较不同。所以只在已核对的区域性（固定区域性、英语、中文）上走快速路径，
/// 其余区域性一律按文化比较，结果与 1.6 相同。
/// </para>
/// </remarks>
internal static class CorporateSearchMatcher
{
    // StaticCacheLifecycle: 进程级，记录上次检查的区域性对象及其是否允许序数快速路径；区域性对象变化时重算，不含游戏状态。
    private static CultureInfo? checkedCulture;
    private static bool checkedAllowsOrdinal;

    /// <summary><paramref name="text"/> 只含快速路径字符集中的字符（ASCII 字母、数字、空格与 CJK 统一表意文字）。</summary>
    internal static bool IsSimple(string text)
    {
        foreach (char character in text)
        {
            if ((character >= 'A' && character <= 'Z')
                || (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character == ' '
                || (character >= '㐀' && character <= '䶿')
                || (character >= '一' && character <= '鿿'))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>该区域性中，快速路径字符集上的忽略大小写文化比较与 ASCII 大小写折叠相同（已核对：固定区域性、英语、中文）。</summary>
    internal static bool AllowsOrdinal(CultureInfo culture)
    {
        if (ReferenceEquals(culture, checkedCulture))
        {
            return checkedAllowsOrdinal;
        }

        string language = culture.TwoLetterISOLanguageName;
        bool allows = culture.Name.Length == 0
            || string.Equals(language, "en", StringComparison.Ordinal)
            || string.Equals(language, "zh", StringComparison.Ordinal);
        checkedCulture = culture;
        checkedAllowsOrdinal = allows;
        return allows;
    }

    /// <summary>以当前区域性判断 <paramref name="label"/> 是否包含 <paramref name="query"/>（忽略大小写）。</summary>
    internal static bool Contains(string label, string query)
        => Contains(label, IsSimple(label), query, IsSimple(query), CultureInfo.CurrentCulture);

    /// <summary>
    /// 同 <see cref="Contains(string, string)"/>，但由调用方提供预先算好的 <see cref="IsSimple"/> 结果与区域性
    /// （目录索引为每个标签只算一次）。
    /// </summary>
    internal static bool Contains(string label, bool labelIsSimple, string query, bool queryIsSimple, CultureInfo culture)
    {
        if (labelIsSimple && queryIsSimple && AllowsOrdinal(culture))
        {
            return label.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        return culture.CompareInfo.IndexOf(label, query, CompareOptions.IgnoreCase) >= 0;
    }
}
