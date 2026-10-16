using Microsoft.Extensions.Logging;

namespace Mugirl.ShaderCheck;

/// <summary>一条引擎日志。</summary>
/// <param name="Level">严重级别。</param>
/// <param name="Category">引擎日志类别（Waylith 的渲染日志为 "Vulkan"）。</param>
/// <param name="Message">格式化后的文本。</param>
internal sealed record CapturedLogEntry(LogLevel Level, string Category, string Message)
{
    /// <summary>警告及以上：引擎报告着色器损坏的方式。</summary>
    public bool IsProblem => Level >= LogLevel.Warning;

    /// <inheritdoc/>
    public override string ToString() => $"[{Level} {Category}] {Message}";
}
