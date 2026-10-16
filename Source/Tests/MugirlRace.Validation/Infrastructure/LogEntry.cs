namespace MugirlRace.Validation.Infrastructure;

/// <summary>捕获到的一次 <c>Verse.Log</c> 调用。</summary>
/// <param name="Level">消息、警告或错误。</param>
/// <param name="Text">日志文本。</param>
/// <param name="Origin">调用栈中第一个属于 Mod 或依赖的帧，否则为 <c>(game)</c>。</param>
/// <param name="Phase">当时正在运行的检查 id。</param>
/// <param name="OriginType"><paramref name="Origin"/> 帧所在的类型（游戏帧时为 <see langword="null"/>）。</param>
internal sealed record LogEntry(LogLevel Level, string Text, string Origin, string Phase, Type? OriginType);
