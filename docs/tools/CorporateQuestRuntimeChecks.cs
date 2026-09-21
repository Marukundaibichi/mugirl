// Optional development harness. Compile into a test build only, never the release DLL.
// Run only in a DISPOSABLE test save: this deliberately spends silver, supplies products,
// creates pawns and sends quest letters. The caller must provide a player home map with
// a powered trade beacon, at least 2,000 available silver, and an unlocked corporate account.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mugirl;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

public static class CorporateQuestRuntimeChecks
{
    public static void Run(Map map, CorporateNetwork network, CorporateTradeContext context, Action<string, bool> check)
    {
        if (map == null || network == null || context == null || check == null) throw new ArgumentNullException();
        string blocked = null;
        if (!context.IsValid || !network.CanTrade(context, out blocked))
            throw new InvalidOperationException("Corporate fixture is unavailable: " + blocked);
        if (context.SilverCount < 2000) throw new InvalidOperationException("Fixture requires 2,000 tradable silver.");
        List<CorporateMission> missions = (List<CorporateMission>)network.Missions;
        List<CorporateMission> previous = missions.ToList();
        missions.Clear();
        List<Thing> spawned = new List<Thing>();
        int id = 900000;
        IntVec3 tradeCell = context.AvailableThings.First(t => t.Spawned).Position;
        try
        {
            // A real vanilla nutrition-counted recipe, using a real workbench and real goods.
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamed("CookMealSimple");
            ThingDef rice = DefDatabase<ThingDef>.GetNamed("RawRice");
            ThingDef stove = DefDatabase<ThingDef>.GetNamed("ElectricStove");
            Thing workbench = ThingMaker.MakeThing(stove, stove.MadeFromStuff ? ThingDefOf.Steel : null);
            workbench.SetFaction(Faction.OfPlayer);
            GenPlace.TryPlaceThing(workbench, map.Center, map, ThingPlaceMode.Near);
            spawned.Add(workbench);
            int rawCount = recipe.ingredients[0].CountRequiredOfFor(rice, recipe) * 2;
            check("Processing converts nutrition into whole ingredient counts", rawCount > recipe.ingredients[0].GetBaseCount() * 2);
            CorporateMission processing = new CorporateMission
            {
                id = id++, kind = CorporateMissionKind.Processing, recipe = recipe,
                product = recipe.products[0].thingDef, batches = 2, count = recipe.products[0].count * 2,
                duration = 7 * CorporateNetwork.DayTicks,
                cost = 200, reward = 150, acceptByTick = CorporateNetwork.Now + 60000,
                ingredients = new List<ThingDefCountClass> { new ThingDefCountClass(rice, rawCount) }
            };
            missions.Add(processing);
            int silver = context.SilverCount;
            bool accepted = network.AcceptMission(processing, context, out string reason);
            check("Processing contract accepts with a usable recipe/workbench", accepted);
            if (!accepted) throw new InvalidOperationException(reason);
            check("Processing acceptance sets a future seven-day deadline", processing.deadline == CorporateNetwork.Now + 7 * CorporateNetwork.DayTicks);
            check("Processing debits the refundable deposit exactly once", context.SilverCount == silver - 200
                && !network.AcceptMission(processing, context, out _));
            Thing meal = ThingMaker.MakeThing(processing.product);
            meal.stackCount = processing.count + 3;
            // 主夹具可能用白银铺满信标，Near 会把产品挤到信标外；固定在已验证交易格。
            GenSpawn.Spawn(meal, tradeCell, map);
            spawned.Add(meal);
            context.Invalidate();
            bool mealAvailable = context.AvailableContractThings.Contains(meal);
            bool mealFresh = !meal.IsNotFresh();
            bool mealHealthy = !meal.def.useHitPoints || meal.HitPoints >= meal.MaxHitPoints * 0.9f;
            check("Processing fixture availability: actual=" + mealAvailable + ", spawned=" + meal.Spawned + ", destroyed=" + meal.Destroyed
                + ", position=" + meal.Position + ", tradeCell=" + tradeCell + ", product=" + meal.def.defName + ", contract=" + processing.product.defName,
                mealAvailable);
            check("Processing fixture freshness: actual=" + mealFresh + ", rot=" + meal.TryGetComp<CompRottable>()?.Stage, mealFresh);
            check("Processing fixture durability: hp=" + meal.HitPoints + "/" + meal.MaxHitPoints + ", useHitPoints=" + meal.def.useHitPoints, mealHealthy);
            check("Processing fixture product definition matches its contract: actual=" + meal.def.defName + ", expected=" + processing.product.defName,
                meal.def == processing.product);
            int productBefore = context.AvailableContractThings.Where(t => t.def == processing.product).Sum(t => t.stackCount);
            check("Processing consumes only the agreed product quantity", network.SubmitMissionProducts(processing, context)
                && processing.state == CorporateMissionState.Ready && processing.progress == processing.count
                && context.AvailableContractThings.Where(t => t.def == processing.product).Sum(t => t.stackCount) == productBefore - processing.count);
            check("Processing refund/reward can only be claimed once", network.ClaimMission(processing, context)
                && processing.state == CorporateMissionState.Completed && !network.ClaimMission(processing, context));

            CorporateMission investment = new CorporateMission
            {
                id = id++, kind = CorporateMissionKind.Investment, cost = 1000,
                acceptByTick = CorporateNetwork.Now + 60000, duration = 60000
            };
            missions.Add(investment);
            silver = context.SilverCount;
            check("Investment physically debits principal", network.AcceptMission(investment, context, out _)
                && context.SilverCount == silver - investment.cost);
            int fixedReturn = investment.reward;
            check("Investment saves one of the advertised payouts", fixedReturn == 600 || fixedReturn == 1050 || fixedReturn == 1600);
            check("Investment cannot be abandoned for a refund", !network.AbandonMission(investment));
            investment.deadline = CorporateNetwork.Now - 1;
            typeof(CorporateNetwork).GetField("nextMissionCheck", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(network, 0);
            Call(network, "QuestsTick");
            check("Maturity preserves the signed payout", investment.state == CorporateMissionState.Ready && investment.reward == fixedReturn);
            check("Investment payout settles exactly once", network.ClaimMission(investment, context)
                && !network.ClaimMission(investment, context) && investment.reward == fixedReturn);

            ThingDef product = ThingDefOf.ComponentIndustrial;
            CorporateMission saleA = new CorporateMission { id = id++, kind = CorporateMissionKind.Distribution, state = CorporateMissionState.Active,
                product = product, count = 10, deadline = CorporateNetwork.Now + 60000 };
            CorporateMission saleB = new CorporateMission { id = id++, kind = CorporateMissionKind.Distribution, state = CorporateMissionState.Active,
                product = product, count = 10, deadline = CorporateNetwork.Now + 60000 };
            missions.Add(saleA); missions.Add(saleB);
            CorporateMission frozen = (CorporateMission)Invoke(network, "BuildDistributionOffer");
            int frozenGoal = frozen.SalesTarget;
            float originalRatio = CorporateNetwork.MissionConfig.salesTarget;
            try
            {
                CorporateNetwork.MissionConfig.salesTarget = 0.2f;
                check("New distribution offers freeze their saved sales goal across config changes", frozen.salesGoal > 0 && frozen.SalesTarget == frozenGoal);
                check("Legacy distribution without a goal keeps the original 80 percent", saleA.SalesTarget == 8);
            }
            finally { CorporateNetwork.MissionConfig.salesTarget = originalRatio; }
            int sold = saleA.SalesTarget + 2;
            network.NotifyDistributionSale(product, null, sold);
            check("One sale is allocated across contracts without double counting", saleA.progress == saleA.SalesTarget
                && saleB.progress == 2 && saleA.state == CorporateMissionState.Ready);
            network.NotifyDistributionSale(rice, null, 200);
            check("Unrelated goods never advance distribution", saleB.progress == 2);
            check("Distribution pays no artificial cash subsidy", saleA.reward == 0 && saleB.reward == 0);
            saleB.deadline = CorporateNetwork.Now - 1;
            network.NotifyDistributionSale(product, null, 5);
            check("Expired contracts reject later sales", saleB.progress == 2);
            RunActualDistributionSale(map, network, context, missions, id++, check);

            // The narrative choices use actual pawns. Only the choice handler is invoked by
            // reflection so the harness does not depend on clicking a modal dialogue.
            Pawn researcher = PawnGenerator.GeneratePawn(PawnKindDefOf.Villager);
            GenSpawn.Spawn(researcher, CellFinder.RandomClosewalkCellNear(map.Center, map, 8), map);
            spawned.Add(researcher);
            CorporateMission enforce = new CorporateMission { id = id++, kind = CorporateMissionKind.Fusion,
                state = CorporateMissionState.Active, count = 1, spawned = true, investor = researcher,
                reward = 4000, goodwill = 10, deadline = CorporateNetwork.Now + 60000, targets = new List<Pawn> { researcher } };
            missions.Add(enforce);
            Call(network, "BeginResearchCombat", enforce);
            check("Enforcement starts real hostility without killing targets or paying", enforce.choice == CorporateResearchChoice.Execute
                && researcher.Faction == Faction.OfAncientsHostile && !researcher.Dead && enforce.state == CorporateMissionState.Active);

            Pawn protectedResearcher = PawnGenerator.GeneratePawn(PawnKindDefOf.Villager);
            GenSpawn.Spawn(protectedResearcher, CellFinder.RandomClosewalkCellNear(map.Center, map, 8), map);
            spawned.Add(protectedResearcher);
            CorporateMission protect = new CorporateMission { id = id++, kind = CorporateMissionKind.Fusion,
                state = CorporateMissionState.Active, count = 1, spawned = true, investor = protectedResearcher,
                reward = 4000, goodwill = 10, targets = new List<Pawn> { protectedResearcher } };
            missions.Add(protect);
            int relationBefore = network.CorporateFaction.GoodwillWith(Faction.OfPlayer);
            Call(network, "ProtectResearchers", protect);
            int relationAfter = network.CorporateFaction.GoodwillWith(Faction.OfPlayer);
            check("Protection closes corporate payment and leaves researchers alive", protect.state == CorporateMissionState.Completed
                && protect.choice == CorporateResearchChoice.Protect && protect.reward == 0 && !protectedResearcher.Dead);
            check("Protection applies the configured relationship penalty", relationAfter <= relationBefore);
            List<Thing> gift = protect.pendingGoods.ToList();
            check("Protection prepares the exact configured gift with saved vault ownership", gift.Count > 0
                && gift.All(t => t.holdingOwner == network.Vault && t.stackCount <= t.def.stackLimit)
                && CorporateNetwork.MissionConfig.fusionProtectionRewards.All(reward =>
                    gift.Where(t => t.def == reward.thingDef).Sum(t => t.stackCount) == reward.count));
            check("Unavailable delivery preserves the researchers' gift", !network.ReceiveMissionGoods(protect, new CorporateTradeContext(map, () => false))
                && protect.pendingGoods.SequenceEqual(gift));
            Call(network, "ProtectResearchers", protect);
            check("Repeated protection cannot apply goodwill or prepare gifts twice", network.CorporateFaction.GoodwillWith(Faction.OfPlayer) == relationAfter
                && protect.pendingGoods.SequenceEqual(gift));
            Call(network, "ResearcherAttacked", protectedResearcher);
            check("Attacking after protection revokes thanks without reopening payment", protect.gratitudeRevoked
                && protect.state == CorporateMissionState.Completed && protect.reward == 0 && !network.ClaimMission(protect, context)
                && protect.pendingGoods.Count == 0 && gift.All(t => t.Destroyed && !network.Vault.Contains(t)));

            CorporateMission delivered = new CorporateMission { id = id++, kind = CorporateMissionKind.Fusion,
                state = CorporateMissionState.Active, reward = 4000, goodwill = 10 };
            missions.Add(delivered);
            Call(network, "ProtectResearchers", delivered);
            List<Thing> deliveredGift = delivered.pendingGoods.ToList();
            typeof(CorporateNetwork).GetField("nextMissionCheck", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(network, 0);
            Call(network, "QuestsTick");
            check("Protection gifts dispatch automatically to the home map", delivered.pendingGoods.Count == 0
                && deliveredGift.Count > 0 && deliveredGift.All(t => !t.Destroyed && t.holdingOwner != network.Vault
                    && (t.Spawned || t.holdingOwner != null)));
            int vaultCount = network.Vault.Count;
            Call(network, "ProtectResearchers", delivered);
            check("Delivered protection gifts cannot be regenerated or claimed as corporate silver", delivered.pendingGoods.Count == 0
                && network.Vault.Count == vaultCount && !network.ClaimMission(delivered, context));
        }
        finally
        {
            foreach (CorporateMission mission in missions.Where(m => !previous.Contains(m)))
                Call(network, "DiscardPendingMissionGoods", mission);
            foreach (Thing thing in spawned) if (thing != null && !thing.Destroyed) thing.Destroy();
            missions.Clear();
            missions.AddRange(previous);
            context.Invalidate();
        }
    }

    private static void RunActualDistributionSale(Map map, CorporateNetwork network, CorporateTradeContext context,
        List<CorporateMission> missions, int id, Action<string, bool> check)
    {
        ITrader previousTrader = TradeSession.trader;
        Pawn previousNegotiator = TradeSession.playerNegotiator;
        TradeDeal previousDeal = TradeSession.deal;
        bool previousGiftMode = TradeSession.giftMode;
        TradeShip ship = null;
        Thing goods = null;
        try
        {
            goods = ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
            goods.stackCount = 5;
            GenSpawn.Spawn(goods, context.AvailableThings.First(t => t.Spawned).Position, map);
            context.Invalidate();
            CorporateMission contract = new CorporateMission
            {
                id = id, kind = CorporateMissionKind.Distribution, state = CorporateMissionState.Active,
                product = goods.def, count = 5, salesGoal = 4, deadline = CorporateNetwork.Now + CorporateNetwork.DayTicks
            };
            missions.Add(contract);
            ship = new TradeShip(DefDatabase<TraderKindDef>.GetNamed("Orbital_BulkGoods"));
            map.passingShipManager.AddShip(ship);
            TradeSession.SetupWith(ship, context.Negotiator ?? map.mapPawns.FreeColonistsSpawned.First(), false);
            Tradeable trade = TradeSession.deal.AllTradeables.FirstOrDefault(t => t.thingsColony.Contains(goods));
            check("Actual orbital deal includes the fixture's available distribution goods", trade != null && trade.TraderWillTrade);
            if (trade == null || !trade.TraderWillTrade) throw new InvalidOperationException("Orbital trade fixture could not offer its goods.");
            int before = ship.CountHeldOf(goods.def, null);
            trade.AdjustTo(-2);
            check("Actual orbital deal schedules a sale of two goods", trade.ActionToDo == TradeAction.PlayerSells && trade.CountToTransfer == -2);
            trade.ResolveTrade();
            check("Real ResolveTrade transfers goods to the ship and triggers distribution accounting", ship.CountHeldOf(goods.def, null) == before + 2
                && contract.progress == 2 && contract.state == CorporateMissionState.Active);
        }
        finally
        {
            // 还原原交易会话，不让一次性商船或货物进入存档。
            TradeSession.trader = previousTrader;
            TradeSession.playerNegotiator = previousNegotiator;
            TradeSession.deal = previousDeal;
            TradeSession.giftMode = previousGiftMode;
            if (ship != null)
            {
                foreach (Thing thing in ship.Goods.ToList()) if (!thing.Destroyed) thing.Destroy();
                map.passingShipManager.passingShips.Remove(ship);
            }
            if (goods != null && !goods.Destroyed) goods.Destroy();
            context.Invalidate();
        }
    }

    // 独立执行。会真正生成四张 125×125 任务地图，宜放在 LongEvent 中调用。
    // 接受和建图使用生产入口；访客是测试人物，战斗死亡明确用 Kill 模拟。
    public static void RunSites(Map homeMap, CorporateNetwork network, CorporateTradeContext context, Action<string, bool> check)
    {
        if (homeMap == null || network == null || context == null || check == null) throw new ArgumentNullException();
        if (!network.CanTrade(context, out string reason)) throw new InvalidOperationException(reason);
        List<CorporateMission> missions = (List<CorporateMission>)network.Missions;
        List<CorporateMission> previous = missions.ToList();
        List<CorporateMission> fixtures = new List<CorporateMission>();
        List<Pawn> visitors = new List<Pawn>();
        List<Pawn> generatedTargets = new List<Pawn>();
        List<Window> originalWindows = Find.WindowStack.Windows.ToList();
        string[] fields = { "fusionQualificationSeen", "fusionInvitationCreated", "fusionInvitationTick", "retainedFusionInvestor", "retainedFusionInvestorName" };
        Dictionary<string, object> saved = fields.ToDictionary(name => name, name => Field(name).GetValue(network));
        int originalGoodwill = network.CorporateFaction.PlayerGoodwill;
        missions.Clear();
        try
        {
            // 避免取走测试存档已经保留的原投资人。
            Field("retainedFusionInvestor").SetValue(network, null);
            Field("retainedFusionInvestorName").SetValue(network, "site validation team");
            ThingDef chunkDef = DefDatabase<ThingDef>.GetNamedSilentFail("ChunkGranite");
            IntVec3 blockerCell = CellFinder.RandomClosewalkCellNear(homeMap.Center, homeMap, 30);
            Thing chunk = chunkDef == null ? null : ThingMaker.MakeThing(chunkDef);
            if (chunk != null) GenSpawn.Spawn(chunk, blockerCell, homeMap);
            CorporateNetwork.ClearFusionResearchCell(homeMap, blockerCell, removePlants: true);
            check("Research-site cleanup removes pass-through item blockers before prefab validation",
                chunk != null && chunk.Destroyed && blockerCell.Walkable(homeMap));
            ThingDef geyserDef = DefDatabase<ThingDef>.GetNamedSilentFail("SteamGeyser");
            Thing geyser = geyserDef == null ? null : ThingMaker.MakeThing(geyserDef);
            if (geyser != null) GenSpawn.Spawn(geyser, blockerCell, homeMap);
            CorporateNetwork.ClearFusionResearchCell(homeMap, blockerCell, removePlants: true);
            check("Research-site cleanup removes non-destroyable natural blockers without leaking the vanilla override",
                geyser != null && geyser.Destroyed && !Thing.allowDestroyNonDestroyable && blockerCell.Walkable(homeMap));
            for (int branch = 0; branch < 3; branch++)
            {
                network.CorporateFaction.TryAffectGoodwillWith(Faction.OfPlayer, 100 - network.CorporateFaction.PlayerGoodwill, false, false);
                Field("fusionQualificationSeen").SetValue(network, true);
                Field("fusionInvitationCreated").SetValue(network, false);
                Field("fusionInvitationTick").SetValue(network, CorporateNetwork.Now - 1);
                Invoke(network, "TickFusionInvitation");
                CorporateMission mission = missions.Last(m => m.IsSide && m.state == CorporateMissionState.Available);
                fixtures.Add(mission);
                bool accepted = network.AcceptMission(mission, context, out reason);
                check("Research site branch " + branch + " accepts via the real contract/site path: " + reason, accepted);
                if (!accepted) throw new InvalidOperationException(reason);
                Map map = GetOrGenerateMapUtility.GetOrGenerateMap(mission.site.Tile, new IntVec3(125, 1, 125), mission.site.def);
                generatedTargets.AddRange(mission.targets);
                check("Research site branch " + branch + " runs real PostMapGenerate and creates its team", mission.spawned
                    && mission.targets.Count == 4 && mission.targets.All(p => p.Spawned && p.Map == map) && mission.investor != null);
                if (!mission.spawned) throw new InvalidOperationException("Research site worker did not generate its targets.");
                Pawn visitor = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, Faction.OfPlayer,
                    PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true, developmentalStages: DevelopmentalStage.Adult));
                visitors.Add(visitor);
                GenSpawn.Spawn(visitor, CellFinder.RandomClosewalkCellNear(map.Center, map, 12), map);
                int dialogueCount = Find.WindowStack.Windows.OfType<Dialog_NodeTree>().Count();
                Invoke(network, "TickMissionSite", mission);
                Dialog_NodeTree dialogue = Find.WindowStack.Windows.OfType<Dialog_NodeTree>().LastOrDefault();
                check("Research arrival opens a real dialogue for branch " + branch, mission.dialogueShown && dialogue != null
                    && Find.WindowStack.Windows.OfType<Dialog_NodeTree>().Count() == dialogueCount + 1);
                if (dialogue == null) throw new InvalidOperationException("Arrival did not open a dialogue.");
                DiaNode root = (DiaNode)typeof(Dialog_NodeTree).GetField("curNode", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialogue);
                if (branch == 2)
                {
                    dialogue.Close();
                    mission.investor.Kill(null);
                    Invoke(network, "TickMissionSite", mission);
                    check("Simulated non-player death interrupts an undecided research contract without choosing execution", mission.state == CorporateMissionState.Failed
                        && mission.choice == CorporateResearchChoice.Undecided && !network.ClaimMission(mission, context));
                    continue;
                }
                // 激活实际对白选项及其确认节点，不直接调用任务分支处理函数。
                Activate(root.options[branch == 0 ? 1 : 2]);
                DiaNode confirmation = (DiaNode)typeof(Dialog_NodeTree).GetField("curNode", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialogue);
                int goodwillBeforeChoice = network.CorporateFaction.PlayerGoodwill;
                int baseGoodwillBeforeChoice = network.CorporateFaction.BaseGoodwillWith(Faction.OfPlayer);
                int adjustedPenalty = network.CorporateFaction.CalculateAdjustedGoodwillChange(Faction.OfPlayer, CorporateNetwork.MissionConfig.fusionRefusalGoodwill);
                int expectedBaseGoodwill = Mathf.Clamp(baseGoodwillBeforeChoice + adjustedPenalty, -100, 100);
                int expectedGoodwill = Mathf.Min(goodwillBeforeChoice, expectedBaseGoodwill);
                Activate(confirmation.options[0]);
                if (branch == 0)
                {
                    check("Research dialogue execution keeps targets alive and creates real hostility", mission.choice == CorporateResearchChoice.Execute
                        && mission.state == CorporateMissionState.Active && mission.targets.All(p => !p.Dead && p.HostileTo(visitor)));
                    foreach (Pawn target in mission.targets.ToList()) target.Kill(null);
                    Invoke(network, "TickMissionSite", mission);
                    check("Simulated execution kills reach the real completion counter", mission.state == CorporateMissionState.Ready && mission.progress == mission.count);
                    check("Generated execution quest settles once and ends with its own script root", network.ClaimMission(mission, context)
                        && !network.ClaimMission(mission, context) && mission.quest.State == QuestState.EndedSuccess
                        && mission.quest.root == CorporateQuestDefOf.Mugirl_CorporateMission);
                }
                else
                {
                    int alive = mission.targets.Count(p => p != null && !p.Dead);
                    int goodwillAfterChoice = network.CorporateFaction.PlayerGoodwill;
                    check("Research protection confirmation previews its configured gift", confirmation.text.ToString().Contains(CorporateNetwork.FusionProtectionRewardDescription));
                    check("Research protection selected choice: actual=" + mission.choice, mission.choice == CorporateResearchChoice.Protect);
                    check("Research protection contract state: actual=" + mission.state, mission.state == CorporateMissionState.Completed);
                    check("Research protection leaves generated team alive: alive=" + alive + ", total=" + mission.targets.Count,
                        alive == mission.targets.Count && alive == 4);
                    check("Research protection goodwill: before=" + goodwillBeforeChoice + ", after=" + goodwillAfterChoice
                        + ", configured=" + CorporateNetwork.MissionConfig.fusionRefusalGoodwill + ", vanillaAdjusted=" + adjustedPenalty
                        + ", expected=" + expectedGoodwill + ", natural=" + network.CorporateFaction.NaturalGoodwill,
                        goodwillAfterChoice == expectedGoodwill && network.CorporateFaction.BaseGoodwillWith(Faction.OfPlayer) == expectedBaseGoodwill);
                    check("Research protection closes corporate payment: reward=" + mission.reward, mission.reward == 0);
                    check("Research protection quest ends successfully: actual=" + mission.quest?.State, mission.quest?.State == QuestState.EndedSuccess);
                    check("Research protection prepares gifts on the actual quest site", mission.pendingGoods.Count > 0
                        && mission.pendingGoods.All(t => t.holdingOwner == network.Vault));
                }
            }
            CorporateMission failedGeneration = fixtures.Last(m => m.IsSide);
            failedGeneration.state = CorporateMissionState.Failed;
            failedGeneration.spawned = false;
            Field("fusionInvitationCreated").SetValue(network, true);
            Field("fusionInvitationTick").SetValue(network, CorporateNetwork.Now + 60000);
            Invoke(network, "RepairFusionSiteGenerationLockout");
            check("A failed pre-fix research map unlocks one replacement invitation for old saves",
                !(bool)Field("fusionInvitationCreated").GetValue(network)
                && (int)Field("fusionInvitationTick").GetValue(network) <= CorporateNetwork.Now);
            failedGeneration.spawned = true;
            network.CorporateFaction.TryAffectGoodwillWith(Faction.OfPlayer, 100 - network.CorporateFaction.PlayerGoodwill, false, false);
            CorporateMission purge = (CorporateMission)Invoke(network, "BuildPurgeOffer", homeMap);
            check("Real purge offer generator finds an eligible enemy and world tile", purge != null);
            if (purge == null) throw new InvalidOperationException("This test world lacks an eligible shared enemy/site for purge validation.");
            missions.Add(purge);
            fixtures.Add(purge);
            bool purgeAccepted = network.AcceptMission(purge, context, out reason);
            check("Purge accepts through the real world-site path: " + reason, purgeAccepted);
            if (!purgeAccepted) throw new InvalidOperationException(reason);
            Map purgeMap = GetOrGenerateMapUtility.GetOrGenerateMap(purge.site.Tile, new IntVec3(125, 1, 125), purge.site.def);
            generatedTargets.AddRange(purge.targets);
            check("Purge PostMapGenerate creates its actual enemy combat group", purge.spawned && purge.count > 0
                && purge.targets.All(p => p.Spawned && p.Map == purgeMap && p.Faction == purge.enemy));
            foreach (Pawn target in purge.targets.ToList()) target.Kill(null);
            Invoke(network, "TickMissionSite", purge);
            check("Simulated purge kills complete the actual generated target list", purge.state == CorporateMissionState.Ready && purge.progress == purge.count);
            check("Generated purge quest pays once and ends successfully", network.ClaimMission(purge, context)
                && !network.ClaimMission(purge, context) && purge.quest.State == QuestState.EndedSuccess);
        }
        finally
        {
            // 地图图层在 LongEvent 结束回调中初始化；必须排在这些回调后卸载。
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    foreach (Dialog_NodeTree dialog in Find.WindowStack.Windows.OfType<Dialog_NodeTree>().Where(w => !originalWindows.Contains(w)).ToList()) dialog.Close();
                    foreach (Pawn visitor in visitors) if (!visitor.Destroyed) visitor.Destroy();
                    foreach (Pawn target in generatedTargets)
                    {
                        if (target.Corpse != null && !target.Corpse.Destroyed) target.Corpse.Destroy();
                        if (!target.Destroyed) target.Destroy();
                    }
                    foreach (CorporateMission mission in fixtures)
                    {
                        Call(network, "DiscardPendingMissionGoods", mission);
                        foreach (Pawn target in mission.targets.Where(p => p != null && !p.Destroyed).ToList()) target.Destroy();
                        if (mission.quest?.State == QuestState.Ongoing) mission.quest.End(QuestEndOutcome.Unknown, false, false);
                        if (mission.site?.HasMap == true) Current.Game.DeinitAndRemoveMap(mission.site.Map, false);
                        if (mission.site?.Spawned == true) mission.site.Destroy();
                    }
                    check("Real generated site fixtures are removed after map initialization", fixtures.All(m => m.site == null || !m.site.Spawned));
                }
                catch (Exception exception) { check("Deferred site cleanup exception: " + exception, false); }
                finally
                {
                    foreach (string field in fields) Field(field).SetValue(network, saved[field]);
                    network.CorporateFaction.TryAffectGoodwillWith(Faction.OfPlayer, originalGoodwill - network.CorporateFaction.PlayerGoodwill, false, false);
                    missions.Clear();
                    missions.AddRange(previous);
                    context.Invalidate();
                }
            });
        }
    }

    private static FieldInfo Field(string name) => typeof(CorporateNetwork).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
    private static void Activate(DiaOption option) => typeof(DiaOption).GetMethod("Activate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(option, null);

    private static void Call(CorporateNetwork network, string method, params object[] args)
    {
        Invoke(network, method, args);
    }

    private static object Invoke(CorporateNetwork network, string method, params object[] args)
    {
        MethodInfo target = typeof(CorporateNetwork).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        if (target == null) throw new MissingMethodException(typeof(CorporateNetwork).FullName, method);
        return target.Invoke(network, args);
    }
}
