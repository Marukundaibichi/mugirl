using RimWorld.QuestGen;

namespace Mugirl.Features.Corporate;

// 巨企现场合同在原版任务列表中的外壳：只记录合同编号，进度与结算都由 CorporateNetwork 管理。
// 类名由 XML（QuestScriptDef root、SitePartDef workerClass）与存档引用，不能改名。
public class QuestNode_Root_CorporateMission : QuestNode
{
    protected override bool TestRunInt(Slate slate) => CorporateNetwork.Current?.FindMission(slate.Get<int>("corporateMissionId")) != null;

    protected override void RunInt()
    {
        QuestGen.quest.AddPart(new QuestPart_CorporateMission { missionId = QuestGen.slate.Get<int>("corporateMissionId") });
    }
}
