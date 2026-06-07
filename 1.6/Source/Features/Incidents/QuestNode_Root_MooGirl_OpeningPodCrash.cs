using Verse;
using RimWorld.QuestGen;
using RimWorld;
using System.Collections.Generic;
using Verse.AI;

namespace MooGirl
{
    // 开局逃生舱任务：生成无派系雪牛娘并放入救援流程。
    public class QuestNode_Root_MooGirl_OpeningPodCrash : QuestNode_Root_RefugeePodCrash
    {
        private const int MaxFactionlessGenerationAttempts = 20;
        private const float OpeningPodPawnAgeYears = 18f;

        public override Pawn GeneratePawn()
        {
            // 逃亡奴隶保持无派系；敌对巨企派系只用于袭击与索赔分支。
            Faction faction = null;

            // 生成请求固定为成年女性、可战斗、可招募，并通过 validator 拒绝带派系的结果。
            PawnGenerationRequest request = new PawnGenerationRequest(
                MooGirl_DefOf.MooGirl_Beginning_Slave,
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
                validatorPostGear: IsValidOpeningPodPawn,
                fixedBiologicalAge: OpeningPodPawnAgeYears,
                fixedChronologicalAge: OpeningPodPawnAgeYears,
                fixedGender: Gender.Female,
                developmentalStages: DevelopmentalStage.Adult
            );

            Pawn pawn = GenerateFactionlessPawn(request);
            if (pawn == null)
            {
                return null;
            }

            ForceOpeningPodPawnAge(pawn);
            MooGirlRescueJoinUtility.PrepareRescueJoinPawn(pawn);

            MooGirl_IdeoUtility.AdoptPlayerPrimaryIdeo(pawn);

            MooGirlApparelTagUtility.TryWearIdeoSuppressedKindApparel(pawn);

            // 开局救援对象需要倒地出现，并带行动不能症作为事件状态。
            pawn.health.AddHediff(MooGirl_DefOf.MooGirl_Abasia);
            HealthUtility.DamageUntilDowned(pawn, true);

            return pawn;
        }

        private static bool IsValidOpeningPodPawn(Pawn pawn)
        {
            return pawn != null
                && pawn.Faction == null
                && !pawn.WorkTagIsDisabled(WorkTags.Violent);
        }

        private static void ForceOpeningPodPawnAge(Pawn pawn)
        {
            if (pawn?.ageTracker == null)
            {
                return;
            }

            long fixedAgeTicks = (long)(OpeningPodPawnAgeYears * GenDate.TicksPerYear);
            pawn.ageTracker.AgeBiologicalTicks = fixedAgeTicks;
            pawn.ageTracker.AgeChronologicalTicks = fixedAgeTicks;
        }

        private Pawn GenerateFactionlessPawn(PawnGenerationRequest request)
        {
            for (int i = 0; i < MaxFactionlessGenerationAttempts; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (IsValidOpeningPodPawn(pawn))
                {
                    return pawn;
                }

                MooGirlGeneratedPawnUtility.Discard(pawn);
            }

            MooGirlLog.WarningOnce(
                "OpeningPodCrashFactionlessGenerationFailed",
                "MooGirl.OpeningPodCrash.Log.FactionlessGenerationFailed".Translate(MaxFactionlessGenerationAttempts).ToString());
            return null;
        }

        // 将多个救援对象交给同一个任务投放流程。
        protected bool TryAddSpawnPawnsInOnePod(Quest quest, Map map, Pawn[] pawns)
        {
            if (quest == null || map?.Parent == null || !HasUsablePawns(pawns))
            {
                return false;
            }

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
            return true;
        }

