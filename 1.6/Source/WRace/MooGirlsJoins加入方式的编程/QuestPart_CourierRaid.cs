using System.Collections.Generic;
using RimWorld;
using RimWorld.QuestGen;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MooGirl
{
    public class QuestPart_SpawnCourier : QuestPart
    {
        public Map map;
        public Faction faction;
        public IntVec3 spawnCell;
        public Pawn courier;
        public string inSignal;

        private const int ConversationWaitTicks = 2 * 60000;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag == inSignal)
            {
                SpawnCourier();
            }
        }

        private void SpawnCourier()
        {
            if (courier != null && !courier.Destroyed)
            {
                return;
            }
            if (map == null)
            {
                Log.Warning("[MooGirl] CourierRaid spawn aborted: map is null.");
                return;
            }
            if (faction == null)
            {
                Log.Warning("[MooGirl] CourierRaid spawn aborted: faction is null.");
                return;
            }
            if (!spawnCell.IsValid && !RCellFinder.TryFindRandomPawnEntryCell(out spawnCell, map, 0f))
            {
                Log.Warning("[MooGirl] CourierRaid spawn aborted: no valid spawn cell.");
                return;
            }

            Faction courierFaction = faction != null && !faction.HostileTo(Faction.OfPlayer) ? faction : null;
            PawnGenerationRequest request = new PawnGenerationRequest(
                MooGirlContentDefOf.AI_GC_Courier,
                courierFaction,
                PawnGenerationContext.NonPlayer,
                map.Tile,
                forceGenerateNewPawn: true,
                canGeneratePawnRelations: false,
                mustBeCapableOfViolence: true,
                allowFood: false);
            courier = PawnGenerator.GeneratePawn(request);

            AddToInventory(courier, MooGirlContentDefOf.MooGirl_CourierDiary, 1);
            AddToInventory(courier, MooGirlContentDefOf.MooGirl_SlaveApperalKey_Medieval, 6);
            AddToInventory(courier, MooGirlContentDefOf.MooGirl_SlaveApperalKey_Industrial, 4);

            GenSpawn.Spawn(courier, spawnCell, map);
            LordMaker.MakeNewLord(
                courier.Faction,
                new LordJob_WaitForDurationThenExit(spawnCell, ConversationWaitTicks),
                map,
                new List<Pawn> { courier });

            Find.LetterStack.ReceiveLetter(
                "MooGirl.CourierContactLetterLabel".Translate(),
                "MooGirl.CourierContactLetterText".Translate(),
                LetterDefOf.NeutralEvent,
                courier);
            Find.TickManager.slower.SignalForceNormalSpeedShort();
        }

        private static void AddToInventory(Pawn pawn, ThingDef thingDef, int count)
        {
            if (pawn == null || thingDef == null || count <= 0) return;
            Thing thing = ThingMaker.MakeThing(thingDef);
            thing.stackCount = count;
            if (!pawn.inventory.innerContainer.TryAdd(thing))
            {
                thing.Destroy();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref map, "map");
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref spawnCell, "spawnCell");
            Scribe_References.Look(ref courier, "courier");
            Scribe_Values.Look(ref inSignal, "inSignal");
        }

        public override void AssignDebugData()
        {
            base.AssignDebugData();
            inSignal = "SpawnCourier";
        }
    }

    public static class CourierConversationUtility
    {
        public static bool CanTalkToCourier(Pawn courier)
        {
            return courier != null
                && courier.Spawned
                && !courier.Dead
                && !courier.Downed
                && !courier.InAggroMentalState
                && courier.kindDef == MooGirlContentDefOf.AI_GC_Courier
                && HasCourierPayload(courier);
        }

        public static void ShowDialog(Pawn courier, Pawn negotiator)
        {
            if (!CanTalkToCourier(courier))
            {
                Messages.Message("MooGirl.CourierAlreadyResolved".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            Dialog_MessageBox dialog = new Dialog_MessageBox(
                "MooGirl.CourierDialogText".Translate(courier.Named("COURIER"), negotiator.Named("NEGOTIATOR")),
                "MooGirl.CourierDialogFight".Translate(),
                () => StartFight(courier),
                "MooGirl.CourierDialogLeaveItems".Translate(),
                () => DropItemsAndLeave(courier),
                "MooGirl.CourierDialogTitle".Translate(),
                buttonADestructive: true);
            Find.WindowStack.Add(dialog);
        }

        private static bool HasCourierPayload(Pawn courier)
        {
            if (courier?.inventory?.innerContainer == null)
            {
                return false;
            }

            ThingOwner<Thing> inventory = courier.inventory.innerContainer;
            for (int i = 0; i < inventory.Count; i++)
            {
                Thing thing = inventory[i];
                if (thing.def == MooGirlContentDefOf.MooGirl_CourierDiary
                    || thing.def == MooGirlContentDefOf.MooGirl_SlaveApperalKey_Medieval
                    || thing.def == MooGirlContentDefOf.MooGirl_SlaveApperalKey_Industrial)
                {
                    return true;
                }
            }

            return false;
        }

        private static void DropItemsAndLeave(Pawn courier)
        {
            if (courier == null || !courier.Spawned)
            {
                return;
            }

            courier.inventory.DropAllNearPawn(courier.Position, forbid: false, unforbid: true);
            StartLeaving(courier);
            EndRelatedQuest(courier, QuestEndOutcome.Success);
            Messages.Message("MooGirl.CourierItemsDroppedMessage".Translate(courier.Named("COURIER")), courier, MessageTypeDefOf.PositiveEvent);
        }

        private static void StartFight(Pawn courier)
        {
            if (courier == null || !courier.Spawned)
            {
                return;
            }

            RemoveFromCurrentLord(courier);
            if (courier.Faction != null)
            {
                courier.SetFaction(null);
            }

            courier.mindState.mentalStateHandler.TryStartMentalState(
                MentalStateDefOf.Berserk,
                "MooGirl.CourierFightReason".Translate(),
                forced: true,
                forceWake: true,
                transitionSilently: true);
            EndRelatedQuest(courier, QuestEndOutcome.Unknown);
            Messages.Message("MooGirl.CourierFightMessage".Translate(courier.Named("COURIER")), courier, MessageTypeDefOf.ThreatSmall);
            Find.TickManager.slower.SignalForceNormalSpeedShort();
        }

        private static void StartLeaving(Pawn courier)
        {
            RemoveFromCurrentLord(courier);
            LordMaker.MakeNewLord(
                courier.Faction,
                new LordJob_ExitMapBest(LocomotionUrgency.Jog, canDig: false, canDefendSelf: true),
                courier.Map,
                Gen.YieldSingle(courier));
        }

        private static void RemoveFromCurrentLord(Pawn courier)
        {
            Lord lord = courier.GetLord();
            lord?.RemovePawn(courier);
        }

        private static void EndRelatedQuest(Pawn courier, QuestEndOutcome outcome)
        {
            List<Quest> quests = Find.QuestManager.QuestsListForReading;
            for (int i = 0; i < quests.Count; i++)
            {
                Quest quest = quests[i];
                if (quest == null || quest.root != MooGirlContentDefOf.MooGirl_CourierRaid || quest.State != QuestState.Ongoing)
                {
                    continue;
                }

                List<QuestPart> parts = quest.PartsListForReading;
                for (int j = 0; j < parts.Count; j++)
                {
                    if (parts[j] is QuestPart_SpawnCourier spawnPart && spawnPart.courier == courier)
                    {
                        quest.End(outcome, sendLetter: false, playSound: false);
                        return;
                    }
                }
            }
        }
    }

    public class FloatMenuProvider_TalkCourier : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => true;

        public override bool TargetPawnValid(Pawn target, FloatMenuContext context)
        {
            return base.TargetPawnValid(target, context) && CourierConversationUtility.CanTalkToCourier(target);
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn actor = context.FirstSelectedPawn;
            if (actor == null || actor.Faction != Faction.OfPlayer || !actor.RaceProps.Humanlike)
            {
                yield break;
            }

            FloatMenuOption option = FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption("MooGirl.CourierTalkOption".Translate(clickedPawn.Named("COURIER")), () =>
                {
                    Job job = JobMaker.MakeJob(MooGirlContentDefOf.MooGirl_TalkCourier, clickedPawn);
                    actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }, MenuOptionPriority.High),
                actor,
                clickedPawn);

            if (!actor.CanReserveAndReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
            {
                option.Disabled = true;
                option.Label = "MooGirl.CourierTalkOptionDisabled".Translate(option.Label, "MooGirl.CourierTalkCannotReach".Translate());
            }

            yield return option;
        }
    }

    public class JobDriver_TalkCourier : JobDriver
    {
        private Pawn Courier => (Pawn)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Courier, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !CourierConversationUtility.CanTalkToCourier(Courier));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() => CourierConversationUtility.ShowDialog(Courier, pawn));
        }
    }

    public class QuestPart_CourierDemand : QuestPart
    {
        public Faction faction;
        public string inSignal;

        private const int SteelDemand = 6000;
        private const int ComponentDemand = 300;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag == inSignal)
            {
                ShowDemandDialog();
            }
        }

        private void ShowDemandDialog()
        {
            Faction resolvedFaction = faction ?? (MooGirlContentDefOf.MooGirl_GiantCorporations_Hostile != null
                ? Find.FactionManager.FirstFactionOfDef(MooGirlContentDefOf.MooGirl_GiantCorporations_Hostile)
                : null);

            Dialog_MessageBox dialog = new Dialog_MessageBox(
                "MooGirl.CourierDemandDialogText".Translate(SteelDemand, ComponentDemand),
                "MooGirl.CourierDemandPay".Translate(),
                () => PayDemand(resolvedFaction),
                "MooGirl.CourierDemandRefuse".Translate(),
                () => MakeHostile(resolvedFaction),
                "MooGirl.CourierDemandTitle".Translate(),
                buttonADestructive: false);
            Find.WindowStack.Add(dialog);
        }

        private static void PayDemand(Faction faction)
        {
            if (CountOnPlayerMaps(ThingDefOf.Steel) >= SteelDemand && CountOnPlayerMaps(ThingDefOf.ComponentIndustrial) >= ComponentDemand)
            {
                ConsumeFromPlayerMaps(ThingDefOf.Steel, SteelDemand);
                ConsumeFromPlayerMaps(ThingDefOf.ComponentIndustrial, ComponentDemand);
                Messages.Message("MooGirl.CourierDemandPaid".Translate(), MessageTypeDefOf.PositiveEvent);
                return;
            }
            Messages.Message("MooGirl.CourierDemandInsufficient".Translate(), MessageTypeDefOf.NegativeEvent);
            MakeHostile(faction);
        }

        private static int CountOnPlayerMaps(ThingDef thingDef)
        {
            int available = 0;
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                List<Thing> things = map.listerThings.ThingsOfDef(thingDef);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed || !thing.Spawned) continue;
                    available += thing.stackCount;
                }
            }
            return available;
        }

        private static void ConsumeFromPlayerMaps(ThingDef thingDef, int count)
        {
            int remaining = count;
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                List<Thing> things = map.listerThings.ThingsOfDef(thingDef);
                for (int i = things.Count - 1; i >= 0 && remaining > 0; i--)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed || !thing.Spawned) continue;
                    int taken = System.Math.Min(remaining, thing.stackCount);
                    Thing payment = thing.SplitOff(taken);
                    payment.Destroy();
                    remaining -= taken;
                }
                if (remaining <= 0) return;
            }
        }

        private static void MakeHostile(Faction faction)
        {
            if (faction == null || faction.HostileTo(Faction.OfPlayer)) return;
            Faction.OfPlayer.TryAffectGoodwillWith(
                faction,
                Faction.OfPlayer.GoodwillToMakeHostile(faction),
                canSendMessage: true,
                canSendHostilityLetter: true);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref inSignal, "inSignal");
        }

        public override void AssignDebugData()
        {
            base.AssignDebugData();
            inSignal = "SendDemand";
        }
    }
}
