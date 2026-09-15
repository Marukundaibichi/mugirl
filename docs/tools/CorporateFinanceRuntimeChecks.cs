// Optional development harness: compile into an isolated test build, never the release DLL.
// Run only in a disposable real Unity game. This spends silver, creates pawns, sends letters,
// advances game time FORWARD and changes faction goodwill. It does not edit mod configuration.
// The caller supplies an unlocked account, a neutral corporation, a player home map, a powered
// trade beacon and at least 50,000 tradable silver. Run this BEFORE hostile quest-branch tests.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mugirl;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

public static class CorporateFinanceRuntimeChecks
{
    public static void Run(Map map, CorporateNetwork network, CorporateTradeContext context, Action<string, bool> check)
    {
        if (map == null || network == null || context == null || check == null) throw new ArgumentNullException();
        string reason;
        if (!context.IsValid || !network.CanTrade(context, out reason))
            throw new InvalidOperationException("Finance fixture requires an unlocked, nonhostile terminal.");
        if (context.SilverCount < 50000 || network.HasLoan || network.HasCollateral || network.FinancePendingSilver != 0)
            throw new InvalidOperationException("Finance fixture requires 50,000 silver and no existing loan/escrow.");
        IntVec3 tradeCell = context.AvailableThings.First(t => t.Spawned && t.def == ThingDefOf.Silver).Position;
        Pawn previousNegotiator = context.Negotiator;
        // A dedicated pawn keeps ideology changes confined to the disposable fixture. When
        // Ideology is active, CheckPeople explicitly assigns a real generated test ideology;
        // forceNoIdeo alone does not stop normal faction code from assigning its primary one.
        Pawn negotiator = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,
            faction: Faction.OfPlayer, forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
            canGeneratePawnRelations: false, forceNoIdeo: true, developmentalStages: DevelopmentalStage.Adult));
        GenSpawn.Spawn(negotiator, CellFinder.RandomClosewalkCellNear(map.Center, map, 8), map);
        context.Negotiator = negotiator;
        try
        {
            CheckLoan(map, network, context, tradeCell, check);
            CheckLiquidation(map, network, context, tradeCell, check);
            CheckPeople(map, network, context, negotiator, check);
        }
        finally
        {
            context.Negotiator = previousNegotiator;
            context.Invalidate();
            // Do not rewind time or overwrite the saved business state. All generated artifacts
            // intentionally remain in the disposable map so the parent runner can inspect them.
        }
    }

    private static void CheckLoan(Map map, CorporateNetwork network, CorporateTradeContext context,
        IntVec3 tradeCell, Action<string, bool> check)
    {
        const int principal = 500;
        Dictionary<Thing, int> pledge = MakeGoldPledge(map, context, tradeCell, principal, out Thing original);
        int pledgedCount = pledge[original];
        int sourceCount = original.stackCount;
        int hp = original.HitPoints;
        ThingDef material = original.Stuff;
        float marketValue = original.MarketValue;
        int signed = CorporateNetwork.Now;
        bool accepted = network.TryTakeLoan(context, principal, pledge, out string reason);
        check("Loan signs against actual available collateral: " + (reason ?? "ok"), accepted);
        if (!accepted) throw new InvalidOperationException(reason);
        Thing escrow = network.LoanCollateral.Single();
        check("Loan splits only the pledged quantity and places it in the persistent vault",
            original.Spawned && original.stackCount == sourceCount - pledgedCount
            && escrow.stackCount == pledgedCount && escrow != original
            && escrow.holdingOwner == network.Vault && !escrow.Spawned);
        check("Collateral locks its actual appraisal and preserves identity/material/condition",
            network.LoanCollateralValue == Mathf.FloorToInt(marketValue * pledgedCount)
            && escrow.def == original.def && escrow.Stuff == material && escrow.HitPoints == hp);
        check("Loan records the stated 14-day due date and initial principal",
            network.LoanDueTick == signed + 14 * CorporateNetwork.DayTicks
            && Near(network.LoanPrincipal, 500f) && Near(network.LoanInterest, 0f));
        check("A second loan cannot reuse the same account/collateral",
            !network.TryTakeLoan(context, principal, pledge, out reason) && network.LoanCollateral.Count == 1);
        check("Silver itself cannot be pledged", !CorporateNetwork.IsEligibleCollateral(
            context.AvailableThings.First(t => t.def == ThingDefOf.Silver)));

        AdvanceTo(signed + CorporateNetwork.DayTicks - 1);
        network.UpdateLoanAccrual();
        check("A partial first day incurs no full-day interest", Near(network.LoanInterest, 0f) && network.LoanBalance == 500);
        AdvanceTo(signed + CorporateNetwork.DayTicks);
        network.UpdateLoanAccrual();
        check("Exactly one day charges 2 percent of 500, without rounding early", Near(network.LoanInterest, 10f) && network.LoanBalance == 510);
        network.UpdateLoanAccrual();
        network.UpdateLoanAccrual();
        check("Repeated accounting at the same time cannot accrue twice", Near(network.LoanInterest, 10f));

        int silverBefore = context.SilverCount;
        bool partiallyPaid = network.TryRepayLoan(context, 260, out reason);
        check("Partial payment debits actual silver, interest first then principal", partiallyPaid
            && context.SilverCount == silverBefore - 260 && Near(network.LoanInterest, 0f) && Near(network.LoanPrincipal, 250f));
        check("Partial repayment cannot release any pledged items", network.HasCollateral
            && !escrow.Destroyed && escrow.holdingOwner == network.Vault);
        network.TryClaimFinanceAssets(context, out reason);
        check("Claiming during an active loan cannot return collateral", escrow.holdingOwner == network.Vault);
        AdvanceTo(signed + 2 * CorporateNetwork.DayTicks);
        network.UpdateLoanAccrual();
        check("The next full day uses the reduced 250 principal", Near(network.LoanInterest, 5f) && network.LoanBalance == 255);

        Faction corporation = network.CorporateFaction;
        int baseGoodwill = corporation.BaseGoodwillWith(Faction.OfPlayer);
        AdvanceTo(network.LoanDueTick);
        network.UpdateLoanAccrual();
        check("At maturity, days 2 through 14 total 65 simple interest", Near(network.LoanInterest, 65f) && network.LoanBalance == 315);
        check("Default locks FactionUtility.HostileTo in both directions", network.LoanDefaulted
            && corporation.HostileTo(Faction.OfPlayer) && Faction.OfPlayer.HostileTo(corporation));
        check("Default also locks both RelationKindWith queries", corporation.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile
            && Faction.OfPlayer.RelationKindWith(corporation) == FactionRelationKind.Hostile);
        check("Default preserves the underlying non-debt goodwill", corporation.BaseGoodwillWith(Faction.OfPlayer) == baseGoodwill);
        check("Default schedules its first collection one day later", network.LoanNextRaidTick == CorporateNetwork.Now + CorporateNetwork.DayTicks);
        check("Default retains collateral instead of automatically liquidating it", escrow.holdingOwner == network.Vault && !escrow.Destroyed);
        bool gift = corporation.TryAffectGoodwillWith(Faction.OfPlayer, 30, false, false);
        check("Positive diplomacy cannot remove the debt lock", !gift
            && corporation.BaseGoodwillWith(Faction.OfPlayer) == baseGoodwill && corporation.HostileTo(Faction.OfPlayer));
        corporation.TryAffectGoodwillWith(Faction.OfPlayer, -10, false, false);
        int independentGoodwill = corporation.BaseGoodwillWith(Faction.OfPlayer);
        check("Independent negative diplomacy still persists", independentGoodwill < baseGoodwill);

        AdvanceTo(network.LoanDueTick + CorporateNetwork.DayTicks);
        network.UpdateLoanAccrual();
        check("First overdue day adds 3 percent of remaining principal, without compounding",
            Near(network.LoanInterest, 72.5f) && network.LoanBalance == 323);
        List<Pawn> spawnedCollectors = CheckActualCollectionRaids(map, network, check, out int cancelledCollectionTick);
        silverBefore = context.SilverCount;
        int settlement = network.LoanBalance;
        bool settled = network.TryRepayLoan(context, int.MaxValue, out reason);
        check("Full settlement is capped at the quoted balance and charges once", settled
            && !network.HasLoan && network.LoanBalance == 0 && context.SilverCount == silverBefore - settlement);
        check("Settlement removes both debt locks and stops future collection",
            !network.LoanDefaulted && network.LoanNextRaidTick == 0
            && corporation.HostileTo(Faction.OfPlayer) == (corporation.RelationWith(Faction.OfPlayer).kind == FactionRelationKind.Hostile)
            && corporation.RelationKindWith(Faction.OfPlayer) == corporation.RelationWith(Faction.OfPlayer).kind);
        check("Settlement does not erase unrelated diplomatic penalties", corporation.BaseGoodwillWith(Faction.OfPlayer) == independentGoodwill);
        HashSet<Pawn> beforeCancelledService = CorporateMapPawns(corporation);
        AdvanceTo(Math.Max(CorporateNetwork.Now + 250, cancelledCollectionTick + 250));
        network.GameComponentTick();
        check("The real service does not spawn another collector wave after settlement",
            !CorporateMapPawns(corporation).Except(beforeCancelledService).Any()
            && !network.HasLoan && network.LoanNextRaidTick == 0);
        check("Settled collateral is returned as the exact same object", network.TryClaimFinanceAssets(context, out reason)
            && !network.HasCollateral && !escrow.Destroyed && escrow.holdingOwner != network.Vault
            && (escrow.Spawned || escrow.holdingOwner != null) && escrow.stackCount == pledgedCount && escrow.Stuff == material && escrow.HitPoints == hp);
        ThingOwner receiver = escrow.holdingOwner;
        network.TryClaimFinanceAssets(context, out reason);
        check("Repeated collateral claims do not move or duplicate the returned object", escrow.holdingOwner == receiver
            && network.LoanCollateral.Count == 0 && !network.TryRepayLoan(context, 1, out reason));
        // Only the exact pawns born in this probe leave the disposable map. Do not kill them,
        // remove other corporate pawns, or leave an army near the following quest fixtures.
        foreach (Pawn collector in spawnedCollectors)
        {
            if (collector == null || collector.Destroyed || !collector.Spawned) continue;
            collector.GetLord()?.Notify_PawnLost(collector, PawnLostCondition.ExitedMap);
            collector.DeSpawn();
            if (!collector.IsWorldPawn()) Find.WorldPawns.PassToWorld(collector, PawnDiscardDecideMode.Decide);
        }
    }

    private static List<Pawn> CheckActualCollectionRaids(Map map, CorporateNetwork network,
        Action<string, bool> check, out int nextScheduledTick)
    {
        Faction corporation = network.CorporateFaction;
        HashSet<Pawn> originalPawns = CorporateMapPawns(corporation);
        MethodInfo target = AccessTools.Method(typeof(StorytellerUtility), nameof(StorytellerUtility.DefaultThreatPointsNow));
        MethodInfo prefix = typeof(CorporateFinanceRuntimeChecks).GetMethod(nameof(CollectionPointsPrefix), BindingFlags.Static | BindingFlags.NonPublic);
        Harmony scope = new Harmony("mugirl.tests.corporate-collection." + Guid.NewGuid().ToString("N"));
        collectionTestMap = map;
        try
        {
            // Scope only the input points of this test map. Production raid workers, faction
            // groups, arrival, scheduling, hostility and the GameComponent service remain real.
            // No storyteller/Def configuration is changed, and the prefix is always removed.
            scope.Patch(target, prefix: new HarmonyMethod(prefix));
            AdvanceTo(Math.Max(CorporateNetwork.Now, network.LoanNextRaidTick));
            network.GameComponentTick();
            List<Pawn> first = CorporateMapPawns(corporation).Except(originalPawns).ToList();
            int delay = network.LoanNextRaidTick - CorporateNetwork.Now;
            check("First collection service creates actual hostile corporate pawns (count=" + first.Count + ")",
                first.Count > 0 && first.All(p => !p.Dead && p.Spawned && p.Faction == corporation && p.HostileTo(Faction.OfPlayer)));
            check("A successful collection schedules its next wave 2 to 4 days later",
                delay >= 2 * CorporateNetwork.DayTicks && delay <= 4 * CorporateNetwork.DayTicks);
            int repeatTick = network.LoanNextRaidTick;
            HashSet<Pawn> firstWavePawns = CorporateMapPawns(corporation);
            AdvanceTo(Math.Max(CorporateNetwork.Now + 250, repeatTick));
            network.GameComponentTick();
            List<Pawn> second = CorporateMapPawns(corporation).Except(firstWavePawns).ToList();
            check("Outstanding debt produces a distinct second collection wave (count=" + second.Count + ")",
                network.LoanDefaulted && network.HasLoan && second.Count > 0
                && second.All(p => p.Spawned && !p.Dead && p.Faction == corporation && p.HostileTo(Faction.OfPlayer)));
            int repeatDelay = network.LoanNextRaidTick - CorporateNetwork.Now;
            check("Continued collection retains the 2 to 4 day interval",
                repeatDelay >= 2 * CorporateNetwork.DayTicks && repeatDelay <= 4 * CorporateNetwork.DayTicks);
            nextScheduledTick = network.LoanNextRaidTick;
            return CorporateMapPawns(corporation).Except(originalPawns).ToList();
        }
        finally
        {
            scope.Unpatch(target, HarmonyPatchType.Prefix, scope.Id);
            collectionTestMap = null;
        }
    }

    private static Map collectionTestMap;

    private static bool CollectionPointsPrefix(IIncidentTarget target, ref float __result)
    {
        if (collectionTestMap == null || target != collectionTestMap) return true;
        __result = 500f;
        return false;
    }

    private static HashSet<Pawn> CorporateMapPawns(Faction faction)
    {
        return new HashSet<Pawn>(Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned)
            .Where(p => p.Faction == faction));
    }

    private static void CheckLiquidation(Map map, CorporateNetwork network, CorporateTradeContext context,
        IntVec3 tradeCell, Action<string, bool> check)
    {
        // A second real loan verifies the independent irreversible liquidation path.
        Dictionary<Thing, int> pledge = MakeGoldPledge(map, context, tradeCell, 500, out Thing original);
        if (!network.TryTakeLoan(context, 500, pledge, out string reason)) throw new InvalidOperationException(reason);
        Thing escrow = network.LoanCollateral.Single();
        check("Collateral cannot be liquidated before default", !network.TryLiquidateCollateral(context, out reason) && !escrow.Destroyed);
        AdvanceTo(network.LoanDueTick);
        network.UpdateLoanAccrual();
        int balance = network.LoanBalance;
        int proceeds = network.LoanLiquidationValue;
        check("Voluntary liquidation reduces debt by its disclosed proceeds", network.TryLiquidateCollateral(context, out reason)
            && escrow.Destroyed && !network.HasCollateral && network.LoanBalance == Math.Max(0, balance - proceeds));
        int after = network.LoanBalance;
        int refundable = network.FinancePendingSilver;
        check("A liquidation can never be repeated", !network.TryLiquidateCollateral(context, out reason)
            && network.LoanBalance == after && network.FinancePendingSilver == refundable);
        if (network.HasLoan && !network.TryRepayLoan(context, int.MaxValue, out reason)) throw new InvalidOperationException(reason);
        network.TryClaimFinanceAssets(context, out reason);
        check("Clearing the residual liquidation debt leaves no further debt or collateral", !network.HasLoan && !network.HasCollateral);
    }

    private static void CheckPeople(Map map, CorporateNetwork network, CorporateTradeContext context,
        Pawn negotiator, Action<string, bool> check)
    {
        network.EnsureWeeklyOffers();
        List<CorporatePersonOffer> before = network.PeopleOffers.ToList();
        network.EnsureWeeklyOffers();
        check("Reopening personnel stock preserves the exact offers and pawns",
            before.SequenceEqual(network.PeopleOffers));
        CorporatePersonOffer offer = before.FirstOrDefault(o => !o.delivered && !o.paid && o.pawn != null);
        CheckIdeologyGuard(network, context, offer, check);
        if (ModsConfig.IdeologyActive)
        {
            check("The weekly roster contains the configured number of real adult pawns",
                before.Count == CorporatePeopleDefOf.Mugirl_CorporatePeople.weeklyCount
                && before.All(o => o.pawn != null && o.pawn.DevelopmentalStage == DevelopmentalStage.Adult
                    && o.pawn.holdingOwner == network.Vault));
            if (offer == null) throw new InvalidOperationException("No actual personnel offer was generated.");
            int silver = context.SilverCount;
            Pawn purchased = offer.pawn;
            bool bought = network.TryPurchasePerson(context, offer, out string reason);
            check("A personnel purchase charges exactly the saved price", bought && offer.paid
                && offer.delivered && context.SilverCount == silver - offer.price);
            check("A purchased pawn joins as a player slave through the real guest tracker",
                purchased.IsSlaveOfColony && purchased.Faction == Faction.OfPlayer
                && purchased.holdingOwner != network.Vault && (purchased.Spawned || purchased.holdingOwner != null));
            silver = context.SilverCount;
            check("An already delivered personnel offer cannot be bought again", !network.TryPurchasePerson(context, offer, out reason)
                && context.SilverCount == silver);
        }
        else check("Without Ideology, the roster does not generate purchasable slaves", before.Count == 0);

        ThoughtDef memory = CorporatePeopleDefOf.Mugirl_CorporateSoldMugirl;
        negotiator.needs.mood.thoughts.memories.RemoveMemoriesOfDef(memory);
        Ideo colonyIdeo = Faction.OfPlayer.ideos?.PrimaryIdeo;
        List<Precept> colonyPrecepts = colonyIdeo?.PreceptsListForReading.ToList();
        Ideo testIdeo = ModsConfig.IdeologyActive ? MakePersonnelTestIdeo(negotiator) : null;
        if (testIdeo != null)
            check("Personnel fixture owns a separate legal ideology that permits slave trading",
                negotiator.Ideo == testIdeo && testIdeo != colonyIdeo
                && IdeoUtility.DoerWillingToDo(HistoryEventDefOf.SoldSlave, negotiator));
        int historyBefore = Find.HistoryEventsManager.GetRecentCountWithinTicks(HistoryEventDefOf.SoldSlave, int.MaxValue);
        for (int i = 0; i < 2; i++)
        {
            Pawn sale = PawnGenerator.GeneratePawn(new PawnGenerationRequest(Mugirl_DefOf.Mugirl_Slave,
                faction: null, forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                canGeneratePawnRelations: false, forceNoIdeo: true, developmentalStages: DevelopmentalStage.Adult));
            sale.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(sale, CellFinder.RandomClosewalkCellNear(map.Center, map, 8), map);
            check("Free adult mugirl colonists are excluded from procurement", !network.PeopleSaleCandidates(context).Contains(sale));
            sale.guest.SetGuestStatus(Faction.OfPlayer, ModsConfig.IdeologyActive ? GuestStatus.Slave : GuestStatus.Prisoner);
            check("A real secure captive appears in procurement candidates", network.PeopleSaleCandidates(context).Contains(sale));
            int price = network.PeopleSalePrice(sale);
            int recordCount = network.Records.Count;
            check("A stale personnel sale quote is rejected without transfer", !network.TrySellPerson(context, sale, price + 1, out string reason)
                && sale.Spawned && network.Records.Count == recordCount);
            if (i == 0 && testIdeo != null)
            {
                SetTestSlaveryPrecept(testIdeo, "Slavery_Abhorrent");
                int rejectionSilver = context.SilverCount;
                int rejectionPending = network.PeoplePendingSilver;
                check("The real abhorrent precept makes this negotiator unwilling to sell slaves",
                    !IdeoUtility.DoerWillingToDo(HistoryEventDefOf.SoldSlave, negotiator));
                check("Ideology refusal preserves the captive, silver, receivable and transaction history",
                    !network.TrySellPerson(context, sale, price, out reason)
                    && reason == "Mugirl.CorporatePeople.IdeoRefusal".Translate().ToString()
                    && sale.Spawned && sale.IsSlaveOfColony && sale.Faction == Faction.OfPlayer
                    && network.PeopleSaleCandidates(context).Contains(sale)
                    && context.SilverCount == rejectionSilver && network.PeoplePendingSilver == rejectionPending
                    && network.Records.Count == recordCount
                    && Find.HistoryEventsManager.GetRecentCountWithinTicks(HistoryEventDefOf.SoldSlave, int.MaxValue) == historyBefore
                    && !negotiator.needs.mood.thoughts.memories.Memories.Any(t => t.def == memory));
                SetTestSlaveryPrecept(testIdeo, "Slavery_Acceptable");
                check("Replacing the fixture precept restores willingness through vanilla ideology rules",
                    IdeoUtility.DoerWillingToDo(HistoryEventDefOf.SoldSlave, negotiator));
            }
            List<Tale> previousTales = Find.TaleManager.AllTalesListForReading.ToList();
            bool sold = network.TrySellPerson(context, sale, price, out reason);
            check("Personnel sale transfers the actual pawn and records its receivable: " + (reason ?? "ok"), sold
                && !sale.Spawned && !network.PeopleSaleCandidates(context).Contains(sale)
                && network.Records.Last().key == "Mugirl.CorporatePeople.Sold" && network.Records.Last().amount == price);
            check("Corporate sale adds exactly one additional colonist memory", negotiator.needs.mood.thoughts.memories.Memories.Count(t => t.def == memory) == 1);
            List<Tale> addedTales = Find.TaleManager.AllTalesListForReading.Except(previousTales)
                .Where(t => t.def == TaleDefOf.SoldPrisoner).ToList();
            check("This sale creates a vanilla SoldPrisoner tale for the actual negotiator and captive",
                sold && addedTales.Count == 1 && addedTales[0].Concerns(negotiator) && addedTales[0].Concerns(sale));
            int silver = context.SilverCount;
            int pending = network.PeoplePendingSilver;
            recordCount = network.Records.Count;
            List<Tale> talesAfterSale = Find.TaleManager.AllTalesListForReading.ToList();
            check("Selling the same pawn twice cannot pay or record twice", !network.TrySellPerson(context, sale, price, out reason)
                && context.SilverCount == silver && network.PeoplePendingSilver == pending && network.Records.Count == recordCount
                && talesAfterSale.SequenceEqual(Find.TaleManager.AllTalesListForReading));
        }
        check("Two sales create two vanilla SoldSlave history events", Find.HistoryEventsManager.GetRecentCountWithinTicks(
            HistoryEventDefOf.SoldSlave, int.MaxValue) == historyBefore + 2);
        // SoldPrisoner.maxPerPawn is 1. TaleManager.Add immediately culls an earlier unused
        // tale with this same dominant pawn; two successful sales must not imply two survivors.
        check("SoldPrisoner tales obey vanilla retention for the same negotiator",
            Find.TaleManager.AllTalesListForReading.Count(t => t.def == TaleDefOf.SoldPrisoner
                && t.Unused && t.DominantPawn == negotiator) == TaleDefOf.SoldPrisoner.maxPerPawn);
        if (testIdeo != null)
            check("Personnel ideology tests leave the colony primary ideology and its precepts unchanged",
                Faction.OfPlayer.ideos.PrimaryIdeo == colonyIdeo
                && colonyPrecepts.SequenceEqual(colonyIdeo.PreceptsListForReading));

        List<Pawn> retired = network.PeopleOffers.Where(o => !o.paid && !o.delivered).Select(o => o.pawn).ToList();
        AdvanceTo(Math.Max(CorporateNetwork.Now, network.NextRefreshTick));
        network.EnsureWeeklyOffers();
        typeof(CorporateNetwork).GetMethod("PeopleTick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(network, null);
        check("Weekly turnover removes retired offers without retaining duplicate vault ownership",
            retired.All(p => !network.PeopleOffers.Any(o => o.pawn == p) && !network.Vault.Contains(p)));
    }

    private static Ideo MakePersonnelTestIdeo(Pawn negotiator)
    {
        // Raider conflicts with Slavery_Abhorrent. Exclude it when generating this independent
        // ideology so both tested slavery precepts remain legal, using native generation APIs.
        Ideo ideo = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms(Faction.OfPlayer.def,
            disallowedMemes: new List<MemeDef> { DefDatabase<MemeDef>.GetNamed("Raider") },
            name: "Corporate personnel validation"));
        SetTestSlaveryPrecept(ideo, "Slavery_Acceptable");
        Find.IdeoManager.Add(ideo);
        negotiator.ideo.SetIdeo(ideo);
        return ideo;
    }

    private static void SetTestSlaveryPrecept(Ideo ideo, string defName)
    {
        PreceptDef desired = DefDatabase<PreceptDef>.GetNamed(defName);
        AcceptanceReport legal = ideo.CanAddPreceptAllFactions(desired);
        if (!legal.Accepted) throw new InvalidOperationException("Invalid test ideology precept " + defName + ": " + legal.Reason);
        foreach (Precept precept in ideo.PreceptsListForReading.Where(p => p.def.issue == desired.issue).ToList())
            ideo.RemovePrecept(precept, replacing: true);
        ideo.AddPrecept(PreceptMaker.MakePrecept(desired), init: true, generatingFor: Faction.OfPlayer.def);
    }

    private static void CheckIdeologyGuard(CorporateNetwork network, CorporateTradeContext context,
        CorporatePersonOffer offer, Action<string, bool> check)
    {
        // The parent starts a separate no-Ideology process. Never change global DLC state.
        if (ModsConfig.IdeologyActive) return;
        int silver = context.SilverCount;
        bool wasPaid = offer?.paid ?? false;
        bool accepted = network.TryPurchasePerson(context, offer, out string reason);
        check("The no-Ideology purchase guard rejects with a clear reason before payment", !accepted
            && reason == "Mugirl.CorporatePeople.RequiresIdeology".Translate().ToString()
            && context.SilverCount == silver && (offer?.paid ?? false) == wasPaid);
    }

    private static Dictionary<Thing, int> MakeGoldPledge(Map map, CorporateTradeContext context,
        IntVec3 tradeCell, int principal, out Thing original)
    {
        original = ThingMaker.MakeThing(ThingDefOf.Gold);
        int pledgeCount = Mathf.CeilToInt(principal / (original.MarketValue * CorporateNetwork.LoanCollateralRatio)) + 1;
        original.stackCount = pledgeCount + 7;
        if (original.stackCount > original.def.stackLimit) throw new InvalidOperationException("Gold fixture exceeds stack limit.");
        // GenPlace may merge a second fixture into the first loan's residual gold, destroying
        // the new object reference. Spawn into a genuinely empty, covered item cell instead.
        bool placed = false;
        foreach (IntVec3 cell in GenRadial.RadialCellsAround(tradeCell, 7f, true))
        {
            if (!cell.InBounds(map) || !cell.Walkable(map)
                || cell.GetThingList(map).Any(t => t.def.category == ThingCategory.Item || t.def.category == ThingCategory.Building)) continue;
            GenSpawn.Spawn(original, cell, map);
            original.SetForbidden(false, false);
            context.Invalidate();
            if (context.AvailableThings.Contains(original)) { placed = true; break; }
            original.DeSpawn();
        }
        if (!placed) throw new InvalidOperationException("No empty gold-collateral cell is covered by the powered trade beacon.");
        return new Dictionary<Thing, int> { { original, pledgeCount } };
    }

    private static void AdvanceTo(int tick)
    {
        if (tick < Find.TickManager.TicksGame) throw new InvalidOperationException("The runtime harness must never rewind game time.");
        Find.TickManager.DebugSetTicksGame(tick);
    }

    private static bool Near(float actual, float expected) { return Math.Abs(actual - expected) < 0.001f; }
}
