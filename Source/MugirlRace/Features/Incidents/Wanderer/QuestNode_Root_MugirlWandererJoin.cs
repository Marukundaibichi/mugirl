using RimWorld.QuestGen;

namespace Mugirl.Features.Incidents;

// 雪牛娘流浪者加入事件：复用原版加入流程，只替换生成对象和信件内容。
public class QuestNode_Root_MugirlWandererJoin : QuestNode_Root_WandererJoin_WalkIn
{
    private const int AcceptLetterTimeoutTicks = 60000;

    // 与原版私有字段 signalAccept/signalReject 取同样的信号名（HardcodedSignalWithQuestID），供自定义信件使用。
    private string? signalAccept;
    private string? signalReject;

    // 1.6 起的任务根调用这个入口；只覆写旧 GeneratePawn 会落回原版人类生成器。
    public override Pawn GeneratePawn_NewTemp(Map map) => GeneratePawn();

    public override Pawn GeneratePawn()
    {
        PawnGenerationRequest request = EventPawnRequestUtility.CreateFactionlessAdultRequest(
            IncidentsDefOf.Mugirl_EscapeWanderSlave);

        Pawn pawn = PawnGenerator.GeneratePawn(request);

        KindApparelUtility.TryWearIdeoSuppressedKindApparel(pawn);
        GeneratedPawnUtility.TryPassToWorld(pawn, keepFactionless: true);

        return pawn;
    }

    protected override void AddSpawnPawnQuestParts(Quest quest, Map map, Pawn pawn)
    {
        base.AddSpawnPawnQuestParts(quest, map, pawn);
        signalAccept = QuestGenUtility.HardcodedSignalWithQuestID("Accept");
        signalReject = QuestGenUtility.HardcodedSignalWithQuestID("Reject");
    }

    public override void SendLetter_NewTemp(Quest quest, Pawn pawn, Map map)
    {
        TaggedString label = "Mugirl.LetterLabelWandererJoins".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
        TaggedString text = "Mugirl.LetterWandererJoins".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);

        QuestNode_Root_WandererJoin_WalkIn.AppendCharityInfoToLetter("JoinerCharityInfo".Translate(pawn), ref text);

        PawnRelationUtility.TryAppendRelationsWithColonistsInfo(ref text, ref label, pawn);

        if (pawn.DevelopmentalStage.Juvenile())
        {
            string age = JoinerLetterUtility.ChildAgeText(pawn);
            text += "\n\n" + "Mugirl.RefugeePodCrash_Child".Translate(pawn.Named("PAWN"), age.Named("AGE"));
        }

        QuestNode_Root_WandererJoin_WalkIn.ApplyBestSkillInfoToLetter(ref text, pawn);

        // 正常情况下 MakeLetter 会返回 ChoiceLetter_AcceptJoiner；若其他 mod 改动信件类型，则退回普通信件。
        Letter? letter = LetterMaker.MakeLetter(label, text, LetterDefOf.AcceptJoiner, null, null);
        if (letter is not ChoiceLetter_AcceptJoiner acceptLetter)
        {
            MugirlLog.WarningOnce(
                "WandererJoinAcceptLetterType",
                "Wanderer join letter was not ChoiceLetter_AcceptJoiner; sending fallback letter without join choice.");
            GameUtility.TryReceiveLetter(letter);
            return;
        }

        acceptLetter.signalAccept = signalAccept;
        acceptLetter.signalReject = signalReject;
        acceptLetter.quest = quest;
        acceptLetter.overrideMap = map;
        acceptLetter.StartTimeout(AcceptLetterTimeoutTicks);

        GameUtility.TryReceiveLetter(acceptLetter);
    }
}
