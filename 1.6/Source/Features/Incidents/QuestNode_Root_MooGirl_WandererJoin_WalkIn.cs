using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace MooGirl
{
    // 雪牛娘流浪者加入事件：复用原版加入流程，只替换生成对象和信件内容。
    public class QuestNode_Root_MooGirl_WandererJoin_WalkIn : QuestNode_Root_WandererJoin_WalkIn
    {
        private string signalAccept;
        private string signalReject;

        public override Pawn GeneratePawn()
        {
            // 逃亡奴隶保持无派系；敌对巨企派系只用于袭击，避免流浪者加入事件附带敌对关系。
            Faction faction = null;

            // 生成请求固定为成年女性、可招募且不生成亲属关系，避免事件引入额外世界状态。
            PawnGenerationRequest request = new PawnGenerationRequest(
                MooGirl_DefOf.MooGirl_EscapeWanderSlave,
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
                fixedGender: Gender.Female,
                developmentalStages: DevelopmentalStage.Adult
            );

            Pawn pawn = PawnGenerator.GeneratePawn(request);

            MooGirlApparelTagUtility.TryWearIdeoSuppressedKindApparel(pawn);

            return pawn;
        }

        protected override void AddSpawnPawnQuestParts(Quest quest, Map map, Pawn pawn)
        {
            base.AddSpawnPawnQuestParts(quest, map, pawn);
            this.signalAccept = QuestGenUtility.HardcodedSignalWithQuestID("Accept");
            this.signalReject = QuestGenUtility.HardcodedSignalWithQuestID("Reject");
        }

        public override void SendLetter_NewTemp(Quest quest, Pawn pawn, Map map)
        {
            TaggedString taggedString = "MooGirl.LetterLabelWandererJoins".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
            TaggedString taggedString2 = "MooGirl.LetterWandererJoins".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);

            QuestNode_Root_WandererJoin_WalkIn.AppendCharityInfoToLetter("JoinerCharityInfo".Translate(pawn), ref taggedString);

            PawnRelationUtility.TryAppendRelationsWithColonistsInfo(ref taggedString2, ref taggedString, pawn);

            if (pawn.DevelopmentalStage.Juvenile())
            {
                string text = (pawn.ageTracker.AgeBiologicalYears * 3600000).ToStringTicksToPeriod(true, false, true, true, false);
                taggedString2 += "\n\n" + "MooGirl.RefugeePodCrash_Child".Translate(pawn.Named("PAWN"), text.Named("AGE"));
            }

            QuestNode_Root_WandererJoin_WalkIn.ApplyBestSkillInfoToLetter(ref taggedString2, pawn);

            // 正常情况下 MakeLetter 会返回 ChoiceLetter_AcceptJoiner；若其他 mod 改动信件类型，则退回普通信件。
            Letter letter = LetterMaker.MakeLetter(taggedString, taggedString2, LetterDefOf.AcceptJoiner, null, null);
            ChoiceLetter_AcceptJoiner choiceLetter_AcceptJoiner = letter as ChoiceLetter_AcceptJoiner;
            if (choiceLetter_AcceptJoiner == null)
            {
                MooGirlLog.WarningOnce(
                    "WandererJoinAcceptLetterType",
                    "Wanderer join letter was not ChoiceLetter_AcceptJoiner; sending fallback letter without join choice.");
                if (letter != null)
                {
                    MooGirlGameUtility.TryReceiveLetter(letter, null, 0, true);
                }

                return;
            }

            choiceLetter_AcceptJoiner.signalAccept = this.signalAccept;
            choiceLetter_AcceptJoiner.signalReject = this.signalReject;
            choiceLetter_AcceptJoiner.quest = quest;
            choiceLetter_AcceptJoiner.overrideMap = map;
            choiceLetter_AcceptJoiner.StartTimeout(60000);

            MooGirlGameUtility.TryReceiveLetter(choiceLetter_AcceptJoiner, null, 0, true);
        }
    }
}
