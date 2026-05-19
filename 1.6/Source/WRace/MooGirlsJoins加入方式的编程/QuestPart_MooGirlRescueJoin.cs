using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MooGirl
{
    public class QuestPart_MooGirlRescueJoin : QuestPart
    {
        public string inSignalRescued;
        public string inSignalRecruited;
        public List<Pawn> pawns = new List<Pawn>();

        public override bool IncreasesPopulation => true;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);

            if (signal.tag == inSignalRescued)
            {
                if (signal.args.TryGetArg("SUBJECT", out Pawn rescuedPawn) && pawns.Contains(rescuedPawn))
                {
                    TryJoinRescuedPawn(rescuedPawn);
                }
                else
                {
                    for (int i = 0; i < pawns.Count; i++)
                    {
                        TryJoinRescuedPawn(pawns[i]);
                    }
                }

                EndQuestIfAllJoined();
            }
            else if (signal.tag == inSignalRecruited)
            {
                EndQuestIfAllJoined();
            }
        }

        private void TryJoinRescuedPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                return;
            }

            MooGirlRescueJoinUtility.PrepareRescueJoinPawn(pawn);
            if (pawn.Faction == Faction.OfPlayer)
            {
                return;
            }

            if (MooGirlRescueJoinUtility.WasRescuedByPlayer(pawn))
            {
                MooGirlRescueJoinUtility.TryJoinPlayer(pawn);
            }
        }

        private void EndQuestIfAllJoined()
        {
            if (quest == null || quest.State != QuestState.Ongoing)
            {
                return;
            }

            bool anyPawn = false;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Dead || pawn.Destroyed)
                {
                    return;
                }

                anyPawn = true;
                if (pawn.Faction != Faction.OfPlayer)
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
            return pawns.Contains(p);
        }

        public override void ReplacePawnReferences(Pawn replace, Pawn with)
        {
            pawns.Replace(replace, with);
        }

        public override void Notify_PawnDiscarded(Pawn pawn)
        {
            pawns.Remove(pawn);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignalRescued, "inSignalRescued");
            Scribe_Values.Look(ref inSignalRecruited, "inSignalRecruited");
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pawns == null)
                {
                    pawns = new List<Pawn>();
                }
                pawns.RemoveAll((Pawn pawn) => pawn == null);
            }
        }
    }
}
