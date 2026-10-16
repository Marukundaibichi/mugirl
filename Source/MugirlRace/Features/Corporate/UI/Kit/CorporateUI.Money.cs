namespace Mugirl.Features.Corporate;

internal static partial class CorporateUI
{
    private const int MoneyCacheLimit = 1024;

    // StaticCacheLifecycle: 进程级金额文本缓存。键为金额的位模式（+0 与 -0 分开，结果与直接格式化逐字相同）；
    // 当前语言对象变化（选项中切换语言）时连同白银单位一起清空，条目超过上限也清空。不含游戏对象。
    private static readonly Dictionary<int, string> moneyTexts = [];
    private static LoadedLanguage? moneyLanguage;
    private static bool moneyInitialized;
    private static string silverSuffix = string.Empty;

    /// <summary>金额文本：当前区域性的千分位整数 + 空格 + 白银单位（与 1.6 相同的 <c>ToString("N0")</c>）。</summary>
    internal static string Money(float value)
    {
        LoadedLanguage? language = LanguageDatabase.activeLanguage;
        if (!moneyInitialized || !ReferenceEquals(language, moneyLanguage))
        {
            // 1.6 的单位只在进程内翻译一次，切换语言后仍显示旧语言；按语言对象重新翻译。
            moneyInitialized = true;
            moneyLanguage = language;
            silverSuffix = "Mugirl.CorporateUI.Silver".Translate().ToString();
            moneyTexts.Clear();
        }

        int key = BitConverter.SingleToInt32Bits(value);
        if (moneyTexts.TryGetValue(key, out string? text))
        {
            return text;
        }

        if (moneyTexts.Count >= MoneyCacheLimit)
        {
            moneyTexts.Clear();
        }

        text = value.ToString("N0") + " " + silverSuffix;
        moneyTexts.Add(key, text);
        return text;
    }
}
