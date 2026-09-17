using System.Collections.Generic;
using RimWorld;
using RimWorld.QuestGen;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Mugirl
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
            if (!string.IsNullOrEmpty(inSignal) && signal.tag == inSignal)
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
                MugirlLog.WarningOnce("CourierRaid.Spawn.NoMap", "Mugirl.CourierRaid.SpawnLog.NoMap".Translate().ToString());
                return;
            }
            if (faction == null)
            {
                MugirlLog.WarningOnce("CourierRaid.Spawn.NoFaction", "Mugirl.CourierRaid.SpawnLog.NoFaction".Translate().ToString());
                return;
            }
            if (!spawnCell.IsValid && !RCellFinder.TryFindRandomPawnEntryCell(out spawnCell, map, 0f))
            {
                MugirlLog.WarningOnce("CourierRaid.Spawn.NoSpawnCell", "Mugirl.CourierRaid.SpawnLog.NoSpawnCell".Translate().ToString());
                return;
            }

            // 运货员始终以巨企雇员身份入场；开战时对话逻辑会另行切断派系归属，避免影响外交。
            Faction courierFaction = faction;
            PawnGenerationRequest request = new PawnGenerationRequest(
                MugirlContentDefOf.AI_GC_Courier,
                courierFaction,
                PawnGenerationContext.NonPlayer,
                map.Tile,
                forceGenerateNewPawn: true,
                canGeneratePawnRelations: false,
                mustBeCapableOfViolence: true,
                allowFood: false);
            courier = PawnGenerator.GeneratePawn(request);
            if (courier == null || courier.inventory?.innerContainer == null || courier.mindState == null)
            {
                MugirlLog.WarningOnce(
                    "CourierGenerationFailed",
                    "Courier raid could not generate a usable courier pawn; skipping courier spawn.");
                MugirlGeneratedPawnUtility.Discard(courier);
                courier = null;
                return;
            }

            AddToInventory(courier, MugirlContentDefOf.Mugirl_CourierDiary, 1);
            AddToInventory(courier, MugirlContentDefOf.Mugirl_SlaveApparelKey_Medieval, 6);
            AddToInventory(courier, MugirlContentDefOf.Mugirl_SlaveApparelKey_Industrial, 4);

            GenSpawn.Spawn(courier, spawnCell, map);
            if (!courier.Spawned)
            {
                MugirlLog.WarningOnce(
                    "CourierSpawnFailed",
                    "Courier raid generated a courier but failed to spawn it on the target map.");
                MugirlGeneratedPawnUtility.Discard(courier);
                courier = null;
                return;
            }

            LordMaker.MakeNewLord(
                courier.Faction,
                new LordJob_WaitForDurationThenExit(spawnCell, ConversationWaitTicks),
                map,
                new List<Pawn> { courier });

            CorporateIntroduction.Current?.RememberCourier(courier, map);

            MugirlGameUtility.TryReceiveLetter(
                "Mugirl.CourierContactLetterLabel".Translate(),
                "Mugirl.CourierContactLetterText".Translate(),
                LetterDefOf.NeutralEvent,
                courier);
            MugirlGameUtility.TrySignalForceNormalSpeedShort();
        }

        private static void AddToInventory(Pawn pawn, ThingDef thingDef, int count)
        {
            if (pawn?.inventory?.innerContainer == null || thingDef == null || count <= 0) return;
            Thing thing = ThingMaker.MakeThing(thingDef);
            if (thing == null)
            {
                return;
            }

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
            Scribe_Values.Look(ref spawnCell, "spawnCell", IntVec3.Invalid);
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
                && courier.kindDef == MugirlContentDefOf.AI_GC_Courier
                && HasCourierPayload(courier);
        }

        public static void ShowDialog(Pawn courier, Pawn negotiator)
        {
            if (!CanNegotiateWithCourier(negotiator, courier))
            {
                Messages.Message("Mugirl.CourierAlreadyResolved".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Dialog_MessageBox dialog = new Dialog_MessageBox(
                "Mugirl.CourierDialogText".Translate(courier.Named("COURIER"), negotiator.Named("NEGOTIATOR")),
                "Mugirl.CourierDialogFight".Translate(),
                () => TryStartFight(courier),
                "Mugirl.CourierDialogLeaveItems".Translate(),
                () => TryDropItemsAndLeave(courier),
                "Mugirl.CourierDialogTitle".Translate(),
                buttonADestructive: true);
            MugirlGameUtility.TryAddWindow(dialog);
        }

        public static bool CanNegotiateWithCourier(Pawn negotiator, Pawn courier)
        {
            return negotiator != null
                && MugirlWildSlaveUtility.IsPlayerFaction(negotiator.Faction)
                && negotiator.RaceProps?.Humanlike == true
                && !negotiator.Dead
                && !negotiator.Downed
                && CanTalkToCourier(courier);
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
                ThingDef def = inventory[i]?.def;
                if (def == MugirlContentDefOf.Mugirl_CourierDiary
                    || def == MugirlContentDefOf.Mugirl_SlaveApparelKey_Medieval
                    || def == MugirlContentDefOf.Mugirl_SlaveApparelKey_Industrial)
                {
                    return true;
                }
            }

            return false;
        }

        private static void TryDropItemsAndLeave(Pawn courier)
        {
            if (!CanTalkToCourier(courier))
            {
                Messages.Message("Mugirl.CourierAlreadyResolved".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            courier.inventory.DropAllNearPawn(courier.Position, forbid: false, unforbid: true);
            StartLeaving(courier);
            CorporateIntroduction.Current?.NotifyCourierChoice(courier, released: true);
            EndRelatedQuest(courier, QuestEndOutcome.Success);
            Messages.Message("Mugirl.CourierItemsDroppedMessage".Translate(courier.Named("COURIER")), courier, MessageTypeDefOf.PositiveEvent);
        }

        private static void TryStartFight(Pawn courier)
        {
            if (!CanTalkToCourier(courier))
            {
                Messages.Message("Mugirl.CourierAlreadyResolved".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            if (courier.mindState?.mentalStateHandler == null)
            {
                Messages.Message("Mugirl.CourierAlreadyResolved".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            RemoveFromCurrentLord(courier);
            if (courier.Faction != null)
            {
                courier.SetFaction(null);
            }

            courier.mindState.mentalStateHandler.TryStartMentalState(
                MentalStateDefOf.Berserk,
                "Mugirl.CourierFightReason".Translate(),
                forced: true,
                forceWake: true,
                transitionSilently: true);
            CorporateIntroduction.Current?.NotifyCourierChoice(courier, released: false);
            EndRelatedQuest(courier, QuestEndOutcome.Unknown);
            Messages.Message("Mugirl.CourierFightMessage".Translate(courier.Named("COURIER")), courier, MessageTypeDefOf.ThreatSmall);
            MugirlGameUtility.TrySignalForceNormalSpeedShort();
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
            if (!MugirlGameUtility.TryGetQuestsListForReading(out List<Quest> quests))
            {
                return;
            }

            for (int i = 0; i < quests.Count; i++)
            {
                Quest quest = quests[i];
                if (quest == null || quest.root != MugirlContentDefOf.Mugirl_CourierRaid || quest.State != QuestState.Ongoing)
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
            if (actor == null || !MugirlWildSlaveUtility.IsPlayerFaction(actor.Faction) || actor.RaceProps?.Humanlike != true)
            {
                yield break;
            }

            FloatMenuOption option = FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption("Mugirl.CourierTalkOption".Translate(clickedPawn.Named("COURIER")), () =>
                {
                    TryStartTalkJob(actor, clickedPawn);
                }, MenuOptionPriority.High),
                actor,
                clickedPawn);

            if (!actor.CanReserveAndReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
            {
                option.Disabled = true;
                option.Label = "Mugirl.CourierTalkOptionDisabled".Translate(option.Label, "Mugirl.CourierTalkCannotReach".Translate());
            }

            yield return option;
        }

        private static void TryStartTalkJob(Pawn actor, Pawn courier)
        {
            if (!CourierConversationUtility.CanNegotiateWithCourier(actor, courier)
                || !actor.CanReserveAndReach(courier, PathEndMode.Touch, Danger.Deadly))
            {
                return;
            }

            Job job = JobMaker.MakeJob(MugirlContentDefOf.Mugirl_TalkCourier, courier);
            actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }

    public class JobDriver_TalkCourier : JobDriver
    {
        private Pawn Courier => job.GetTarget(TargetIndex.A).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn courier = Courier;
            return CourierConversationUtility.CanNegotiateWithCourier(pawn, courier)
                && pawn.Reserve(courier, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !CourierConversationUtility.CanNegotiateWithCourier(pawn, Courier));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() => CourierConversationUtility.ShowDialog(Courier, pawn));
        }
    }

}
