using Verse;
using RimWorld.QuestGen;
using RimWorld;

namespace MooGirl
{
    public class QuestNode_Root_MooGirl_RefugeePodCrash : QuestNode_Root_RefugeePodCrash
    {
        private const int MaxDownedGenerationAttempts = 10;

        public override Pawn GeneratePawn()
        {
            // 逃亡奴隶保持无派系；敌对巨企派系只用于袭击，避免救援任务变成战斗事件。
            Faction faction = null;

            // 创建一个PawnGenerationRequest对象，详细定义了生成的pawn的属性和条件。
            PawnGenerationRequest request = new PawnGenerationRequest(
                // 指定要生成的Pawn的种类
                MooGirl_DefOf.MooGirl_EscapeSpaceSlave,
                // 指定该Pawn所属的派系
                faction,
                // 指定生成上下文为非玩家角色
                PawnGenerationContext.NonPlayer,
                // 指定的种子值，这里使用-1表示不使用特定的种子，即随机生成
                -1,
                // 强制生成一个新的Pawn对象，而不是从现有的池中获取
                forceGenerateNewPawn: true,
                // 不允许生成的Pawn是死亡的
                allowDead: false,
                // 允许生成的Pawn是倒下的
                allowDowned: true,
                // 不允许为该Pawn生成关系（如亲友关系等）
                canGeneratePawnRelations: false,
                // 生成的Pawn必须有能力进行暴力行为
                mustBeCapableOfViolence: true,
                // 如果需要，不强制添加免费的保暖层（可能是针对某些特定环境或生物的设定）
                forceAddFreeWarmLayerIfNeeded: false,
                // 允许生成的Pawn是同性恋的
                allowGay: true,
                // 不允许生成的Pawn是怀孕的
                allowPregnant: false,
                // 强制生成的Pawn可被招募
                forceRecruitable: true,
                // 指定生成的Pawn的性别为女性
                fixedGender: Gender.Female,
                // 不允许生成儿童
                developmentalStages: DevelopmentalStage.Adult); // 仅允许成人

            Pawn pawn = GenerateDownedPawn(request);
            if (pawn == null)
            {
                return null;
            }

            MooGirlApparelTagUtility.TryWearIdeoSuppressedKindApparel(pawn);

            // 如果生成的pawn不是世界pawn，则将其传递到世界pawn管理中。
            MooGirlGeneratedPawnUtility.TryPassToWorld(pawn);

            // 返回生成的pawn。
            return pawn;
        }

        private static Pawn GenerateDownedPawn(PawnGenerationRequest request)
        {
            Pawn fallback = null;
            for (int i = 0; i < MaxDownedGenerationAttempts; i++)
            {
                MooGirlGeneratedPawnUtility.Discard(fallback);
                fallback = null;

                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (pawn == null)
                {
                    continue;
                }

                HealthUtility.DamageUntilDowned(pawn, true);
                if (pawn.Downed && !pawn.Dead)
                {
                    return pawn;
                }

                fallback = pawn;
            }

            if (fallback != null && !fallback.Dead)
            {
                MooGirlLog.WarningOnce(
                    "RefugeePodDownedGenerationFallback",
                    "MooGirl.RefugeePodCrash.Log.DownedGenerationFallback".Translate(MaxDownedGenerationAttempts).ToString());
                return fallback;
            }

            MooGirlGeneratedPawnUtility.Discard(fallback);
            MooGirlLog.WarningOnce(
                "RefugeePodPawnGenerationFailed",
                "MooGirl.RefugeePodCrash.Log.GenerationFailed".Translate(MaxDownedGenerationAttempts).ToString());
            return null;
        }

        protected override void RunInt()
        {
            Quest quest = QuestGen.quest;
            Slate slate = QuestGen.slate;
            if (quest == null || slate == null)
            {
                MooGirlLog.WarningOnce(
                    "RefugeePodCrashMissingQuestContext",
                    "MooGirl.RefugeePodCrash.Log.MissingQuestContext".Translate().ToString());
                return;
            }

            if (!slate.TryGet<Map>("map", out Map map) || map == null)
            {
                bool canBeSpace = CanBeSpace;
                map = QuestGen_Get.GetMap(mustBeInfestable: false, null, canBeSpace);
            }
            if (map?.Parent == null)
            {
                MooGirlLog.WarningOnce(
                    "RefugeePodCrashMissingMap",
                    "MooGirl.RefugeePodCrash.Log.MissingMap".Translate().ToString());
                return;
            }

            if (!CanBeSpace)
            {
                quest.AcceptanceRequirementNotSpace(map.Parent);
            }

            Pawn pawn = GeneratePawn();
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                MooGirlLog.WarningOnce(
                    "RefugeePodCrashPawnGenerationFailed",
                    "MooGirl.RefugeePodCrash.Log.GenerationFailed".Translate(MaxDownedGenerationAttempts).ToString());
                MooGirlGeneratedPawnUtility.Discard(pawn);
                return;
            }

