namespace Mugirl.Platform.Ui;

/// <summary>
/// 读取当前的 GUI 颜色。改写颜色一律用 Verse 的 <c>TextBlock(Color)</c> 作用域（结束时恢复原颜色），
/// 或直接把颜色交给 <c>Widgets.DrawBoxSolid</c>、<c>Widgets.ButtonImage</c>、<see cref="UiDraw"/>。
/// </summary>
public static class UiColor
{
    /// <summary>
    /// 当前 <c>GUI.color</c>。用于继承外层透明度（如 <c>c.a *= UiColor.Current.a</c>）或在作用域内基于外层颜色计算新颜色。
    /// </summary>
    public static Color Current
    {
        get
        {
            // Verse 没有读取当前 GUI 颜色的封装（TextBlock 只在内部保存）；这里只读不写。
#pragma warning disable RS0030
            return UnityEngine.GUI.color;
#pragma warning restore RS0030
        }
    }
}