        protected override void RunInt()
        {
            Quest quest = QuestGen.quest;
            Slate slate = QuestGen.slate;
            if (quest == null || slate == null)
            {
                MooGirlLog.WarningOnce(
                    "OpeningPodCrashMissingQuestContext",
                    "Opening pod crash quest root ran without a valid quest context.");
                return;
            }

            if (!slate.TryGet<Map>("map", out var map) || map == null)
            {
                bool canBeSpace = CanBeSpace;
                map = QuestGen_Get.GetMap(mustBeInfestable: false, null, canBeSpace);
            }
            if (map?.Parent == null)
            {
                MooGirlLog.WarningOnce(
                    "OpeningPodCrashMissingMap",
                    "Opening pod crash quest could not resolve a usable target map.");
                return;
            }

            if (!CanBeSpace)
            {
                quest.AcceptanceRequirementNotSpace(map.Parent);
            }

            // 开局事件固定生成两个救援对象；任意一个生成失败都会丢弃已生成 pawn。
            int pawnCount = 2;
            Pawn[] pawns = new Pawn[pawnCount];
            for (int i = 0; i < pawnCount; i++)
            {
                pawns[i] = GeneratePawn();
                if (pawns[i] == null || pawns[i].Destroyed || pawns[i].Dead)
                {
                    MooGirlLog.WarningOnce(
                        "OpeningPodCrashPawnGenerationFailed",
                        "Opening pod crash could not generate a usable pawn.");
                    DiscardGeneratedPawns(pawns);
                    return;
                }
            }

            if (!TryAddSpawnPawnsInOnePod(quest, map, pawns))
            {
                MooGirlLog.WarningOnce(
                    "OpeningPodCrashDropPodSetupFailed",
                    "Opening pod crash could not set up its drop pod quest part.");
                DiscardGeneratedPawns(pawns);
                return;
            }

            slate.Set("pawns", pawns);

            SendLetter_NewTemp(quest, pawns, map);

            string inSignalRescued = QuestGenUtility.HardcodedSignalWithQuestID("pawns.Rescued");
            string inSignalKilled = QuestGenUtility.HardcodedSignalWithQuestID("pawns.Killed");
            string inSignalLeftBehind = QuestGenUtility.HardcodedSignalWithQuestID("pawns.LeftBehind");
            string inSignalRecruited = QuestGenUtility.HardcodedSignalWithQuestID("pawns.Recruited");

            quest.AddPart(new QuestPart_MooGirlRescueJoin
            {
                inSignalRescued = inSignalRescued,
                inSignalRecruited = inSignalRecruited,
                signalListenMode = QuestPart.SignalListenMode.OngoingOnly,
                pawns = new List<Pawn>(pawns)
            });

            quest.End(QuestEndOutcome.Fail, 0, null, inSignalKilled);

            quest.End(QuestEndOutcome.Fail, 0, null, inSignalLeftBehind);
        }

        private static bool HasUsablePawns(Pawn[] pawns)
        {
            if (pawns == null || pawns.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < pawns.Length; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    return false;
                }
            }

            return true;
        }

        private void DiscardGeneratedPawns(Pawn[] pawns)
        {
            if (pawns == null)
            {
                return;
            }

            for (int i = 0; i < pawns.Length; i++)
            {
                MooGirlGeneratedPawnUtility.Discard(pawns[i]);
            }
        }

        // 开局救援信件需要支持多个 pawn，并把所有对象作为可跳转目标。
        public void SendLetter_NewTemp(Quest quest, Pawn[] pawns, Map map)
        {
            if (pawns == null || pawns.Length == 0)
            {
                return;
            }

            List<Pawn> validPawns = new List<Pawn>();
            for (int i = 0; i < pawns.Length; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null && !pawn.Destroyed && !pawn.Dead)
                {
                    validPawns.Add(pawn);
                }
            }
            if (validPawns.Count == 0)
            {
                return;
            }

            TaggedString label = "MooGirl.LetterLabelOpeningPodCrash".Translate();
            TaggedString text = "";

            if (validPawns.Count > 1)
            {
                text += "MooGirl.OpeningPodCrash_MultipleIntro".Translate();
                text += "\n\n";
            }

            foreach (Pawn pawn in validPawns)
            {
                text += "MooGirl.OpeningPodCrash".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
                text += "\n\n";

                if (pawn.Faction == null)
                    text += "MooGirl.OpeningPodCrash_Factionless".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
                else if (MooGirlWildSlaveUtility.IsHostileToPlayer(pawn.Faction))
                    text += "MooGirl.OpeningPodCrash_Hostile".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
                else
                    text += "MooGirl.OpeningPodCrash_NonHostile".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);

                if (pawn.ageTracker != null && pawn.DevelopmentalStage.Juvenile())
                {
                    string arg = (pawn.ageTracker.AgeBiologicalYears * 3600000)
                        .ToStringTicksToPeriod(true, false, true, true, false);
                    text += "\n\n" + "MooGirl.OpeningPodCrash_Child".Translate(pawn.Named("PAWN"), arg.Named("AGE"));
                }

                QuestNode_Root_WandererJoin_WalkIn.AppendCharityInfoToLetter("JoinerCharityInfo".Translate(pawn), ref text);

                PawnRelationUtility.TryAppendRelationsWithColonistsInfo(ref text, ref label, pawn);

                text += "\n\n";
            }

            LookTargets lookTargets = new LookTargets(validPawns);

            MooGirlGameUtility.TryReceiveLetter(
                label,
                text,
                LetterDefOf.NeutralEvent,
                lookTargets,
                null, null, null, null, 0, true
            );
        }
    }
}