            AddSpawnPawnQuestParts(quest, map, pawn);
            slate.Set("pawn", pawn);
            SendLetter_NewTemp(quest, pawn, map);

            string inSignalKilled = QuestGenUtility.HardcodedSignalWithQuestID("pawn.Killed");
            string inSignalLeftBehind = QuestGenUtility.HardcodedSignalWithQuestID("pawn.LeftBehind");
            string inSignalPlayerTended = QuestGenUtility.HardcodedSignalWithQuestID("pawn.PlayerTended");
            string inSignalLeftMap = QuestGenUtility.HardcodedSignalWithQuestID("pawn.LeftMap");
            string inSignalRecruited = QuestGenUtility.HardcodedSignalWithQuestID("pawn.Recruited");

            quest.End(QuestEndOutcome.Success, 0, null, inSignalPlayerTended);
            quest.Signal(inSignalKilled, delegate
            {
                quest.AcceptedAfterTicks(AllowKilledBeforeTicks, delegate
                {
                    quest.AnyColonistWithCharityPrecept(delegate
                    {
                        quest.Message("MessageCharityEventRefused".Translate() + ": " + "MessageWandererLeftToDie".Translate(pawn), MessageTypeDefOf.NegativeEvent, getLookTargetsFromSignal: false, null, pawn);
                    });
                    QuestGen_End.End(quest, QuestEndOutcome.Fail);
                }, delegate
                {
                    QuestGen_End.End(quest, QuestEndOutcome.Fail);
                });
            });
            quest.Signal(inSignalLeftBehind, delegate
            {
                quest.AnyColonistWithCharityPrecept(delegate
                {
                    quest.Message("MessageCharityEventRefused".Translate() + ": " + "MessageWandererLeftBehind".Translate(pawn), MessageTypeDefOf.NegativeEvent, getLookTargetsFromSignal: false, null, pawn);
                });
                QuestGen_End.End(quest, QuestEndOutcome.Fail);
            });
            quest.AnyColonistWithCharityPrecept(delegate
            {
                quest.Message("MessageCharityEventFulfilled".Translate() + ": " + "MessageWandererRecruited".Translate(pawn), MessageTypeDefOf.PositiveEvent, getLookTargetsFromSignal: false, null, pawn);
            }, null, inSignalRecruited);
            quest.End(QuestEndOutcome.Success, 0, null, inSignalRecruited);
            quest.Signal(inSignalLeftMap, delegate
            {
                AddLeftMapQuestParts(quest, pawn);
            });
        }

        public override void SendLetter_NewTemp(Quest quest, Pawn pawn, Map map)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                MooGirlLog.WarningOnce(
                    "RefugeePodCrashLetterMissingPawn",
                    "Refugee pod crash tried to send a letter without a usable pawn.");
                return;
            }

            TaggedString label = "MooGirl.LetterLabelRefugeePodCrash".Translate();
            TaggedString taggedString = "MooGirl.RefugeePodCrash".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
            taggedString += "\n\n";
            if (pawn.Faction == null)
            {
                taggedString += "MooGirl.RefugeePodCrash_Factionless".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
            }
            else if (MooGirlWildSlaveUtility.IsHostileToPlayer(pawn.Faction))
            {
                taggedString += "MooGirl.RefugeePodCrash_Hostile".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
            }
            else
            {
                taggedString += "MooGirl.RefugeePodCrash_NonHostile".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
            }
            if (pawn.ageTracker != null && pawn.DevelopmentalStage.Juvenile())
            {
                string arg = (pawn.ageTracker.AgeBiologicalYears * 3600000).ToStringTicksToPeriod(true, false, true, true, false);
                taggedString += "\n\n" + "MooGirl.RefugeePodCrash_Child".Translate(pawn.Named("PAWN"), arg.Named("AGE"));
            }
            QuestNode_Root_WandererJoin_WalkIn.AppendCharityInfoToLetter("JoinerCharityInfo".Translate(pawn), ref taggedString);
            PawnRelationUtility.TryAppendRelationsWithColonistsInfo(ref taggedString, ref label, pawn);
            MooGirlGameUtility.TryReceiveLetter(label, taggedString, LetterDefOf.NeutralEvent, new TargetInfo(pawn), null, null, null, null, 0, true);
        }
    }
}
