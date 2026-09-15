using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Mugirl
{
    public class IncidentWorker_Mugirl_FusionInvestment : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!base.CanFireNowSub(parms) || MugirlMod.Settings?.enableMugirlFusionInvestmentEvent != true)
            {
                return false;
            }

            Map map = parms.target as Map;
            if (map == null)
            {
                return false;
            }

            if (!MugirlGameUtility.TryGetGameComponent(out MugirlStoryState state))
            {
                return false;
            }

            if (!MugirlTickUtility.TryGetCurrentGameTick(out int currentTick))
            {
                return false;
            }

            return !state.fusionInvestmentAccepted
                && !state.fusionInvestmentPending
                && !state.fusionInvestmentInvestorActive
                && currentTick >= state.fusionInvestmentNextOfferTick;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms.target as Map;
            if (map == null || !MugirlFusionInvestmentUtility.TrySpawnInvestor(map, out Pawn investor))
            {
                return false;
            }

            MugirlGameUtility.TryReceiveLetter(
                "Mugirl.FusionInvestment.LetterLabel".Translate(),
                "Mugirl.FusionInvestment.LetterText".Translate(),
                LetterDefOf.NeutralEvent,
                investor);
            MugirlGameUtility.TrySignalForceNormalSpeedShort();
            return true;
        }
    }

    // Save compatibility for older games that already had the investment choice letter open.
    public class ChoiceLetter_MugirlFusionInvestment : ChoiceLetter
    {
        public Map map;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                Map targetMap = ResolveMap();
                for (int i = 0; i < MugirlFusionInvestmentUtility.InvestmentAmounts.Length; i++)
                {
                    int amount = MugirlFusionInvestmentUtility.InvestmentAmounts[i];
                    DiaOption option = new DiaOption("Mugirl.FusionInvestment.OptionInvest".Translate(amount));
                    if (targetMap == null)
                    {
                        option.Disable("Mugirl.FusionInvestment.OptionNoMap".Translate());
                    }
                    else if (!MugirlFusionInvestmentUtility.HasSilver(targetMap, amount))
                    {
                        option.Disable("Mugirl.FusionInvestment.OptionNoSilver".Translate(amount));
                    }
                    else
                    {
                        option.action = delegate
                        {
                            if (MugirlFusionInvestmentUtility.TryAcceptInvestment(targetMap, amount, null))
                            {
                                MugirlGameUtility.TryRemoveLetter(this);
                            }
                        };
                    }

                    option.resolveTree = true;
                    yield return option;
                }

                DiaOption postpone = new DiaOption("Mugirl.FusionInvestment.OptionPostpone".Translate())
                {
                    resolveTree = true
                };
                yield return postpone;

                DiaOption reject = new DiaOption("Mugirl.FusionInvestment.OptionReject".Translate())
                {
                    action = delegate
                    {
                        MugirlFusionInvestmentUtility.ScheduleNextOffer();
                        Messages.Message("Mugirl.FusionInvestment.MessageRejected".Translate(), MessageTypeDefOf.NeutralEvent, historical: false);
                        MugirlGameUtility.TryRemoveLetter(this);
                    },
                    resolveTree = true
                };
                yield return reject;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref map, "map");
        }

        private Map ResolveMap()
        {
            if (map != null)
            {
                return map;
            }

            MugirlGameUtility.TryResolvePlayerEventMap(out Map resolvedMap);
            map = resolvedMap;
            return map;
        }
    }

    internal static class MugirlFusionInvestmentUtility
    {
        internal static readonly int[] InvestmentAmounts = { 1000, 3000, 5000 };
        internal static readonly IntRange ResultDelayTicks = new IntRange(60000 * 14, 60000 * 28);

        private static readonly int[] MilkFoodRewardCounts = { 30, 90, 180 };

        private const int ReofferDelayTicks = 60000 * 60;
        private const int PostponeDelayTicks = 60000 * 7;
        private const int InvestorWaitTicks = 60000 * 6;

        internal static bool TrySpawnInvestor(Map map, out Pawn investor)
        {
            investor = null;
            if (map == null || !RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 spawnCell, map, 0f))
            {
                return false;
            }

            MugirlGameUtility.TryGetRandomNonHostileFaction(out Faction faction, allowHidden: false, minTechLevel: TechLevel.Medieval);
            PawnGenerationRequest request = new PawnGenerationRequest(
                PawnKindDefOf.Villager,
                faction,
                PawnGenerationContext.NonPlayer,
                map.Tile,
                forceGenerateNewPawn: true,
                allowDead: false,
                allowDowned: false,
                canGeneratePawnRelations: false,
                mustBeCapableOfViolence: false,
                forceAddFreeWarmLayerIfNeeded: false,
                allowGay: true,
                allowPregnant: false,
                forceRecruitable: false,
                developmentalStages: DevelopmentalStage.Adult);

            investor = PawnGenerator.GeneratePawn(request);
            if (investor == null)
            {
                return false;
            }

            MugirlEventUtility.MarkFusionInvestor(investor);
            GenSpawn.Spawn(investor, spawnCell, map);
            if (!investor.Spawned)
            {
                MugirlGeneratedPawnUtility.Discard(investor);
                investor = null;
                return false;
            }

            IntVec3 waitCell = FindInvestorWaitCell(map);
            LordMaker.MakeNewLord(
                investor.Faction,
                new LordJob_WaitForDurationThenExit(waitCell, InvestorWaitTicks),
                map,
                Gen.YieldSingle(investor));

            if (MugirlGameUtility.TryGetGameComponent(out MugirlStoryState state))
            {
                state.fusionInvestmentInvestorActive = true;
                state.fusionInvestmentInvestor = investor;
                state.WakeStoryService();
            }

            return true;
        }

        internal static void ScheduleNextOffer()
        {
            ScheduleNextOffer(ReofferDelayTicks);
        }

        internal static void SchedulePostponedOffer()
        {
            ScheduleNextOffer(PostponeDelayTicks);
        }

        internal static bool HasSilver(Map map, int amount)
        {
            return CountSilver(map, amount) >= amount;
        }

        internal static bool TryAcceptInvestment(Map targetMap, int amount, Pawn investor)
        {
            if (!TryTakeSilver(targetMap, amount))
            {
                Messages.Message("Mugirl.FusionInvestment.MessageNoSilver".Translate(amount), MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            if (MugirlGameUtility.TryGetGameComponent(out MugirlStoryState state))
            {
                state.fusionInvestmentAccepted = true;
                state.fusionInvestmentPending = true;
                state.fusionInvestmentAmount = amount;
                state.fusionInvestmentTimer = ResultDelayTicks.RandomInRange;
                state.WakeStoryService();
            }

            Messages.Message("Mugirl.FusionInvestment.MessageAccepted".Translate(amount), MessageTypeDefOf.PositiveEvent);
            CorporateNetwork.Current?.RememberFusionInvestor(investor);
            ResolveInvestor(investor);
            return true;
        }

        internal static void RejectInvestment(Pawn investor)
        {
            ScheduleNextOffer();
            Messages.Message("Mugirl.FusionInvestment.MessageRejected".Translate(), MessageTypeDefOf.NeutralEvent, historical: false);
            ResolveInvestor(investor);
        }

        internal static void PostponeInvestment(Pawn investor)
        {
        }

        internal static void NotifyInvestorAttacked(Pawn investor)
        {
            if (investor == null || !MugirlEventUtility.IsFusionInvestor(investor))
            {
                return;
            }

            if (MugirlGameUtility.TryGetGameComponent(out MugirlStoryState state))
            {
                state.fusionInvestmentInvestorActive = false;
                state.fusionInvestmentInvestor = null;
                state.WakeStoryService();
                if (!state.fusionInvestmentAccepted && !state.fusionInvestmentPending)
                {
                    ScheduleNextOffer(PostponeDelayTicks);
                }
            }

            MugirlEventUtility.ClearFusionInvestor(investor);
            StartLeaving(investor);
        }

        internal static void DevDeliverPendingResult()
        {
            if (!MugirlGameUtility.TryGetGameComponent(out MugirlStoryState state) || !state.fusionInvestmentPending)
            {
                Messages.Message("Mugirl.FusionInvestment.DevDeliver.NoPending".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            state.fusionInvestmentTimer = 0;
            DeliverResult(state);
        }

        internal static bool TryTakeSilver(Map map, int amount)
        {
            if (CountSilver(map, amount) < amount)
            {
                return false;
            }

            int remaining = amount;
            List<Thing> things = map.listerThings.AllThings;
            for (int i = things.Count - 1; i >= 0 && remaining > 0; i--)
            {
                Thing thing = things[i];
                if (!IsUsableSilver(thing))
                {
                    continue;
                }

                int taken = Mathf.Min(remaining, thing.stackCount);
                remaining -= taken;
                if (taken >= thing.stackCount)
                {
                    thing.Destroy();
                }
                else
                {
                    thing.stackCount -= taken;
                }
            }

            return remaining <= 0;
        }

        internal static void Tick(MugirlStoryState state)
        {
            Tick(state, 1, checkInvestor: true);
        }

        internal static void Tick(MugirlStoryState state, int elapsedTicks, bool checkInvestor)
        {
            if (state == null)
            {
                return;
            }

            if (checkInvestor)
            {
                ClearStaleInvestor(state);
            }

            if (!state.fusionInvestmentPending)
            {
                return;
            }

            state.fusionInvestmentTimer -= elapsedTicks > 0 ? elapsedTicks : 1;
            if (state.fusionInvestmentTimer > 0)
            {
                return;
            }

            DeliverResult(state);
        }

        internal static bool CanTalkToInvestor(Pawn investor)
        {
            if (investor == null
                || !investor.Spawned
                || investor.Dead
                || investor.Downed
                || investor.InAggroMentalState
                || !MugirlEventUtility.IsFusionInvestor(investor))
            {
                return false;
            }

            return MugirlGameUtility.TryGetGameComponent(out MugirlStoryState state)
                && state.fusionInvestmentInvestorActive
                && !state.fusionInvestmentAccepted
                && !state.fusionInvestmentPending
                && (state.fusionInvestmentInvestor == null || state.fusionInvestmentInvestor == investor);
        }

        internal static bool CanNegotiateWithInvestor(Pawn negotiator, Pawn investor)
        {
            return negotiator != null
                && MugirlWildSlaveUtility.IsPlayerFaction(negotiator.Faction)
                && negotiator.RaceProps?.Humanlike == true
                && !negotiator.Dead
                && !negotiator.Downed
                && CanTalkToInvestor(investor);
        }

        internal static void ShowDialog(Pawn investor, Pawn negotiator)
        {
            if (!CanNegotiateWithInvestor(negotiator, investor))
            {
                Messages.Message("Mugirl.FusionInvestment.AlreadyResolved".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Map targetMap = investor.Map;
            DiaNode node = new DiaNode("Mugirl.FusionInvestment.DialogText".Translate(investor.Named("INVESTOR"), negotiator.Named("NEGOTIATOR")));
            for (int i = 0; i < InvestmentAmounts.Length; i++)
            {
                int amount = InvestmentAmounts[i];
                DiaOption option = new DiaOption("Mugirl.FusionInvestment.OptionInvest".Translate(amount));
                if (targetMap == null)
                {
                    option.Disable("Mugirl.FusionInvestment.OptionNoMap".Translate());
                }
                else if (!HasSilver(targetMap, amount))
                {
                    option.Disable("Mugirl.FusionInvestment.OptionNoSilver".Translate(amount));
                }
                else
                {
                    option.action = delegate
                    {
                        TryAcceptInvestment(targetMap, amount, investor);
                    };
                }

                option.resolveTree = true;
                node.options.Add(option);
            }

            node.options.Add(new DiaOption("Mugirl.FusionInvestment.OptionPostpone".Translate())
            {
                action = delegate
                {
                    PostponeInvestment(investor);
                },
                resolveTree = true
            });

            node.options.Add(new DiaOption("Mugirl.FusionInvestment.OptionReject".Translate())
            {
                action = delegate
                {
                    RejectInvestment(investor);
                },
                resolveTree = true
            });

            MugirlGameUtility.TryAddWindow(new Dialog_NodeTree(node, delayInteractivity: true, title: "Mugirl.FusionInvestment.DialogTitle".Translate()));
        }

        private static void DeliverResult(MugirlStoryState state)
        {
            int tier = InvestmentTierForAmount(state.fusionInvestmentAmount);

            if (!MugirlGameUtility.TryResolvePlayerEventMap(out Map map))
            {
                state.fusionInvestmentTimer = 60000;
                return;
            }

            List<Thing> rewards = new List<Thing>();
            AddMilkFoodRewards(rewards, MilkFoodRewardCountForTier(tier));
            AddWildMugirlRewards(rewards, tier);

            IntVec3 dropCell = DropCellFinder.TradeDropSpot(map);
            DropPodUtility.DropThingsNear(dropCell, map, rewards, 110, canInstaDropDuringInit: false, leaveSlag: false, canRoofPunch: true, forbid: false);

            MugirlGameUtility.TryReceiveLetter(
                "Mugirl.FusionInvestment.ResultLetterLabel".Translate(),
                "Mugirl.FusionInvestment.ResultLetterText".Translate(),
                LetterDefOf.PositiveEvent,
                new TargetInfo(dropCell, map));

            state.fusionInvestmentPending = false;
            state.fusionInvestmentTimer = 0;
        }

        private static IntVec3 FindInvestorWaitCell(Map map)
        {
            if (CellFinder.TryFindRandomCellNear(map.Center, map, 20, c => c.Standable(map) && !c.Fogged(map), out IntVec3 cell))
            {
                return cell;
            }

            return map.Center;
        }

        private static void ScheduleNextOffer(int delayTicks)
        {
            if (MugirlGameUtility.TryGetGameComponent(out MugirlStoryState state))
            {
                int currentTick = MugirlTickUtility.CurrentGameTickOrFallback(state.fusionInvestmentNextOfferTick);
                state.fusionInvestmentNextOfferTick = currentTick + delayTicks;
            }
        }

        private static void ResolveInvestor(Pawn investor)
        {
            if (MugirlGameUtility.TryGetGameComponent(out MugirlStoryState state))
            {
                state.fusionInvestmentInvestorActive = false;
                state.fusionInvestmentInvestor = null;
                state.WakeStoryService();
            }

            if (investor == null)
            {
                return;
            }

            MugirlEventUtility.ClearFusionInvestor(investor);
            StartLeaving(investor);
        }

        private static void ClearStaleInvestor(MugirlStoryState state)
        {
            if (!state.fusionInvestmentInvestorActive)
            {
                return;
            }

            Pawn investor = state.fusionInvestmentInvestor;
            if (investor != null && investor.Spawned && !investor.Dead && !investor.Downed && MugirlEventUtility.IsFusionInvestor(investor))
            {
                return;
            }

            state.fusionInvestmentInvestorActive = false;
            state.fusionInvestmentInvestor = null;
            if (!state.fusionInvestmentAccepted && !state.fusionInvestmentPending)
            {
                ScheduleNextOffer(PostponeDelayTicks);
            }
        }

        private static void StartLeaving(Pawn investor)
        {
            if (investor == null || !investor.Spawned || investor.Dead)
            {
                return;
            }

            Lord lord = investor.GetLord();
            lord?.RemovePawn(investor);
            LordMaker.MakeNewLord(
                investor.Faction,
                new LordJob_ExitMapBest(LocomotionUrgency.Jog, canDig: false, canDefendSelf: true),
                investor.Map,
                Gen.YieldSingle(investor));
        }

        private static int InvestmentTierForAmount(int amount)
        {
            for (int i = InvestmentAmounts.Length - 1; i >= 0; i--)
            {
                if (amount >= InvestmentAmounts[i])
                {
                    return i + 1;
                }
            }

            return 1;
        }

        private static int MilkFoodRewardCountForTier(int tier)
        {
            int index = Mathf.Clamp(tier, 1, MilkFoodRewardCounts.Length) - 1;
            return MilkFoodRewardCounts[index];
        }

        private static void AddMilkFoodRewards(List<Thing> rewards, int totalCount)
        {
            ThingDef[] foodDefs =
            {
                MugirlContentDefOf.Mugirl_MilkPudding,
                MugirlContentDefOf.Mugirl_Cheese,
                MugirlContentDefOf.Mugirl_AgedCheese,
                MugirlContentDefOf.Mugirl_BaotaSugar,
                MugirlContentDefOf.Mugirl_MilkCandy,
                MugirlContentDefOf.Mugirl_CowCake,
                MugirlContentDefOf.Mugirl_MilkPowder,
                MugirlContentDefOf.Mugirl_MilkTablet,
                MugirlContentDefOf.Mugirl_MilkCreamApple
            };

            List<ThingDef> validFoodDefs = new List<ThingDef>();
            for (int i = 0; i < foodDefs.Length; i++)
            {
                if (foodDefs[i] != null)
                {
                    validFoodDefs.Add(foodDefs[i]);
                }
            }

            int remaining = totalCount;
            while (remaining > 0 && validFoodDefs.Count > 0)
            {
                ThingDef def = validFoodDefs.RandomElement();

                Thing thing = ThingMaker.MakeThing(def);
                if (thing == null)
                {
                    validFoodDefs.Remove(def);
                    continue;
                }

                thing.stackCount = Mathf.Min(remaining, Mathf.Max(1, def.stackLimit));
                remaining -= thing.stackCount;
                rewards.Add(thing);
            }
        }

        private static void AddWildMugirlRewards(List<Thing> rewards, int count)
        {
            for (int i = 0; i < count; i++)
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    Mugirl_DefOf.Mugirl_WildMugirl,
                    MugirlWildSlaveUtility.PlayerFaction,
                    PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: true,
                    forceAddFreeWarmLayerIfNeeded: false,
                    allowGay: true,
                    allowPregnant: false,
                    forceRecruitable: true,
                    dontGiveWeapon: true,
                    fixedGender: Gender.Female,
                    developmentalStages: DevelopmentalStage.Adult);
                request.ForceNoIdeoGear = true;

                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (pawn != null)
                {
                    MugirlWildSlaveUtility.CleanupAfterJoiningPlayer(pawn);
                    rewards.Add(pawn);
                }
            }
        }

        private static int CountSilver(Map map, int stopAt)
        {
            if (map?.listerThings?.AllThings == null)
            {
                return 0;
            }

            int count = 0;
            List<Thing> things = map.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (!IsUsableSilver(thing))
                {
                    continue;
                }

                count += thing.stackCount;
                if (count >= stopAt)
                {
                    break;
                }
            }

            return count;
        }

        private static bool IsUsableSilver(Thing thing)
        {
            Faction playerFaction = MugirlWildSlaveUtility.PlayerFaction;
            return thing != null
                && playerFaction != null
                && thing.Spawned
                && !thing.Destroyed
                && thing.def == ThingDefOf.Silver
                && thing.IsInValidStorage()
                && !thing.IsForbidden(playerFaction);
        }
    }

    internal static class MugirlFusionInvestmentDebugActions
    {
        [DebugAction("Mugirl", "Deliver pending bovine fusion reward", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DeliverPendingFusionInvestmentResult()
        {
            MugirlFusionInvestmentUtility.DevDeliverPendingResult();
        }
    }

    public class FloatMenuProvider_TalkFusionInvestor : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => true;

        public override bool TargetPawnValid(Pawn target, FloatMenuContext context)
        {
            return base.TargetPawnValid(target, context) && MugirlFusionInvestmentUtility.CanTalkToInvestor(target);
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn actor = context.FirstSelectedPawn;
            if (actor == null || !MugirlWildSlaveUtility.IsPlayerFaction(actor.Faction) || actor.RaceProps?.Humanlike != true)
            {
                yield break;
            }

            FloatMenuOption option = FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption("Mugirl.FusionInvestment.TalkOption".Translate(), () =>
                {
                    TryStartTalkJob(actor, clickedPawn);
                }, MenuOptionPriority.High),
                actor,
                clickedPawn);

            if (!actor.CanReserveAndReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
            {
                option.Disabled = true;
                option.Label = "Mugirl.FusionInvestment.TalkOptionDisabled".Translate(option.Label, "Mugirl.FusionInvestment.TalkCannotReach".Translate());
            }

            yield return option;
        }

        private static void TryStartTalkJob(Pawn actor, Pawn investor)
        {
            if (!MugirlFusionInvestmentUtility.CanNegotiateWithInvestor(actor, investor)
                || !actor.CanReserveAndReach(investor, PathEndMode.Touch, Danger.Deadly))
            {
                return;
            }

            Job job = JobMaker.MakeJob(MugirlContentDefOf.Mugirl_TalkFusionInvestor, investor);
            actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }

    public class JobDriver_TalkFusionInvestor : JobDriver
    {
        private Pawn Investor => job.GetTarget(TargetIndex.A).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn investor = Investor;
            return MugirlFusionInvestmentUtility.CanNegotiateWithInvestor(pawn, investor)
                && pawn.Reserve(investor, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !MugirlFusionInvestmentUtility.CanNegotiateWithInvestor(pawn, Investor));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() => MugirlFusionInvestmentUtility.ShowDialog(Investor, pawn));
        }
    }
}
