using Verse;
using RimWorld.QuestGen;
using RimWorld;
using RimWorld.Planet;
using System;
using Verse.AI;
using System.Collections.Generic;
using System.Linq;

namespace MooGirl
{
    // 自定义任务节点：处理坠机逃生舱事件，生成特定角色并处理相关逻辑
    public class QuestNode_Root_MooGirl_OpeningPodCrash : QuestNode_Root_RefugeePodCrash
    {
        private const int MaxFactionlessGenerationAttempts = 20;

        // 生成自定义逃生者角色
        public override Pawn GeneratePawn()
        {
            // Escaped slaves are factionless; the hostile corporation faction is reserved for raids.
            Faction faction = null;

            // 配置角色生成参数
            PawnGenerationRequest request = new PawnGenerationRequest(
                MooGirl_DefOf.MooGirl_Beginning_Slave,  // 自定义角色定义
                faction,                                // 所属派系
                PawnGenerationContext.NonPlayer,        // 生成上下文
                -1,                                     // 固定生物年龄（-1表示随机）
                forceGenerateNewPawn: true,             // 强制生成新角色
                allowDead: false,                       // 不允许死亡
                allowDowned: true,                      // 允许倒地状态
                canGeneratePawnRelations: false,        // 不生成关系
                mustBeCapableOfViolence: true,          // 必须具备战斗能力
                forceAddFreeWarmLayerIfNeeded: false,   // 不强制添加保暖层
                allowGay: true,                         // 允许同性恋
                allowPregnant: false,                   // 不允许怀孕
                forceRecruitable: true,                 // 强制可招募
                validatorPostGear: p => p.Faction == null,
                fixedGender: Gender.Female,             // 固定女性
                developmentalStages: DevelopmentalStage.Adult // 成年阶段
            );

            Pawn pawn = GenerateFactionlessPawn(request);
            MooGirlRescueJoinUtility.PrepareRescueJoinPawn(pawn);

            MooGirl_IdeoUtility.AdoptPlayerPrimaryIdeo(pawn);

            // 检查是否允许服装要求（根据信仰系统）
            bool allowApparelRequirements = false;
            if (pawn.Ideo != null)
            {
                foreach (var meme in pawn.Ideo.memes)
                {
                    if (meme.preventApparelRequirements)
                    {
                        allowApparelRequirements = true;
                        break;
                    }
                }
            }

            // 如果允许服装要求且角色定义包含服装标签，则生成对应服装
            if (allowApparelRequirements && pawn.kindDef.apparelTags != null && pawn.kindDef.apparelTags.Count > 0)
            {
                List<string> apparelTags = pawn.kindDef.apparelTags.ToList();

                foreach (var tag in apparelTags)
                {
                    // 查找匹配标签的服装定义
                    var candidates = DefDatabase<ThingDef>.AllDefsListForReading
                        .Where(td => td.IsApparel
                                     && td.apparel != null
                                     && td.apparel.tags != null
                                     && td.apparel.tags.Contains(tag)
                                     && AdultContentUtility.IsAllowed(td))
                        .ToList();

                    if (candidates.Count > 0)
                    {
                        // 随机选择服装并实例化
                        ThingDef chosenDef = candidates.RandomElement();
                        Apparel newApparel;

                        if (chosenDef.MadeFromStuff)
                        {
                            ThingDef stuff = GenStuff.RandomStuffFor(chosenDef);
                            newApparel = (Apparel)ThingMaker.MakeThing(chosenDef, stuff);
                        }
                        else
                        {
                            newApparel = (Apparel)ThingMaker.MakeThing(chosenDef, null);
                        }

                        // 判断是否需要锁定服装（特殊类型）
                        bool shouldLock = newApparel is AdvancedSlaveApparel || newApparel is BrainWashSlaveApparel;

                        // 穿戴服装
                        pawn.apparel.Wear(newApparel, dropReplacedApparel: true, locked: shouldLock);
                    }
                }
            }

            // 添加自定义健康状态：行动不能症
            pawn.health.AddHediff(MooGirl_DefOf.MooGirl_Abasia);
            HealthUtility.DamageUntilDowned(pawn, true);

            return pawn;
        }

        private Pawn GenerateFactionlessPawn(PawnGenerationRequest request)
        {
            for (int i = 0; i < MaxFactionlessGenerationAttempts; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (pawn != null && pawn.Faction == null)
                {
                    return pawn;
                }

                DiscardGeneratedPawn(pawn);
            }

            Log.Error("[MooGirl] Failed to generate a factionless opening pod pawn after "
                + MaxFactionlessGenerationAttempts
                + " attempts. Check PawnKindDef or other pawn generation patches.");
            throw new InvalidOperationException("MooGirl opening pod pawn must be generated factionless.");
        }

        private void DiscardGeneratedPawn(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            if (Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.RemovePawn(pawn);
            }
            Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
        }

