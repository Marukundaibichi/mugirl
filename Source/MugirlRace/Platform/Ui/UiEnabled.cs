namespace Mugirl.Platform.Ui;

/// <summary>
/// 模组自定义控件的“可交互”环境开关，替代把 <c>GUI.enabled</c> 当作环境标志读写的写法。不调用兼容层。
/// </summary>
/// <remarks>
/// <para>
/// 1.7 中 <c>GUI.enabled</c> 没有任何效果：UnityStubs 的 <c>GUI.Button</c>、<c>GUI.TextField</c>（Verse <c>ButtonInvisible</c>、
/// <c>TextField</c> 的实现）与宿主都不读取它，原版只写不读。1.6 里它会让 Unity 控件停止响应并变灰，1.7 不会。所以：
/// </para>
/// <list type="bullet">
/// <item>要禁用 Verse 控件：用控件自带的参数（<c>Widgets.ButtonText(…, active: false)</c>），或干脆不调用输入控件、改画标签。</item>
/// <item>模组自己的控件读取 <see cref="IsEnabled"/>，语义与 1.6 读取 <c>GUI.enabled</c> 相同。</item>
/// </list>
/// <para>只能经 <see cref="Set"/>/<see cref="DisableIf"/> 返回的作用域修改，离开作用域时恢复进入前的值。只在主线程的 GUI 回调中使用。</para>
/// </remarks>
public static class UiEnabled
{
    // StaticCacheLifecycle: 只在 GUI 回调内由 Scope 成对修改，Dispose 即恢复；不含游戏状态，新建或读档无需清理。
    private static bool enabled = true;

    /// <summary>当前是否可交互；不在任何作用域内时为 true。</summary>
    public static bool IsEnabled => enabled;

    /// <summary>
    /// 把开关设为 <paramref name="value"/>，直到作用域结束（对应 1.6 的 <c>GUI.enabled = value</c> 加 finally 恢复）。
    /// 与 <c>GUI.enabled</c> 相同，可以在禁用区域内重新设为 true。
    /// </summary>
    public static Scope Set(bool value)
    {
        Scope scope = new(enabled);
        enabled = value;
        return scope;
    }

    /// <summary><paramref name="condition"/> 为 true 时禁用，否则保持当前值；对应 <c>if (condition) GUI.enabled = false;</c>。</summary>
    public static Scope DisableIf(bool condition) => Set(enabled && !condition);

    /// <summary>由 <see cref="Set"/> 返回；<c>using</c> 结束时恢复进入前的值。<c>default</c> 实例不做任何事。</summary>
    public readonly ref struct Scope
    {
        private readonly bool previous;
        private readonly bool active;

        internal Scope(bool previous)
        {
            this.previous = previous;
            active = true;
        }

        /// <summary>恢复进入作用域前的值。</summary>
        public void Dispose()
        {
            if (active)
            {
                enabled = previous;
            }
        }
    }
}
