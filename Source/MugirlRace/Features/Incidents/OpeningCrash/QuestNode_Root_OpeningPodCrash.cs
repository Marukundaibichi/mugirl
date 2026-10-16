using System.Diagnostics.CodeAnalysis;
using RimWorld.QuestGen;

namespace Mugirl.Features.Incidents;

// 开局逃生舱任务：生成两个无派系雪牛娘并放入救援流程。是否成功由 StorylineService 根据任务中
// 是否含有 QuestPart_JoinOnRescue 判断（1.6 用一个进程级静态标记回传，跨档与调试入口都会残留）。
public class QuestNode_Root_OpeningPodCrash : QuestNode_Root_RefugeePodCrash
{
    private const int MaxFactionlessGenerationAttempts = 5;
    private const float OpeningPodPawnAgeYears = 18f;
    private const int OpeningPodPawnCount = 2;

    // 原版基类的 GeneratePawn_NewTemp 会生成原版难民；本节点的 RunInt 不经过它，覆写只为防止其他调用方拿到原版 Pawn。
    public override Pawn? GeneratePawn_NewTemp(Map map) => GeneratePawn();

    public override Pawn? GeneratePawn()
    {
        PawnGenerationRequest request = EventPawnRequestUtility.CreateFactionlessAdultRequest(
            IncidentsDefOf.Mugirl_Beginning_Slave, allowDowned: true,
            fixedAge: OpeningPodPawnAgeYears, validator: IsValidOpeningPodPawn);

        Pawn? pawn = GenerateFactionlessPawn(request);
        if (pawn == null)
        {
            return null;
        }

        ForceOpeningPodPawnAge(pawn);
        RescueJoinUtility.PrepareRescueJoinPawn(pawn);

        PlayerIdeoUtility.AdoptPlayerPrimaryIdeo(pawn);

        KindApparelUtility.TryWearIdeoSuppressedKindApparel(pawn);

        // 开局救援对象需要倒地出现，并带行动不能症作为事件状态。
        // 原版 DamageUntilDowned 对高抗倒地种族存在累计伤势致死风险，改用无伤安全倒地。
        pawn.health.AddHediff(IncidentsDefOf.Mugirl_Abasia);
        PawnDowningUtility.DownRescuePawnSafely(pawn);

        return pawn;
    }

    private static bool IsValidOpeningPodPawn([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn != null
            && MugirlIdentity.IsMugirlDef(pawn)
            && pawn.kindDef == IncidentsDefOf.Mugirl_Beginning_Slave
            && pawn.Faction == null
            && EventPawnRequestUtility.HasMugirlSlaveBackstories(pawn)
            && !pawn.WorkTagIsDisabled(WorkTags.Violent);
    }

    private static void ForceOpeningPodPawnAge(Pawn pawn)
    {
        if (pawn.ageTracker == null)
        {
            return;
        }

        long fixedAgeTicks = (long)(OpeningPodPawnAgeYears * GenDate.TicksPerYear);
        pawn.ageTracker.AgeBiologicalTicks = fixedAgeTicks;
        pawn.ageTracker.AgeChronologicalTicks = fixedAgeTicks;
    }

    private static Pawn? GenerateFactionlessPawn(PawnGenerationRequest request)
    {
        for (int i = 0; i < MaxFactionlessGenerationAttempts; i++)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            if (IsValidOpeningPodPawn(pawn))
            {
                return pawn;
            }

            GeneratedPawnUtility.Discard(pawn);
        }

        MugirlLog.WarningOnce(
            "OpeningPodCrashFactionlessGenerationFailed",
            "Mugirl.OpeningPodCrash.Log.FactionlessGenerationFailed".Translate(MaxFactionlessGenerationAttempts).ToString());
        return null;
    }

