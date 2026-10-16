using System.Text.RegularExpressions;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Reporting;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// C. Mod 的英文与简体中文 Keyed 文件定义相同的键，每种语言每个键只定义一次（重复是加载错误“Duplicate keyed translation key”）。
/// 编号占位符（<c>{0}</c>、<c>{1}</c>）两种语言必须一致（README：中英 key 与占位符一致）；具名占位符（<c>{PAWN_labelShort}</c>）
/// 不一致只是警告，因为中文可以合理省略代词类占位符。
/// </summary>
internal static partial class KeyedParityCheck
{
    public static void Run(Report report, ContentModel content)
        => report.Run("C.keyed", "English and Simplified Chinese Keyed key sets and placeholders of the mod match, without duplicates", recorder => Check(recorder, content));

    private static void Check(CheckRecorder recorder, ContentModel content)
    {
        ContentPack mod = content.Catalog.UnderTest;
        foreach (string problem in content.Keyed.Problems.Where(p => p.StartsWith(mod.RootDir, StringComparison.OrdinalIgnoreCase)))
        {
            recorder.Fail(XmlPaths.File(mod.RootDir, problem.Split(": ", 2)[0]), "Malformed Keyed file: " + problem.Split(": ", 2)[^1]);
        }

        var keySets = new Dictionary<string, IReadOnlyDictionary<string, List<KeyedEntry>>>(StringComparer.Ordinal);
        foreach (string language in KeyedIndex.Languages.Keys)
        {
            IReadOnlyDictionary<string, List<KeyedEntry>> keys = content.Keyed.KeysOf(language, mod);
            keySets[language] = keys;
            foreach ((string key, List<KeyedEntry> entries) in keys.Where(pair => pair.Value.Count > 1).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                recorder.Fail($"{key} ({language})", "Defined more than once: " + string.Join(", ", entries.Select(e => XmlPaths.File(mod.RootDir, e.File))));
            }
        }

        IReadOnlyDictionary<string, List<KeyedEntry>> english = keySets["English"];
        IReadOnlyDictionary<string, List<KeyedEntry>> chinese = keySets["ChineseSimplified"];
        foreach (string key in english.Keys.Where(key => !chinese.ContainsKey(key)).Order(StringComparer.Ordinal))
        {
            recorder.Fail(key, "English Keyed key without a Simplified Chinese translation.", XmlPaths.File(mod.RootDir, english[key][0].File));
        }

        foreach (string key in chinese.Keys.Where(key => !english.ContainsKey(key)).Order(StringComparer.Ordinal))
        {
            recorder.Fail(key, "Simplified Chinese Keyed key without an English entry.", XmlPaths.File(mod.RootDir, chinese[key][0].File));
        }

        // 占位符在每种语言里都由同一组参数填充：只有一种语言有的编号占位符要么原样显示，要么丢信息。
        foreach (string key in english.Keys.Where(chinese.ContainsKey).Order(StringComparer.Ordinal))
        {
            SortedSet<string> englishTokens = Placeholders(english[key][0].Text);
            SortedSet<string> chineseTokens = Placeholders(chinese[key][0].Text);
            if (englishTokens.SetEquals(chineseTokens))
            {
                continue;
            }

            string message = $"Placeholders differ: English {Describe(englishTokens)}, Simplified Chinese {Describe(chineseTokens)}.";
            string location = XmlPaths.File(mod.RootDir, chinese[key][0].File);
            if (!new SortedSet<string>(englishTokens.Where(IsNumbered), StringComparer.Ordinal).SetEquals(chineseTokens.Where(IsNumbered)))
            {
                recorder.Fail(key, message + " Numbered placeholders must match.", location);
            }
            else
            {
                recorder.Warn(key, message, location);
            }
        }

        recorder.Summary = $"{english.Count} English and {chinese.Count} Simplified Chinese keys";
    }

    private static bool IsNumbered(string token) => token.Length > 2 && token[1..^1].All(char.IsAsciiDigit);

    private static SortedSet<string> Placeholders(string text)
        => new(PlaceholderPattern().Matches(text).Select(match => match.Value), StringComparer.Ordinal);

    private static string Describe(SortedSet<string> tokens) => tokens.Count == 0 ? "none" : string.Join(" ", tokens);

    [GeneratedRegex(@"\{[A-Za-z0-9_]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
