using System.Text.Encodings.Web;
using System.Text.Json;

namespace MugirlRace.Validation.Reporting;

/// <summary>
/// 运行检查、打印结果并写 JSON 报告。输出是确定的（序数排序），两次运行可逐行比较；tools/Validate.ps1 在两个运行时之间
/// 比较 <see cref="Finding.Key"/>。
/// </summary>
internal sealed class Report
{
    // 失败与警告总是打印；每项检查超过这个数量的信息行只写进 JSON 报告。
    private const int MaxConsoleInfos = 12;

    private readonly List<CheckResult> results = [];
    private readonly List<Finding> findings = [];
    private readonly SortedDictionary<string, string> environment = new(StringComparer.Ordinal);

    /// <summary>每项检查运行前以检查 id 调用（日志捕获用它给记录打标签）。</summary>
    public Action<string>? PhaseChanged { get; set; }

    public bool AnyFailure => results.Any(result => result.Status == CheckStatus.Failed);

    public IReadOnlyList<CheckResult> Results => results;

    /// <summary>记录一条环境信息（运行时、文件指纹），写入报告头。</summary>
    public void Environment(string name, string value)
    {
        environment[name] = value;
        Console.WriteLine($"  {name}: {value}");
    }

    /// <summary>把 <paramref name="body"/> 作为检查 <paramref name="id"/> 运行。异常只让这一项失败。</summary>
    public void Run(string id, string title, Action<CheckRecorder> body)
    {
        Console.WriteLine();
        Console.WriteLine($"== [{id}] {title}");
        PhaseChanged?.Invoke(id);
        var recorder = new CheckRecorder(id);
        try
        {
            body(recorder);
        }
        catch (Exception exception)
        {
            recorder.Fail("(check aborted)", "The check threw instead of completing: " + exception);
        }

        Complete(id, title, recorder);
    }

    /// <summary>加入在别处算好的检查结果。</summary>
    public void Complete(string id, string title, CheckRecorder recorder)
    {
        int infos = 0;
        foreach (Finding finding in recorder.Findings.OrderByDescending(f => f.Severity).ThenBy(f => f.Subject, StringComparer.Ordinal))
        {
            if (finding.Severity == Severity.Info && ++infos > MaxConsoleInfos)
            {
                continue;
            }

            string label = finding.Severity switch
            {
                Severity.Failure => "FAIL",
                Severity.Warning => "WARN",
                _ => "info",
            };
            string location = finding.Location is null ? "" : $"  [{finding.Location}]";
            Console.WriteLine($"   {label}  {finding.Subject}: {finding.Message}{location}");
        }

        if (infos > MaxConsoleInfos)
        {
            Console.WriteLine($"   info  … {infos - MaxConsoleInfos} more information line(s) in the JSON report (--report).");
        }

        CheckStatus status = recorder.FailureCount > 0 ? CheckStatus.Failed
            : recorder.SkipReason is not null ? CheckStatus.Skipped
            : CheckStatus.Passed;
        string summary = status switch
        {
            CheckStatus.Failed => $"{recorder.FailureCount} failure(s)" + (recorder.Summary is null ? "" : "; " + recorder.Summary),
            CheckStatus.Skipped => recorder.SkipReason!,
            _ => recorder.Summary ?? "ok",
        };
        if (recorder.WarningCount > 0)
        {
            summary += $" ({recorder.WarningCount} warning(s))";
        }

        Console.WriteLine($"{StatusLabel(status)} [{id}] {summary}");
        results.Add(new CheckResult(id, title, status, summary));
        findings.AddRange(recorder.Findings);
    }

    /// <summary>打印结尾汇总表。</summary>
    public void PrintSummary()
    {
        Console.WriteLine();
        Console.WriteLine("== Summary");
        foreach (CheckResult result in results)
        {
            Console.WriteLine($"  {StatusLabel(result.Status),-4} {result.Id,-26} {result.Summary}");
        }

        int failed = results.Count(result => result.Status == CheckStatus.Failed);
        Console.WriteLine();
        Console.WriteLine(failed == 0 ? $"RESULT: PASS ({results.Count} checks)" : $"RESULT: FAIL ({failed} of {results.Count} checks failed)");
    }

    public void WriteJson(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        writer.WriteStartObject();
        writer.WriteString("result", AnyFailure ? "fail" : "pass");
        writer.WriteStartObject("environment");
        foreach ((string name, string value) in environment)
        {
            writer.WriteString(name, value);
        }

        writer.WriteEndObject();
        writer.WriteStartArray("checks");
        foreach (CheckResult result in results.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", result.Id);
            writer.WriteString("title", result.Title);
            writer.WriteString("status", result.Status.ToString().ToLowerInvariant());
            writer.WriteString("summary", result.Summary);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("findings");
        foreach (Finding finding in findings.OrderBy(f => f.Key, StringComparer.Ordinal).ThenBy(f => f.Message, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("key", finding.Key);
            writer.WriteString("check", finding.Check);
            writer.WriteString("severity", finding.Severity.ToString().ToLowerInvariant());
            writer.WriteString("subject", finding.Subject);
            writer.WriteString("message", finding.Message);
            if (finding.Location is not null)
            {
                writer.WriteString("location", finding.Location);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static string StatusLabel(CheckStatus status) => status switch
    {
        CheckStatus.Passed => "PASS",
        CheckStatus.Failed => "FAIL",
        _ => "SKIP",
    };
}
