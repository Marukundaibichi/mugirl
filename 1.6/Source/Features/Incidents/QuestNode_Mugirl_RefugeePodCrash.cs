using Verse;
using RimWorld.QuestGen;
using RimWorld;

namespace Mugirl
{
    public class QuestNode_Root_Mugirl_RefugeePodCrash : QuestNode_Root_RefugeePodCrash
    {
        private const int MaxDownedGenerationAttempts = 10;

        public override Pawn GeneratePawn()
        {
            // 逃亡奴隶保持无派系；敌对巨企派系只用于袭击，避免救援任务变成战斗事件。
            Faction faction = null;

            // 救援逃生舱生成成年女性、可招募且无亲属关系的雪牛娘。
            PawnGenerationRequest request = new PawnGenerationRequest(
                Mugirl_DefOf.Mugirl_EscapeSpaceSlave,
                faction,
                PawnGenerationContext.NonPlayer,
                -1,
                forceGenerateNewPawn: true,
                allowDead: false,
                allowDowned: true,
                canGeneratePawnRelations: false,
                mustBeCapableOfViolence: true,
                forceAddFreeWarmLayerIfNeeded: false,
                allowGay: true,
                allowPregnant: false,
                forceRecruitable: true,
                validatorPostGear: IsValidRefugeePodPawn,
                fixedGender: Gender.Female,
                developmentalStages: DevelopmentalStage.Adult);

            Pawn pawn = GenerateDownedPawn(request);
            if (pawn == null)
            {
                return null;
            }

            MugirlApparelTagUtility.TryWearIdeoSuppressedKindApparel(pawn);

            // 任务系统需要世界 pawn 参与后续信号和清理。
            MugirlGeneratedPawnUtility.TryPassToWorld(pawn);

            return pawn;
        }

        private static Pawn GenerateDownedPawn(PawnGenerationRequest request)
        {
            Pawn fallback = null;
            for (int i = 0; i < MaxDownedGenerationAttempts; i++)
            {
                MugirlGeneratedPawnUtility.Discard(fallback);
                fallback = null;

                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (pawn == null)
                {
                    continue;
                }

                if (!IsValidRefugeePodPawn(pawn))
                {
                    MugirlGeneratedPawnUtility.Discard(pawn);
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
                MugirlLog.WarningOnce(
                    "RefugeePodDownedGenerationFallback",
                    "Mugirl.RefugeePodCrash.Log.DownedGenerationFallback".Translate(MaxDownedGenerationAttempts).ToString());
                return fallback;
            }

            MugirlGeneratedPawnUtility.Discard(fallback);
            MugirlLog.WarningOnce(
                "RefugeePodPawnGenerationFailed",
                "Mugirl.RefugeePodCrash.Log.GenerationFailed".Translate(MaxDownedGenerationAttempts).ToString());
            return null;
        }

        private static bool IsValidRefugeePodPawn(Pawn pawn)
        {
            return pawn != null
                && MugirlIdentity.IsMugirlDef(pawn)
                && pawn.kindDef == Mugirl_DefOf.Mugirl_EscapeSpaceSlave
                && pawn.Faction == null
                && !pawn.WorkTagIsDisabled(WorkTags.Violent);
        }

        protected override void RunInt()
        {
            Quest quest = QuestGen.quest;
            Slate slate = QuestGen.slate;
            if (quest == null || slate == null)
            {
                MugirlLog.WarningOnce(
                    "RefugeePodCrashMissingQuestContext",
                    "Mugirl.RefugeePodCrash.Log.MissingQuestContext".Translate().ToString());
                return;
            }

            if (!slate.TryGet<Map>("map", out Map map) || map == null)
            {
                bool canBeSpace = CanBeSpace;
                map = QuestGen_Get.GetMap(mustBeInfestable: false, null, canBeSpace);
            }
            if (map?.Parent == null)
            {
                MugirlLog.WarningOnce(
                    "RefugeePodCrashMissingMap",
                    "Mugirl.RefugeePodCrash.Log.MissingMap".Translate().ToString());
                return;
            }

            if (!CanBeSpace)
            {
                quest.AcceptanceRequirementNotSpace(map.Parent);
            }

            Pawn pawn = GeneratePawn();
            if (pawn == null || pawn.Destroyed || pawn.Dead || !IsValidRefugeePodPawn(pawn))
            {
                MugirlLog.WarningOnce(
                    "RefugeePodCrashPawnGenerationFailed",
                    "Mugirl.RefugeePodCrash.Log.GenerationFailed".Translate(MaxDownedGenerationAttempts).ToString());
                MugirlGeneratedPawnUtility.Discard(pawn);
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
                MugirlLog.WarningOnce(
                    "RefugeePodCrashLetterMissingPawn",
                    "Refugee pod crash tried to send a letter without a usable pawn.");
                return;
            }

            TaggedString label = "Mugirl.LetterLabelRefugeePodCrash".Translate();
            TaggedString taggedString = "Mugirl.RefugeePodCrash".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
            taggedString += "\n\n";
            if (pawn.Faction == null)
            {
                taggedString += "Mugirl.RefugeePodCrash_Factionless".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
            }
            else if (MugirlWildSlaveUtility.IsHostileToPlayer(pawn.Faction))
            {
                taggedString += "Mugirl.RefugeePodCrash_Hostile".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
            }
            else
            {
                taggedString += "Mugirl.RefugeePodCrash_NonHostile".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
            }
            if (pawn.ageTracker != null && pawn.DevelopmentalStage.Juvenile())
            {
                string arg = (pawn.ageTracker.AgeBiologicalYears * 3600000).ToStringTicksToPeriod(true, false, true, true, false);
                taggedString += "\n\n" + "Mugirl.RefugeePodCrash_Child".Translate(pawn.Named("PAWN"), arg.Named("AGE"));
            }
            QuestNode_Root_WandererJoin_WalkIn.AppendCharityInfoToLetter("JoinerCharityInfo".Translate(pawn), ref taggedString);
            PawnRelationUtility.TryAppendRelationsWithColonistsInfo(ref taggedString, ref label, pawn);
            MugirlGameUtility.TryReceiveLetter(label, taggedString, LetterDefOf.NeutralEvent, new TargetInfo(pawn), null, null, null, null, 0, true);
        }
    }
}
