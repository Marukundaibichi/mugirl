using WaylithShell = Waylith.Utils.Shell;

namespace Mugirl.Platform;

/// <summary>
/// 用系统默认程序打开网址、文件或文件夹，替代 <c>Application.OpenURL</c>。
/// </summary>
/// <remarks>
/// <c>Waylith.Utils.Shell.Open</c> 与 UnityStubs 的 <c>Application.OpenURL</c> 实现相同：Windows 上 <c>UseShellExecute</c>，
/// macOS 用 <c>open</c>，Linux 用 <c>xdg-open</c>；空白参数直接忽略，启动失败只写引擎日志、不抛异常。
/// </remarks>
public static class EngineShell
{
    /// <summary>打开网址（<c>https://…</c>、<c>file://…</c>）或本地文件、文件夹的完整路径。</summary>
    public static void Open(string target) => WaylithShell.Open(target);

    /// <summary>
    /// 在系统文件管理器中打开文件夹。传入完整路径（如 <c>GenFilePaths</c> 给出的目录）；目录不存在时由系统报错，
    /// 需要时调用方先 <c>Directory.CreateDirectory</c>。与旧写法 <c>Application.OpenURL(new Uri(dir).AbsoluteUri)</c> 效果相同。
    /// </summary>
    public static void OpenFolder(string directory) => WaylithShell.Open(directory);
}
