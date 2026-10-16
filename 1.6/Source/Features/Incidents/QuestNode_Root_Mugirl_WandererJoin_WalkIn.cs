using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace Mugirl
{
    // 雪牛娘流浪者加入事件：复用原版加入流程，只替换生成对象和信件内容。
    public class QuestNode_Root_Mugirl_WandererJoin_WalkIn : QuestNode_Root_WandererJoin_WalkIn
    {
        private string signalAccept;
        private string signalReject;

        // 1.6 的任务根调用这个入口；只覆写旧 GeneratePawn 会落回原版人类生成器。
        public override Pawn GeneratePawn_NewTemp(Map map) => GeneratePawn();

        public override Pawn GeneratePawn()
        {
            PawnGenerationRequest request = MugirlGeneratedPawnUtility.CreateFactionlessAdultRequest(
                Mugirl_DefOf.Mugirl_EscapeWanderSlave);

            Pawn pawn = PawnGenerator.GeneratePawn(request);

            MugirlApparelTagUtility.TryWearIdeoSuppressedKindApparel(pawn);
            MugirlGeneratedPawnUtility.TryPassToWorld(pawn, keepFactionless: true);

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
            TaggedString taggedString = "Mugirl.LetterLabelWandererJoins".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
            TaggedString taggedString2 = "Mugirl.LetterWandererJoins".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);

            QuestNode_Root_WandererJoin_WalkIn.AppendCharityInfoToLetter("JoinerCharityInfo".Translate(pawn), ref taggedString2);

            PawnRelationUtility.TryAppendRelationsWithColonistsInfo(ref taggedString2, ref taggedString, pawn);

            if (pawn.DevelopmentalStage.Juvenile())
            {
                string text = (pawn.ageTracker.AgeBiologicalYears * 3600000).ToStringTicksToPeriod(true, false, true, true, false);
                taggedString2 += "\n\n" + "Mugirl.RefugeePodCrash_Child".Translate(pawn.Named("PAWN"), text.Named("AGE"));
            }

            QuestNode_Root_WandererJoin_WalkIn.ApplyBestSkillInfoToLetter(ref taggedString2, pawn);

            // 正常情况下 MakeLetter 会返回 ChoiceLetter_AcceptJoiner；若其他 mod 改动信件类型，则退回普通信件。
            Letter letter = LetterMaker.MakeLetter(taggedString, taggedString2, LetterDefOf.AcceptJoiner, null, null);
            ChoiceLetter_AcceptJoiner choiceLetter_AcceptJoiner = letter as ChoiceLetter_AcceptJoiner;
            if (choiceLetter_AcceptJoiner == null)
            {
                MugirlLog.WarningOnce(
                    "WandererJoinAcceptLetterType",
                    "Wanderer join letter was not ChoiceLetter_AcceptJoiner; sending fallback letter without join choice.");
                if (letter != null)
                {
                    MugirlGameUtility.TryReceiveLetter(letter, null, 0, true);
                }

                return;
            }

            choiceLetter_AcceptJoiner.signalAccept = this.signalAccept;
            choiceLetter_AcceptJoiner.signalReject = this.signalReject;
            choiceLetter_AcceptJoiner.quest = quest;
            choiceLetter_AcceptJoiner.overrideMap = map;
            choiceLetter_AcceptJoiner.StartTimeout(60000);

            MugirlGameUtility.TryReceiveLetter(choiceLetter_AcceptJoiner, null, 0, true);
        }
    }
}
