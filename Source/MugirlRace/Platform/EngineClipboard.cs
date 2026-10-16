using WaylithClipboard = Waylith.Input.Clipboard;

namespace Mugirl.Platform;

/// <summary>
/// 系统剪贴板，替代 <c>GUIUtility.systemCopyBuffer</c>。两者底层都是 <c>Glfw.Clipboard</c>，行为相同。
/// </summary>
public static class EngineClipboard
{
    /// <summary>剪贴板文本。读取时没有文本得空串；写入 null 等同写入空串。只在主线程访问。</summary>
    public static string Text
    {
        get => WaylithClipboard.Text;
        set => WaylithClipboard.Text = value ?? string.Empty;
    }
}
