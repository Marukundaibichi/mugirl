namespace Mugirl.SaveUpgrade;

/// <summary>保留既有隐藏事件的不可触发语义；旧队列和历史引用不进入当前事件领域。</summary>
public sealed class IncidentWorker_RetiredEvent : IncidentWorker
{
    protected override bool CanFireNowSub(IncidentParms parms) => false;

    protected override bool TryExecuteWorker(IncidentParms parms) => false;
}
