using System.Collections.Concurrent;
using Brutal.Logging;
using Microsoft.Extensions.Logging;

namespace Mugirl.ShaderCheck;

/// <summary>
/// 捕获引擎日志（Brutal.Logging -> Microsoft.Extensions.Logging）。Waylith 在这里报告材质布局错误、阶段接口错误、场景抓取冲突与
/// 着色器编译失败；游戏中它们只进引擎日志，这里全部收集，检查据此判定失败。
/// </summary>
internal sealed class EngineLogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEntry> entries = new();

    /// <summary>把所有引擎日志导入一个新的捕获器。</summary>
    public static EngineLogCapture Install()
    {
        var capture = new EngineLogCapture();
        LogSystem.Initialize(builder => builder.AddProvider(capture).SetMinimumLevel(LogLevel.Trace));
        return capture;
    }

    /// <summary>取出并清空上次调用以来捕获的全部消息。</summary>
    public IReadOnlyList<CapturedLogEntry> Drain()
    {
        List<CapturedLogEntry> drained = [];
        while (entries.TryDequeue(out CapturedLogEntry? entry))
        {
            drained.Add(entry);
        }

        return drained;
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    private sealed class CapturingLogger(EngineLogCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            string message = formatter(state, exception);
            if (exception is not null)
            {
                message += " " + exception.Message;
            }

            owner.entries.Enqueue(new CapturedLogEntry(logLevel, category, message));
        }
    }
}