    // 将多个救援对象交给同一个任务投放流程。
    protected bool TryAddSpawnPawnsInOnePod(Quest? quest, Map? map, Pawn?[]? pawns)
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
            faction: null);
        return true;
    }

    protected override void RunInt()
    {
        Quest? quest = QuestGen.quest;
        Slate? slate = QuestGen.slate;
        if (quest == null || slate == null)
        {
            MugirlLog.WarningOnce(
                "OpeningPodCrashMissingQuestContext",
                "Opening pod crash quest root ran without a valid quest context.");
            return;
        }

        if (!slate.TryGet(QuestLauncher.MapSlateKey, out Map? map) || map == null)
        {
            bool canBeSpace = CanBeSpace;
            map = QuestGen_Get.GetMap(mustBeInfestable: false, null, canBeSpace);
        }

        if (map?.Parent == null)
        {
            MugirlLog.WarningOnce(
                "OpeningPodCrashMissingMap",
                "Opening pod crash quest could not resolve a usable target map.");
            return;
        }

        if (!CanBeSpace)
        {
            quest.AcceptanceRequirementNotSpace(map.Parent);
        }

        // 开局事件固定生成两个救援对象；任意一个生成失败都会丢弃已生成 Pawn。
        Pawn[] pawns = new Pawn[OpeningPodPawnCount];
        for (int i = 0; i < pawns.Length; i++)
        {
            Pawn? pawn = GeneratePawn();
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                MugirlLog.WarningOnce(
                    "OpeningPodCrashPawnGenerationFailed",
                    "Opening pod crash could not generate a usable pawn.");
                GeneratedPawnUtility.Discard(pawn);
                DiscardGeneratedPawns(pawns, i);
                return;
            }

            pawns[i] = pawn;
        }

        if (!TryAddSpawnPawnsInOnePod(quest, map, pawns))
        {
            MugirlLog.WarningOnce(
                "OpeningPodCrashDropPodSetupFailed",
                "Opening pod crash could not set up its drop pod quest part.");
            DiscardGeneratedPawns(pawns, pawns.Length);
            return;
        }

        if (GameUtility.TryGetGameComponent<GameComponent_Storyline>(out GameComponent_Storyline? storyState))
        {
            storyState.RecordOpeningCrashPawns(pawns);
        }

        slate.Set("pawns", pawns);

        SendLetter_NewTemp(quest, pawns, map);

        string inSignalRescued = QuestGenUtility.HardcodedSignalWithQuestID("pawns.Rescued");
        string inSignalKilled = QuestGenUtility.HardcodedSignalWithQuestID("pawns.Killed");
        string inSignalLeftBehind = QuestGenUtility.HardcodedSignalWithQuestID("pawns.LeftBehind");
        string inSignalRecruited = QuestGenUtility.HardcodedSignalWithQuestID("pawns.Recruited");

        quest.AddPart(new QuestPart_JoinOnRescue
        {
            inSignalRescued = inSignalRescued,
            inSignalRecruited = inSignalRecruited,
            signalListenMode = QuestPart.SignalListenMode.OngoingOnly,
            pawns = new List<Pawn>(pawns)
        });

        quest.End(QuestEndOutcome.Fail, 0, null, inSignalKilled);

        quest.End(QuestEndOutcome.Fail, 0, null, inSignalLeftBehind);
    }

    private static bool HasUsablePawns(Pawn?[]? pawns)
    {
        if (pawns == null || pawns.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < pawns.Length; i++)
        {
            Pawn? pawn = pawns[i];
            if (pawn == null || pawn.Destroyed || pawn.Dead || !IsValidOpeningPodPawn(pawn))
            {
                return false;
            }
        }

        return true;
    }

    private static void DiscardGeneratedPawns(Pawn[] pawns, int count)
    {
        for (int i = 0; i < count; i++)
        {
            GeneratedPawnUtility.Discard(pawns[i]);
        }
    }

    // 开局救援信件需要支持多个 Pawn，并把所有对象作为可跳转目标。
    public void SendLetter_NewTemp(Quest quest, Pawn?[]? pawns, Map map)
    {
        if (pawns == null || pawns.Length == 0)
        {
            return;
        }

        List<Pawn> validPawns = new List<Pawn>();
        for (int i = 0; i < pawns.Length; i++)
        {
            Pawn? pawn = pawns[i];
            if (pawn != null && !pawn.Destroyed && !pawn.Dead)
            {
                validPawns.Add(pawn);
            }
        }

        if (validPawns.Count == 0)
        {
            return;
        }

        TaggedString label = "Mugirl.LetterLabelOpeningPodCrash".Translate();
        TaggedString text = "";

        if (validPawns.Count > 1)
        {
            text += "Mugirl.OpeningPodCrash_MultipleIntro".Translate();
            text += "\n\n";
        }

        Pawn representativePawn = validPawns[0];
        text += "Mugirl.OpeningPodCrash".Translate(representativePawn.Named("PAWN")).AdjustedFor(representativePawn, "PAWN", true);

        AppendFactionInfo(validPawns, ref text);
        AppendChildInfo(validPawns, ref text);
        QuestNode_Root_WandererJoin_WalkIn.AppendCharityInfoToLetter("JoinerCharityInfo".Translate(representativePawn), ref text);
        AppendRelationsInfo(validPawns, ref text, ref label);

        GameUtility.TryReceiveLetter(label, text, LetterDefOf.NeutralEvent, new LookTargets(validPawns));
    }

    private static void AppendFactionInfo(List<Pawn> pawns, ref TaggedString text)
    {
        if (AllFactionless(pawns))
        {
            if (pawns.Count > 1)
            {
                AppendParagraph(ref text, "Mugirl.OpeningPodCrash_FactionlessMultiple".Translate());
            }
            else
            {
                Pawn pawn = pawns[0];
                AppendParagraph(ref text, "Mugirl.OpeningPodCrash_Factionless".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true));
            }

            return;
        }

        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn pawn = pawns[i];
            if (pawn.Faction == null)
            {
                AppendParagraph(ref text, "Mugirl.OpeningPodCrash_Factionless".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true));
            }
            else if (PlayerFactionUtility.IsHostileToPlayer(pawn.Faction))
            {
                AppendParagraph(ref text, "Mugirl.OpeningPodCrash_Hostile".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true));
            }
            else
            {
                AppendParagraph(ref text, "Mugirl.OpeningPodCrash_NonHostile".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true));
            }
        }
    }

    private static bool AllFactionless(List<Pawn> pawns)
    {
        for (int i = 0; i < pawns.Count; i++)
        {
            if (pawns[i].Faction != null)
            {
                return false;
            }
        }

        return true;
    }

    private static void AppendChildInfo(List<Pawn> pawns, ref TaggedString text)
    {
        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn pawn = pawns[i];
            if (pawn.ageTracker != null && pawn.DevelopmentalStage.Juvenile())
            {
                string age = JoinerLetterUtility.ChildAgeText(pawn);
                AppendParagraph(ref text, "Mugirl.OpeningPodCrash_Child".Translate(pawn.Named("PAWN"), age.Named("AGE")));
            }
        }
    }

    private static void AppendRelationsInfo(List<Pawn> pawns, ref TaggedString text, ref TaggedString label)
    {
        for (int i = 0; i < pawns.Count; i++)
        {
            PawnRelationUtility.TryAppendRelationsWithColonistsInfo(ref text, ref label, pawns[i]);
        }
    }

    private static void AppendParagraph(ref TaggedString text, TaggedString paragraph)
    {
        text += "\n\n";
        text += paragraph;
    }
}
