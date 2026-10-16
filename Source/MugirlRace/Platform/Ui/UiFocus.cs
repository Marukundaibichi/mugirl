namespace Mugirl.Platform.Ui;

/// <summary>
/// 文本框等控件的命名焦点。Verse 提供 <c>UI.FocusControl(name, window)</c> 与 <c>UI.UnfocusCurrentControl()</c>，
/// 但没有给下一个控件命名、查询当前焦点名的封装，这两件事集中在这里。
/// </summary>
/// <remarks>
/// 用法：<c>UiFocus.SetNextControlName(name); text = Widgets.TextField(rect, text);</c>，之后用 <see cref="IsFocused"/> 判断。
/// 名字只对紧接着的下一个控件生效；同一帧内重复的名字按出现次序分别登记，所以名字在一个窗口内应唯一。
/// 只需要“命名对话框 + 回车确认”时，优先继承原版 <c>Dialog_Rename&lt;T&gt;</c>。
/// </remarks>
public static class UiFocus
{
    // 下面三个 GUI 成员没有 Verse 封装：Verse 自己的文本框（DelayedTextField 等）也直接调用它们。
#pragma warning disable RS0030

    /// <summary>给紧接着绘制的下一个控件命名（<c>GUI.SetNextControlName</c>）。</summary>
    public static void SetNextControlName(string name) => UnityEngine.GUI.SetNextControlName(name);

    /// <summary>当前持有键盘焦点的控件名（<c>GUI.GetNameOfFocusedControl</c>）；没有命名焦点时为空串，从不为 null。</summary>
    public static string FocusedControlName => UnityEngine.GUI.GetNameOfFocusedControl() ?? string.Empty;

#pragma warning restore RS0030

    /// <summary>名为 <paramref name="name"/> 的控件持有键盘焦点（序数比较）。</summary>
    public static bool IsFocused(string name) => !string.IsNullOrEmpty(name) && string.Equals(FocusedControlName, name, StringComparison.Ordinal);
}
