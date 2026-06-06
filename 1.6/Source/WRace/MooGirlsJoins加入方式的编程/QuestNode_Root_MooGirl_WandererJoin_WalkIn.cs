using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace MooGirl
{
    // 自定义任务节点类，继承自基础流浪者加入任务节点
    public class QuestNode_Root_MooGirl_WandererJoin_WalkIn : QuestNode_Root_WandererJoin_WalkIn
    {
        private string signalAccept; // 接受信号标识符
        private string signalReject; // 拒绝信号标识符

        // 重写生成Pawn的方法，创建符合特定条件的角色
        public override Pawn GeneratePawn()
        {
            // 逃亡奴隶保持无派系；敌对巨企派系只用于袭击，避免流浪者加入事件附带敌对关系。
            Faction faction = null;

            // 构造Pawn生成请求，定义角色生成参数
            PawnGenerationRequest request = new PawnGenerationRequest(
                MooGirl_DefOf.MooGirl_EscapeWanderSlave, // 使用自定义角色定义
                faction, // 设置所属派系
                PawnGenerationContext.NonPlayer, // 非玩家角色
                -1, // 随机种子
                forceGenerateNewPawn: true, // 强制生成新角色
                allowDead: false, // 不允许死亡
                allowDowned: true, // 允许倒地状态
                canGeneratePawnRelations: false, // 不生成亲属关系
                mustBeCapableOfViolence: true, // 必须能进行暴力行为
                forceAddFreeWarmLayerIfNeeded: false, // 不强制添加保暖衣物
                allowGay: true, // 允许同性恋
                allowPregnant: false, // 不允许怀孕
                forceRecruitable: true, // 强制可招募
                fixedGender: Gender.Female, // 固定女性
                developmentalStages: DevelopmentalStage.Adult // 仅成人
            );

            // 生成角色实例
            Pawn pawn = PawnGenerator.GeneratePawn(request);

            MooGirlApparelTagUtility.TryWearIdeoSuppressedKindApparel(pawn);

            return pawn; // 返回生成的角色
        }

        // 重写添加任务部件的方法，处理角色生成后的逻辑
        protected override void AddSpawnPawnQuestParts(Quest quest, Map map, Pawn pawn)
        {
            base.AddSpawnPawnQuestParts(quest, map, pawn); // 调用基类逻辑
            this.signalAccept = QuestGenUtility.HardcodedSignalWithQuestID("Accept"); // 设置接受信号
            this.signalReject = QuestGenUtility.HardcodedSignalWithQuestID("Reject"); // 设置拒绝信号
        }

        // 重写发送通知信件的方法，创建角色加入通知
        public override void SendLetter_NewTemp(Quest quest, Pawn pawn, Map map)
        {
            // 生成信件标题和内容（支持多语言）
            TaggedString taggedString = "MooGirl.LetterLabelWandererJoins".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
            TaggedString taggedString2 = "MooGirl.LetterWandererJoins".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);

            // 添加慈善信息到信件
            QuestNode_Root_WandererJoin_WalkIn.AppendCharityInfoToLetter("JoinerCharityInfo".Translate(pawn), ref taggedString);

            // 添加角色与殖民者关系信息
            PawnRelationUtility.TryAppendRelationsWithColonistsInfo(ref taggedString2, ref taggedString, pawn);

            // 如果是未成年人，添加年龄相关信息
            if (pawn.DevelopmentalStage.Juvenile())
            {
                string text = (pawn.ageTracker.AgeBiologicalYears * 3600000).ToStringTicksToPeriod(true, false, true, true, false);
                taggedString2 += "\n\n" + "MooGirl.RefugeePodCrash_Child".Translate(pawn.Named("PAWN"), text.Named("AGE"));
            }

            // 添加角色最佳技能信息
            QuestNode_Root_WandererJoin_WalkIn.ApplyBestSkillInfoToLetter(ref taggedString2, pawn);

            // 创建可选择是否接受的信件
            ChoiceLetter_AcceptJoiner choiceLetter_AcceptJoiner = (ChoiceLetter_AcceptJoiner)LetterMaker.MakeLetter(taggedString, taggedString2, LetterDefOf.AcceptJoiner, null, null);
            choiceLetter_AcceptJoiner.signalAccept = this.signalAccept; // 设置接受信号
            choiceLetter_AcceptJoiner.signalReject = this.signalReject; // 设置拒绝信号
            choiceLetter_AcceptJoiner.quest = quest; // 关联任务
            choiceLetter_AcceptJoiner.overrideMap = map; // 设置地图
            choiceLetter_AcceptJoiner.StartTimeout(60000); // 设置超时时间

            // 发送信件
            Find.LetterStack.ReceiveLetter(choiceLetter_AcceptJoiner, null, 0, true);
        }
    }
}
