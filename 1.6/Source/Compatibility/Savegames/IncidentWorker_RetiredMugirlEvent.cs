using RimWorld;

namespace Mugirl
{
    // 已删除玩法的旧 Def 仅保留存档引用；即使旧队列强制执行，也不再生成人物。
    public class IncidentWorker_RetiredMugirlEvent : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) => false;
        protected override bool TryExecuteWorker(IncidentParms parms) => false;
    }
}
