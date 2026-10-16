namespace Mugirl.Features.Corporate;

/// <summary>
/// 巨企终端中的一个页面。页面持有自己的滚动位置、选择与按版本缓存的视图数据，
/// 通过 <see cref="Terminal"/> 使用窗口提供的服务（反馈、菜单、确认、页面过渡、关闭后动作与每帧快照）。
/// </summary>
/// <remarks>
/// 窗口是 forcePause 模态：派生数据只在玩家操作后变化。所有操作结果都经 <see cref="Dialog_CorporateTerminal.ShowFeedback"/>
/// 递增 <see cref="Dialog_CorporateTerminal.CacheVersion"/>，各页面按这个版本号重建缓存。
/// </remarks>
internal abstract class CorporateTerminalPage(Dialog_CorporateTerminal terminal)
{
    protected Dialog_CorporateTerminal Terminal { get; } = terminal;

    protected CorporateTradeContext Context => Terminal.Context;

    /// <summary>在页面区域（局部坐标，原点为左上角）中绘制；只在网络存在、连接有效且介绍章节已完成时调用。</summary>
    internal abstract void Draw(Rect rect, CorporateNetwork network);

    /// <summary>窗口关闭后释放页面持有的临时物品与缓存。</summary>
    internal virtual void PostClose()
    {
    }
}
