using System.IO;
using System.Text;

namespace Mugirl.GameValidation.Core;

/// <summary>结果记录：每条结果一行，同时写入报告文件与 Verse 日志（即标准输出）。</summary>
/// <remarks>
/// 行格式供 tools/Test-InGame.ps1 解析：<c>[Mugirl.GameValidation] ok|FAIL|skip|info &lt;范围&gt;: &lt;内容&gt;</c>，
/// 最后一行为 <c>[Mugirl.GameValidation] DONE ok=… failed=… skipped=…</c>。多行详情的后续行以四个空格开头。
/// 只在主线程调用。
/// </remarks>
internal sealed class ValidationReport
{
    public const string Prefix = "[Mugirl.GameValidation]";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly string? path;

    public ValidationReport(string? path)
    {
        this.path = path;
        if (path is null)
        {
            return;
        }

        try
        {
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(path, string.Empty, Utf8);
        }
        catch (Exception ex)
        {
            this.path = null;
            Log.Message($"{Prefix} info setup: cannot create the report file {path}: {ex.Message}");
        }
    }

    public int Ok { get; private set; }

    public int Failed { get; private set; }

    public int Skipped { get; private set; }

    /// <summary>当前范围，形如 "startup/assembly"；步骤之外为 "setup"、"session" 等。</summary>
    public string Scope { get; private set; } = "setup";

    /// <summary>当前范围内记录的失败数。</summary>
    public int ScopeFailures { get; private set; }

    public bool WritesFile => path is not null;

    /// <summary>切换范围；日志捕获按同一范围标注之后收到的引擎与 Verse 日志。</summary>
    public void BeginScope(string scope, bool isStep)
    {
        Scope = scope;
        ScopeFailures = 0;
        LogCapture.SetScope(scope, isStep);
    }

    public void Pass(string what, string? detail = null)
    {
        Ok++;
        Write(detail is null ? $"ok {Scope}: {what}" : $"ok {Scope}: {what}: {detail}");
    }

    public void Fail(string what, string detail)
    {
        Failed++;
        ScopeFailures++;
        Write($"FAIL {Scope}: {what}: {detail}");
    }

    public void Skip(string what, string reason)
    {
        Skipped++;
        Write($"skip {Scope}: {what}: {reason}");
    }

    public void Info(string text) => Write($"info {Scope}: {text}");

    /// <summary>记录通过或失败；<paramref name="detail"/> 只在失败时求值。</summary>
    public bool Check(bool condition, string what, Func<string> detail)
    {
        if (condition)
        {
            Pass(what);
        }
        else
        {
            Fail(what, detail());
        }

        return condition;
    }

    public void Done(TimeSpan elapsed)
        => Write($"DONE ok={Ok} failed={Failed} skipped={Skipped} seconds={elapsed.TotalSeconds:0}");

    private void Write(string text)
    {
        string line = Prefix + " " + text;
        // Verse 在一万条消息后停止记录；嘈杂的运行不能吞掉报告行。
        Log.ResetMessageCount();
        Log.Message(line);
        if (path is null)
        {
            return;
        }

        try
        {
            File.AppendAllText(path, Indent(line) + "\n", Utf8);
        }
        catch (IOException)
        {
            // 报告文件暂时被占用时，这一行仍在标准输出中；脚本在报告缺行时回退到标准输出。
        }
    }

    // 多行详情（异常堆栈）在报告文件中缩进，使每条结果仍以前缀开头、可逐行解析。
    private static string Indent(string text)
        => text.Contains('\n', StringComparison.Ordinal)
            ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\n    ", StringComparison.Ordinal)
            : text;
}
