using RimWorld.QuestGen;

namespace Mugirl.Features.Corporate;

// 开户主线在原版任务列表中的外壳；进度由 CorporateIntroduction 组件管理。
// 类名由 XML 与存档引用，不能改名。
public class QuestNode_Root_CorporateIntroduction : QuestNode
{
    protected override bool TestRunInt(Slate slate) => slate.Get<bool>("corporateIntroduction");

    protected override void RunInt()
    {
        QuestGen.quest.AddPart(new QuestPart_CorporateIntroduction());
    }
}
