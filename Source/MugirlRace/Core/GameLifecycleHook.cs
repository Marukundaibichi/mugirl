namespace Mugirl.Core;

/// <summary>一个已登记的生命周期处理器。</summary>
/// <param name="Id">完整方法名（<c>类型全名.方法名</c>），同顺序值时按它排序。</param>
internal sealed record GameLifecycleHook(GameLifecycleEvent Event, int Order, string Id, Action Callback);