        // 在同一逃生舱中生成多个角色
        protected void AddSpawnPawnsInOnePod(Quest quest, Map map, Pawn[] pawns)
        {
            if (quest == null || map == null || pawns == null || pawns.Length == 0)
                return;

            quest.DropPods(
                map.Parent,
                pawns,
                sendStandardLetter: false,
                useTradeDropSpot: false,
                joinPlayer: false,
                makePrisoners: false,
                signalListenMode: QuestPart.SignalListenMode.OngoingOnly,
                destroyItemsOnCleanup: true,
                dropAllInSamePod: false,
                allowFogged: false,
                canRetargetAnyMap: false,
                faction: null
            );
        }

        // 执行任务主逻辑
        protected override void RunInt()
        {
            Quest quest = QuestGen.quest;
            Slate slate = QuestGen.slate;

            // 获取或生成地图
            if (!slate.TryGet<Map>("map", out var map))
            {
                bool canBeSpace = CanBeSpace;
                map = QuestGen_Get.GetMap(mustBeInfestable: false, null, canBeSpace);
            }

            // 如果不是太空地图，添加接受条件
            if (!CanBeSpace)
            {
                quest.AcceptanceRequirementNotSpace(map.Parent);
            }

            // 生成多个角色
            int pawnCount = 2;
            Pawn[] pawns = new Pawn[pawnCount];
            for (int i = 0; i < pawnCount; i++)
            {
                pawns[i] = GeneratePawn();
            }

            // 将所有角色放入同一逃生舱
            AddSpawnPawnsInOnePod(quest, map, pawns);

            // 保存角色到任务变量
            slate.Set("pawns", pawns);

            // 发送通知信件
            SendLetter_NewTemp(quest, pawns, map);

            // 设置任务信号处理
            string inSignalRescued = QuestGenUtility.HardcodedSignalWithQuestID("pawns.Rescued");
            string inSignalKilled = QuestGenUtility.HardcodedSignalWithQuestID("pawns.Killed");
            string inSignalLeftBehind = QuestGenUtility.HardcodedSignalWithQuestID("pawns.LeftBehind");
            string inSignalRecruited = QuestGenUtility.HardcodedSignalWithQuestID("pawns.Recruited");

            quest.AddPart(new QuestPart_MooGirlRescueJoin
            {
                inSignalRescued = inSignalRescued,
                inSignalRecruited = inSignalRecruited,
                signalListenMode = QuestPart.SignalListenMode.OngoingOnly,
                pawns = pawns.ToList()
            });

            // 角色死亡处理
            quest.End(QuestEndOutcome.Fail, 0, null, inSignalKilled);

            // 角色被遗弃处理
            quest.End(QuestEndOutcome.Fail, 0, null, inSignalLeftBehind);
        }

        // 发送自定义通知信件（支持多角色）
        public void SendLetter_NewTemp(Quest quest, Pawn[] pawns, Map map)
        {
            TaggedString label = "MooGirl.LetterLabelOpeningPodCrash".Translate();
            TaggedString text = "";

            // 多角色情况说明
            if (pawns.Length > 1)
            {
                text += "逃生舱坠毁，里面载有多位逃亡者。\n\n";
            }

            // 为每个角色生成描述文本
            foreach (var pawn in pawns)
            {
                // 基本描述
                text += "MooGirl.OpeningPodCrash".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
                text += "\n\n";

                // 派系关系描述
                if (pawn.Faction == null)
                    text += "MooGirl.OpeningPodCrash_Factionless".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
                else if (pawn.Faction.HostileTo(Faction.OfPlayer))
                    text += "MooGirl.OpeningPodCrash_Hostile".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
                else
                    text += "MooGirl.OpeningPodCrash_NonHostile".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);

                // 未成年角色特殊描述
                if (pawn.DevelopmentalStage.Juvenile())
                {
                    string arg = (pawn.ageTracker.AgeBiologicalYears * 3600000)
                        .ToStringTicksToPeriod(true, false, true, true, false);
                    text += "\n\n" + "MooGirl.OpeningPodCrash_Child".Translate(pawn.Named("PAWN"), arg.Named("AGE"));
                }

                // 附加慈善信息
                QuestNode_Root_WandererJoin_WalkIn.AppendCharityInfoToLetter("JoinerCharityInfo".Translate(pawn), ref text);

                // 附加殖民者关系信息
                PawnRelationUtility.TryAppendRelationsWithColonistsInfo(ref text, ref label, pawn);

                text += "\n\n"; // 分段
            }

            // 创建多目标查看器（指向所有角色）
            LookTargets lookTargets = new LookTargets(pawns);

            // 发送信件
            Find.LetterStack.ReceiveLetter(
                label,
                text,
                LetterDefOf.NeutralEvent,
                lookTargets, // 多目标指向
                null, null, null, null, 0, true
            );
        }
    }
}
