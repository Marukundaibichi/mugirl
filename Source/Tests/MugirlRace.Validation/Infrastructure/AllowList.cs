using MugirlRace.Validation.Reporting;

namespace MugirlRace.Validation.Infrastructure;

/// <summary>
/// <c>Baselines/</c> 中人工审阅的例外清单。格式：每行 <c>key&lt;TAB&gt;reason</c>；<c>#</c> 开头的行和空行忽略。
/// 每条必须写理由；不再匹配任何对象的条目视为过期并判失败，否则清单会随时间悄悄放宽。
/// </summary>
/// <remarks>
/// 理由以 <see cref="ReviewPrefix"/> 开头的条目由 <c>--update-baselines</c> 生成，仍然放行，但作为警告报告，直到有人换成真实理由。
/// </remarks>
internal sealed class AllowList
{
    /// <summary>自动生成、尚待人工审阅的理由前缀。</summary>
    public const string ReviewPrefix = "REVIEW:";

    private readonly Dictionary<string, string> entries;
    private readonly HashSet<string> used = new(StringComparer.Ordinal);
    private readonly List<string> malformed;
    private readonly List<string> pendingReview = [];
    private bool malformedReported;

    private AllowList(string directory, string fileName, Dictionary<string, string> entries, List<string> malformed)
    {
        Directory = directory;
        FileName = fileName;
        this.entries = entries;
        this.malformed = malformed;
    }

    public string Directory { get; }

    public string FileName { get; }

    public IReadOnlyDictionary<string, string> Entries => entries;

    /// <summary>从 <paramref name="directory"/> 读取 <paramref name="fileName"/>；文件不存在视为空清单。</summary>
    public static AllowList Load(string directory, string fileName)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var malformed = new List<string>();
        string path = Path.Combine(directory, fileName);
        if (File.Exists(path))
        {
            int number = 0;
            foreach (string raw in File.ReadLines(path))
            {
                number++;
                string line = raw.TrimEnd();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                int tab = line.LastIndexOf('\t');
                string key = tab < 0 ? line : line[..tab].Trim();
                string reason = tab < 0 ? "" : line[(tab + 1)..].Trim();
                if (reason.Length == 0)
                {
                    malformed.Add($"line {number}: '{key}' has no reason (format: key<TAB>reason)");
                }
                else if (!entries.TryAdd(key, reason))
                {
                    malformed.Add($"line {number}: duplicate entry '{key}'");
                }
            }
        }

        return new AllowList(directory, fileName, entries, malformed);
    }

    /// <summary>列出 <paramref name="key"/> 时返回 true，并记为已使用。</summary>
    public bool Allows(string key)
    {
        if (entries.ContainsKey(key))
        {
            used.Add(key);
            return true;
        }

        return false;
    }

    public string? ReasonFor(string key) => entries.GetValueOrDefault(key);

    /// <summary>为 true 时（<c>--update-baselines</c>）<see cref="AllowsOrReview"/> 把缺失的键记为待审条目而不是拒绝。</summary>
    public bool AddsMissingForReview { get; set; }

    /// <summary>
    /// 同 <see cref="Allows"/>；缺失且 <see cref="AddsMissingForReview"/> 时把 <paramref name="key"/> 记下，稍后由
    /// <see cref="WritePendingReview"/> 以 <see cref="ReviewPrefix"/> 理由追加到文件，本次运行视为放行。
    /// </summary>
    public bool AllowsOrReview(string key, string why)
    {
        if (Allows(key))
        {
            return true;
        }

        if (!AddsMissingForReview)
        {
            return false;
        }

        if (entries.TryAdd(key, ReviewPrefix + " " + why))
        {
            pendingReview.Add(key);
        }

        used.Add(key);
        return true;
    }

    /// <summary>把待审条目追加到文件末尾（保留原有内容与注释），返回追加的条数。</summary>
    public int WritePendingReview()
    {
        int count = pendingReview.Count;
        if (count > 0)
        {
            // 与手写条目一样用 LF；原文件末尾缺换行时先补上，追加的条目不会接在最后一行之后。
            string path = Path.Combine(Directory, FileName);
            string existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            string separator = existing.Length == 0 || existing.EndsWith('\n') ? string.Empty : "\n";
            IEnumerable<string> lines = pendingReview.Order(StringComparer.Ordinal).Select(key => key + "\t" + entries[key]);
            File.AppendAllText(path, separator + string.Join('\n', lines) + "\n");
            pendingReview.Clear();
        }

        return count;
    }

    /// <summary>报告格式错误、过期条目与待审条目。在检查查询完所有候选之后调用一次。</summary>
    public void ReportProblems(CheckRecorder recorder) => ReportProblems(recorder, _ => true);

    /// <summary>同 <see cref="ReportProblems(CheckRecorder)"/>，只针对共享清单中属于当前检查的条目。</summary>
    public void ReportProblems(CheckRecorder recorder, Func<string, bool> belongsToCheck)
    {
        if (!malformedReported)
        {
            malformedReported = true;
            foreach (string problem in malformed)
            {
                recorder.Fail("Baselines/" + FileName, problem);
            }
        }

        foreach (string key in entries.Keys.Where(key => belongsToCheck(key)).Order(StringComparer.Ordinal))
        {
            string subject = "Baselines/" + FileName + ": " + key.Replace('\t', ' ');
            if (!used.Contains(key))
            {
                recorder.Fail(subject, "Stale allow-list entry: nothing matches it any more. Remove it.");
            }
            else if (entries[key].StartsWith(ReviewPrefix, StringComparison.Ordinal))
            {
                recorder.Warn(subject, "Generated entry awaiting review: replace the reason with why the exception is safe, or fix the code.");
            }
        }
    }
}
