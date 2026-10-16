namespace Mugirl.ShaderCheck;

/// <summary>检查的控制台报告。每个失败都计数，退出码由失败数决定。</summary>
internal sealed class CheckReport
{
    private readonly List<string> failures = [];

    /// <summary>目前记录的失败数。</summary>
    public int FailureCount => failures.Count;

    /// <summary>开始一个报告小节。</summary>
    public static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("== " + title);
    }

    /// <summary>输出一行信息。</summary>
    public static void Info(string message) => Console.WriteLine("     " + message);

    /// <summary>输出一个通过项。</summary>
    public static void Pass(string message) => Console.WriteLine("  ok " + message);

    /// <summary>记录并输出一个失败。</summary>
    public void Fail(string message)
    {
        failures.Add(message);
        Console.WriteLine("FAIL " + message);
    }

    /// <summary>输出汇总并返回进程退出码。</summary>
    public int Finish()
    {
        Console.WriteLine();
        if (failures.Count == 0)
        {
            Console.WriteLine("ShaderCheck: ALL CHECKS PASSED");
            return 0;
        }

        Console.WriteLine($"ShaderCheck: {failures.Count} FAILURE(S)");
        foreach (string failure in failures)
        {
            Console.WriteLine("  - " + failure);
        }

        return 1;
    }
}
