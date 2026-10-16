using System.Text.RegularExpressions;

namespace Mugirl.GameValidation.Core;

/// <summary>日志评判规则：被测 Mod 的记号、评审过的原版白名单、明确致命的消息与 "foreign" 归类。</summary>
/// <remarks>
/// <para>
/// 默认策略（<see cref="SuiteLogPolicy.ModMentions"/>）沿用原来的规则：提到被测 Mod（"mugirl"，测试模组自己的名字除外）的警告与错误失败。
/// </para>
/// <para>
/// <see cref="SuiteLogPolicy.AnyError"/>：任何错误都失败，按顺序例外为：宿主底噪（忽略）、评审过的白名单（计数）、
/// 只含 foreign 记号且不含任何被测 Mod 记号的错误（列出）。下列情况无论如何都失败：消息命中 <see cref="FatalPatterns"/>
/// （读档丢数据的典型消息，警告级别也算），或含有被测 Mod 的记号（"mugirl"/"moogirl"、旧名单与当前 Def 名中的任一记号）。
/// foreign 记号必须不在被测 Mod 的记号表中（证明它不属于任何雪牛娘构建），否则被丢弃并报告。
/// </para>
/// <para>只在主线程使用。</para>
/// </remarks>
internal sealed partial class LogPolicy
{
    /// <summary>读档丢失数据的典型消息（critique-validation §4.3）。</summary>
    public static readonly IReadOnlyList<string> FatalPatterns =
    [
        "Could not find class",
        "SaveableFromNode exception",
        "Could not load reference",
        "Could not resolve cross-reference",
        "Some verbs were null",
        "Cleaning up invalid job state",
        "Exception ticking",
    ];

    /// <summary>
    /// 评审过的原版底噪：读任何存档时都会出现、与被测 Mod 无关的错误。每条写明来源；新增条目需要评审。
    /// </summary>
    private static readonly (Regex Pattern, string Reason)[] ReviewedAllowlist =
    [
        // Ideo.ExposeData 给读入的意识形态补上快速测试世界生成时没有的隐藏仪式（hk SmokeTest 的 loadNoise）。
        (HiddenRitualPrecept(), "vanilla Ideo.ExposeData adds a hidden ritual precept the quick-test ideoligion lacked"),
    ];

    private readonly HashSet<string> modTokens = new(StringComparer.Ordinal);
    private readonly HashSet<string> foreignTokens = new(StringComparer.Ordinal);
    private readonly List<(Regex Pattern, string Reason)> extraAllowlist = [];

    public LogPolicy(IEnumerable<string> extraAllowlist, IEnumerable<string> foreign)
    {
        foreach (string pattern in extraAllowlist)
        {
            this.extraAllowlist.Add((new Regex(pattern, RegexOptions.CultureInvariant), "allowlisted by the run options: " + pattern));
        }

        foreach (string token in foreign)
        {
            foreignTokens.Add(token);
        }
    }

    /// <summary>被丢弃的 foreign 记号（同时是被测 Mod 的记号）；套件把它们写进报告。</summary>
    public List<string> RejectedForeignTokens { get; } = [];

    /// <summary>白名单命中次数，按原因。</summary>
    public Dictionary<string, int> AllowedCounts { get; } = new(StringComparer.Ordinal);

    public int ModTokenCount => modTokens.Count;

    /// <summary>加入被测 Mod 的记号（旧名单、升级清单中的旧名、当前 Def 名）。与之重合的 foreign 记号随即作废。</summary>
    public void AddModTokens(IEnumerable<string> tokens)
    {
        foreach (string token in tokens)
        {
            if (token.Length < 3)
            {
                // 过短的记号会误中普通单词；旧名单不含这样的名字。
                continue;
            }

            modTokens.Add(token);
            if (foreignTokens.Remove(token))
            {
                RejectedForeignTokens.Add(token);
            }
        }
    }

    public LogVerdict Classify(CapturedLog entry, SuiteLogPolicy policy, out string reason)
    {
        reason = string.Empty;
        if (!entry.IsWarningOrWorse || LogCapture.IsHostNoise(entry))
        {
            return LogVerdict.Ignored;
        }

        if (policy == SuiteLogPolicy.ModMentions)
        {
            if (entry.MentionsMod)
            {
                reason = "mentions the mod";
                return LogVerdict.Failure;
            }

            return entry.IsErrorOrWorse ? LogVerdict.Unattributed : LogVerdict.Ignored;
        }

        string text = entry.StackTrace is null ? entry.Message : entry.Message + "\n" + entry.StackTrace;
        if (FatalPatterns.FirstOrDefault(p => entry.Message.Contains(p, StringComparison.Ordinal)) is { } fatal)
        {
            reason = $"fatal message \"{fatal}\"";
            return LogVerdict.Failure;
        }

        if (entry.MentionsMod || entry.Message.Contains("moogirl", StringComparison.OrdinalIgnoreCase))
        {
            reason = "mentions the mod";
            return LogVerdict.Failure;
        }

        if (FirstToken(entry.Message, modTokens) is { } modToken)
        {
            reason = $"names the mod token {modToken}";
            return LogVerdict.Failure;
        }

        if (!entry.IsErrorOrWorse)
        {
            return LogVerdict.Ignored;
        }

        foreach ((Regex pattern, string why) in ReviewedAllowlist.Concat(extraAllowlist))
        {
            if (pattern.IsMatch(entry.Message))
            {
                AllowedCounts[why] = AllowedCounts.GetValueOrDefault(why) + 1;
                reason = why;
                return LogVerdict.Allowed;
            }
        }

        if (FirstToken(text, foreignTokens) is { } foreignToken)
        {
            reason = $"only names the foreign token {foreignToken}";
            return LogVerdict.Foreign;
        }

        reason = "any error fails in this suite";
        return LogVerdict.Failure;
    }

    /// <summary>
    /// 文本中第一个属于 <paramref name="tokens"/> 的记号。记号按标识符切分（含点号的类型全名、去掉末尾数字的 ThingID、
    /// <c>Thing_</c> 引用），不按子串匹配：旧名单里的短名不能误中更长的普通名字。
    /// </summary>
    public static string? FirstToken(string text, IReadOnlySet<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return null;
        }

        foreach (Match match in Identifier().Matches(text))
        {
            foreach (string candidate in Candidates(match.Value))
            {
                if (tokens.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates(string identifier)
    {
        yield return identifier;
        string trimmed = identifier.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        if (trimmed.Length > 0 && trimmed.Length < identifier.Length)
        {
            yield return trimmed;
        }

        if (identifier.StartsWith("Thing_", StringComparison.Ordinal) && identifier.Length > 6)
        {
            string id = identifier[6..];
            yield return id;
            yield return id.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        }

        int dot = identifier.LastIndexOf('.');
        if (dot > 0 && dot < identifier.Length - 1)
        {
            yield return identifier[(dot + 1)..];
        }
    }

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();

    [GeneratedRegex(@"^A hidden ritual precept was missing, adding: ", RegexOptions.CultureInvariant)]
    private static partial Regex HiddenRitualPrecept();
}
