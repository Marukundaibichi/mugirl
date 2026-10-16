namespace Mugirl.Features.Incidents;

// 开局坠舱对象被玩家救援（送上玩家的床或成为玩家的客人）后自动加入；全部加入后任务成功。按类名存档。
public class QuestPart_JoinOnRescue : QuestPart
{
    public string? inSignalRescued;
    public string? inSignalRecruited;
    public List<Pawn> pawns = new List<Pawn>();

    public override bool IncreasesPopulation => true;

    public override void Notify_QuestSignalReceived(Signal signal)
    {
        base.Notify_QuestSignalReceived(signal);

        if (!string.IsNullOrEmpty(inSignalRescued) && signal.tag == inSignalRescued)
        {
            if (signal.args.TryGetArg("SUBJECT", out Pawn rescuedPawn) && pawns?.Contains(rescuedPawn) == true)
            {
                TryJoinRescuedPawn(rescuedPawn);
            }
            else
            {
                if (pawns == null)
                {
                    return;
                }

                for (int i = 0; i < pawns.Count; i++)
                {
                    TryJoinRescuedPawn(pawns[i]);
                }
            }

            EndQuestIfAllJoined();
        }
        else if (!string.IsNullOrEmpty(inSignalRecruited) && signal.tag == inSignalRecruited)
        {
            EndQuestIfAllJoined();
        }
    }

    private static void TryJoinRescuedPawn(Pawn? pawn)
    {
        if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.mindState == null)
        {
            return;
        }

        if (PlayerFactionUtility.IsPlayerFaction(pawn.Faction))
        {
            return;
        }

        RescueJoinUtility.PrepareRescueJoinPawn(pawn);
        if (RescueJoinUtility.WasRescuedByPlayer(pawn))
        {
            RescueJoinUtility.TryJoinPlayer(pawn);
        }
    }

    private void EndQuestIfAllJoined()
    {
        if (quest == null || quest.State != QuestState.Ongoing || pawns == null)
        {
            return;
        }

        bool anyPawn = false;
        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn? pawn = pawns[i];
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                return;
            }

            anyPawn = true;
            if (!PlayerFactionUtility.IsPlayerFaction(pawn.Faction))
            {
                return;
            }
        }

        if (anyPawn)
        {
            quest.End(QuestEndOutcome.Success, sendLetter: false, playSound: false);
        }
    }

    public override bool QuestPartReserves(Pawn p)
    {
        return pawns?.Contains(p) == true;
    }

    public override void ReplacePawnReferences(Pawn replace, Pawn with)
    {
        // 显式调用 Verse 的 GenCollection.Replace：.NET 10 也有同名扩展方法，接收者改为数组时会静默改绑。
        if (pawns != null)
        {
            GenCollection.Replace(pawns, replace, with);
        }
    }

    public override void Notify_PawnDiscarded(Pawn pawn)
    {
        pawns?.Remove(pawn);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref inSignalRescued, "inSignalRescued");
        Scribe_Values.Look(ref inSignalRecruited, "inSignalRecruited");
        Scribe_Collections.Look(ref pawns, "pawns", LookMode.Reference);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            pawns ??= new List<Pawn>();
            pawns.RemoveAll(pawn => pawn == null);
        }
    }
}
