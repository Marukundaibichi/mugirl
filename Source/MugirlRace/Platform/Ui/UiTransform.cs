namespace Mugirl.Platform.Ui;

/// <summary>
/// 让一段任意绘制（多次 <c>Widgets.DrawBoxSolid</c>、<see cref="UiDraw"/> 等）绕某点旋转。只旋转单张纹理时直接用
/// Verse 的 <c>Widgets.DrawTextureRotated</c>，不需要本类。
/// </summary>
/// <remarks>
/// 旋转本身调用 Verse 的 <c>UI.RotateAroundPivot</c>，它与 <c>Widgets.DrawTextureRotated</c> 一样按 <c>Prefs.UIScale</c> 换算支点。
/// 1.6 的 <c>GUIUtility.RotateAroundPivot(angle, center)</c> 没有换算，界面缩放不为 1 时支点偏移；改用本类后支点始终正确。
/// 只用于绘制；旋转区域内不要放需要点击判定的控件。
/// </remarks>
public static class UiTransform
{
    /// <summary>
    /// 绕 <paramref name="pivot"/>（当前 GUI 组的局部坐标）顺时针旋转 <paramref name="angleDegrees"/> 度，直到作用域结束。
    /// 替代 <c>Matrix4x4 m = GUI.matrix; GUIUtility.RotateAroundPivot(angle, pivot); …; GUI.matrix = m;</c>。可以嵌套。
    /// </summary>
    public static RotationScope RotateAroundPivot(float angleDegrees, Vector2 pivot)
    {
        RotationScope scope = new(CurrentMatrix);
        UI.RotateAroundPivot(angleDegrees, pivot.ToUnity());
        return scope;
    }

    // Verse 只在 Widgets.DrawTextureRotated 内部保存并恢复 GUI.matrix，没有给任意绘制使用的旋转作用域。
#pragma warning disable RS0030
    private static UMatrix4x4 CurrentMatrix
    {
        get => UnityEngine.GUI.matrix;
        set => UnityEngine.GUI.matrix = value;
    }
#pragma warning restore RS0030

    /// <summary>由 <see cref="RotateAroundPivot"/> 返回；<c>using</c> 结束时恢复进入前的 GUI 矩阵。<c>default</c> 实例不做任何事。</summary>
    public readonly ref struct RotationScope
    {
        private readonly UMatrix4x4 previous;
        private readonly bool active;

        internal RotationScope(UMatrix4x4 previous)
        {
            this.previous = previous;
            active = true;
        }

        /// <summary>恢复进入作用域前的 GUI 矩阵。</summary>
        public void Dispose()
        {
            if (active)
            {
                CurrentMatrix = previous;
            }
        }
    }
}
