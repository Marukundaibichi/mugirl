using System.Diagnostics.CodeAnalysis;
using RimWorld.QuestGen;

namespace Mugirl.Features.Incidents;

// 单个逃亡太空奴隶坠舱：照搬原版 QuestNode_Root_WandererJoin.RunInt 的信号结构（1.7 已核对），只替换生成对象和信件。
// 不改用基类 RunInt：缺少地图或 Pawn 生成失败时这里放弃本任务，基类会抛出空引用或用空 Pawn 建立空投与信号。
public class QuestNode_Root_MugirlRefugeePodCrash : QuestNode_Root_RefugeePodCrash
{
    private const int MaxDownedGenerationAttempts = 10;

    // 原版基类的 GeneratePawn_NewTemp 会生成原版难民；本节点的 RunInt 不经过它，覆写只为防止其他调用方拿到原版 Pawn。
    public override Pawn? GeneratePawn_NewTemp(Map map) => GeneratePawn();

    public override Pawn? GeneratePawn()
    {
        PawnGenerationRequest request = EventPawnRequestUtility.CreateFactionlessAdultRequest(
            IncidentsDefOf.Mugirl_EscapeSpaceSlave, allowDowned: true, validator: IsValidRefugeePodPawn);

        Pawn? pawn = GenerateDownedPawn(request);
        if (pawn == null)
        {
            return null;
        }

        KindApparelUtility.TryWearIdeoSuppressedKindApparel(pawn);

        // 任务系统需要世界 Pawn 参与后续信号和清理。
        GeneratedPawnUtility.TryPassToWorld(pawn, keepFactionless: true);

        return pawn;
    }

    private static Pawn? GenerateDownedPawn(PawnGenerationRequest request)
    {
        Pawn? fallback = null;
        for (int i = 0; i < MaxDownedGenerationAttempts; i++)
        {
            GeneratedPawnUtility.Discard(fallback);
            fallback = null;

            Pawn? pawn = PawnGenerator.GeneratePawn(request);
            if (pawn == null)
            {
                continue;
            }

            if (!IsValidRefugeePodPawn(pawn))
            {
                GeneratedPawnUtility.Discard(pawn);
                continue;
            }

            // 原版 DamageUntilDowned 对高抗倒地种族存在累计伤势致死风险，改用无伤安全倒地。
            // forceDowned 只抑制倒地即死，不代替失能条件；与开局共用可自然恢复的疲惫状态。
            pawn.health.AddHediff(IncidentsDefOf.Mugirl_Abasia);
            PawnDowningUtility.DownRescuePawnSafely(pawn);
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

        GeneratedPawnUtility.Discard(fallback);
        MugirlLog.WarningOnce(
            "RefugeePodPawnGenerationFailed",
            "Mugirl.RefugeePodCrash.Log.GenerationFailed".Translate(MaxDownedGenerationAttempts).ToString());
        return null;
    }

    private static bool IsValidRefugeePodPawn([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn != null
            && MugirlIdentity.IsMugirlDef(pawn)
            && pawn.kindDef == IncidentsDefOf.Mugirl_EscapeSpaceSlave
            && pawn.Faction == null
            && EventPawnRequestUtility.HasMugirlSlaveBackstories(pawn)
            && !pawn.WorkTagIsDisabled(WorkTags.Violent);
    }

    protected override void RunInt()
    {
        Quest? quest = QuestGen.quest;
        Slate? slate = QuestGen.slate;
        if (quest == null || slate == null)
        {
            MugirlLog.WarningOnce(
                "RefugeePodCrashMissingQuestContext",
                "Mugirl.RefugeePodCrash.Log.MissingQuestContext".Translate().ToString());
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
                "RefugeePodCrashMissingMap",
                "Mugirl.RefugeePodCrash.Log.MissingMap".Translate().ToString());
            return;
        }

        if (!CanBeSpace)
        {
            quest.AcceptanceRequirementNotSpace(map.Parent);
        }

        // GeneratePawn 只返回 null，或已通过 IsValidRefugeePodPawn 且存活、未销毁的 Pawn；校验之后的倒地、补穿服装和转入世界不改变这些条件。
        Pawn? pawn = GeneratePawn();
        if (pawn == null)
        {
            MugirlLog.WarningOnce(
                "RefugeePodCrashPawnGenerationFailed",
                "Mugirl.RefugeePodCrash.Log.GenerationFailed".Translate(MaxDownedGenerationAttempts).ToString());
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

    public override void SendLetter_NewTemp(Quest quest, Pawn? pawn, Map map)
    {
        if (pawn == null || pawn.Destroyed || pawn.Dead)
        {
            MugirlLog.WarningOnce(
                "RefugeePodCrashLetterMissingPawn",
                "Refugee pod crash tried to send a letter without a usable pawn.");
            return;
        }

        TaggedString label = "Mugirl.LetterLabelRefugeePodCrash".Translate();
        TaggedString text = "Mugirl.RefugeePodCrash".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
        text += "\n\n";
        if (pawn.Faction == null)
        {
            text += "Mugirl.RefugeePodCrash_Factionless".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
        }
        else if (PlayerFactionUtility.IsHostileToPlayer(pawn.Faction))
        {
            text += "Mugirl.RefugeePodCrash_Hostile".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
        }
        else
        {
            text += "Mugirl.RefugeePodCrash_NonHostile".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
        }

        if (pawn.ageTracker != null && pawn.DevelopmentalStage.Juvenile())
        {
            string age = JoinerLetterUtility.ChildAgeText(pawn);
            text += "\n\n" + "Mugirl.RefugeePodCrash_Child".Translate(pawn.Named("PAWN"), age.Named("AGE"));
        }

        QuestNode_Root_WandererJoin_WalkIn.AppendCharityInfoToLetter("JoinerCharityInfo".Translate(pawn), ref text);
        PawnRelationUtility.TryAppendRelationsWithColonistsInfo(ref text, ref label, pawn);
        GameUtility.TryReceiveLetter(label, text, LetterDefOf.NeutralEvent, new TargetInfo(pawn));
    }
}
