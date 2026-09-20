using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public partial class CorporateNetwork
    {
        private List<CorporateMission> missions = new List<CorporateMission>();
        private int nextMissionId = 1;
        private int nextMissionCheck;
        private bool fusionQualificationSeen;
        private bool fusionInvitationCreated;
        private int fusionInvitationTick = -1;
        private Pawn retainedFusionInvestor;
        private string retainedFusionInvestorName;
        public IReadOnlyList<CorporateMission> Missions => missions;
        internal static CorporateQuestConfigDef MissionConfig => CorporateQuestDefOf.Mugirl_CorporateQuestConfig;
        public int ActiveMissionCount => missions.Count(m => !m.IsSide && (m.state == CorporateMissionState.Active || m.state == CorporateMissionState.Ready));

        partial void QuestsExposeData()
        {
            Scribe_Collections.Look(ref missions, "corporateMissions", LookMode.Deep);
            Scribe_Values.Look(ref nextMissionId, "nextMissionId", 1);
            Scribe_Values.Look(ref fusionQualificationSeen, "fusionQualificationSeen");
            Scribe_Values.Look(ref fusionInvitationCreated, "fusionInvitationCreated");
            Scribe_Values.Look(ref fusionInvitationTick, "fusionInvitationTick", -1);
            Scribe_References.Look(ref retainedFusionInvestor, "retainedFusionInvestor");
            Scribe_Values.Look(ref retainedFusionInvestorName, "retainedFusionInvestorName");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                missions = missions ?? new List<CorporateMission>();
                missions.RemoveAll(m => m == null);
                foreach (CorporateMission mission in missions) RepairMissionQuestRoot(mission);
                RepairFusionSiteGenerationLockout();
                nextMissionCheck = 0;
            }
        }

        private void RepairFusionSiteGenerationLockout()
        {
            // Older builds permanently consumed the one-time invitation when PostMapGenerate failed.
            // Only unlock a replacement when the latest fusion contract reached a real map but never
            // spawned its team; normal expiry, abandonment, and post-spawn gameplay failures stay final.
            CorporateMission latest = missions.Where(m => m.IsSide).OrderByDescending(m => m.id).FirstOrDefault();
            if (latest == null || latest.state != CorporateMissionState.Failed || latest.spawned || latest.site?.HasMap != true) return;
            fusionInvitationCreated = false;
            fusionInvitationTick = Now;
        }

        public void RememberFusionInvestor(Pawn pawn)
        {
            if (pawn == null) return;
            retainedFusionInvestor = pawn;
            retainedFusionInvestorName = pawn.LabelShortCap;
        }

        public CorporateMission FindMission(int id) => missions.FirstOrDefault(m => m.id == id);

        private CorporateMission NewMission(CorporateMissionKind kind)
        {
            return new CorporateMission { id = nextMissionId++, kind = kind, offeredTick = Now, acceptByTick = Now + WeekTicks };
        }

        partial void QuestsRefreshWeekly()
        {
            foreach (CorporateMission old in missions)
                if (!old.IsSide && old.state == CorporateMissionState.Available) old.state = CorporateMissionState.Expired;
            // Retain a bounded readable history; live assets and story contracts are never pruned.
            List<CorporateMission> history = missions.Where(m => !m.IsSide && m.IsFinished && m.pendingGoods.Count == 0).OrderByDescending(m => m.id).Skip(36).ToList();
            foreach (CorporateMission old in history) missions.Remove(old);
            Map map = MugirlGameUtility.LoadedMaps.FirstOrDefault(m => m.IsPlayerHome);
            CorporateMission processing = BuildProcessingOffer();
            if (processing != null) missions.Add(processing);
            CorporateMission purge = BuildPurgeOffer(map);
            if (purge != null) missions.Add(purge);
            missions.Add(BuildInvestmentOffer(false));
            missions.Add(BuildInvestmentOffer(true));
            if (CorporateQuestDefs.DistributionProducts.Length > 0) missions.Add(BuildDistributionOffer());
            CorporateMission native = BuildNativeOffer(map);
            if (native != null) missions.Add(native);
            while (missions.Count(m => !m.IsSide && m.state == CorporateMissionState.Available) < MissionConfig.weeklyOffers)
                missions.Add(BuildInvestmentOffer(false));
        }

        private CorporateMission BuildProcessingOffer()
        {
            List<RecipeDef> recipes = MugirlGameUtility.LoadedMaps.Where(m => m.IsPlayerHome)
                .SelectMany(m => m.listerBuildings.allBuildingsColonist).OfType<Building_WorkTable>()
                .SelectMany(b => b.def.AllRecipes).Distinct()
                .Where(r => r.AvailableNow && r.products != null && r.products.Count == 1 && r.ingredients != null
                    && r.ingredients.Count > 0 && r.Worker.GetType() == typeof(RecipeWorker)
                    && r.products[0].thingDef.category == ThingCategory.Item && r.products[0].count > 0).InRandomOrder().ToList();
            foreach (RecipeDef r in recipes)
            {
                CorporateMission m = NewMission(CorporateMissionKind.Processing);
                m.recipe = r;
                m.product = r.products[0].thingDef;
                m.batches = Rand.RangeInclusive(MissionConfig.batchMin, MissionConfig.batchMax);
                m.duration = MissionConfig.processingDays * DayTicks;
                m.count = m.batches * r.products[0].count;
                bool valid = true;
                foreach (IngredientCount slot in r.ingredients)
                {
                    ThingDef ingredient = slot.filter.AllowedThingDefs.Where(d => d.category == ThingCategory.Item && !d.MadeFromStuff
                        && d.BaseMarketValue > 0 && d != ThingDefOf.Silver && r.fixedIngredientFilter.Allows(d)
                        && (m.stuff != null || !m.product.MadeFromStuff || !d.IsStuff || m.product.stuffCategories.Any(c => d.stuffProps.categories.Contains(c))))
                        .OrderBy(d => d.BaseMarketValue).FirstOrDefault();
                    if (ingredient == null) { valid = false; break; }
                    int amount = slot.CountRequiredOfFor(ingredient, r) * m.batches;
                    if (amount <= 0 || amount > 5000) { valid = false; break; }
                    if (m.product.MadeFromStuff && ingredient.IsStuff && m.stuff == null) m.stuff = ingredient;
                    ThingDefCountClass existing = m.ingredients.FirstOrDefault(x => x.thingDef == ingredient);
                    if (existing == null) m.ingredients.Add(new ThingDefCountClass(ingredient, amount));
                    else existing.count += amount;
                }
                if (!valid || (m.product.MadeFromStuff && m.stuff == null)) continue;
                m.cost = Mathf.CeilToInt(m.ingredients.Sum(x => x.thingDef.BaseMarketValue * x.count));
                if (m.cost < 1 || m.cost > MissionConfig.processingMaxDeposit) continue;
                Thing sample = ThingMaker.MakeThing(m.product, m.stuff);
                m.reward = Mathf.Max(MissionConfig.processingMinimumFee, Mathf.RoundToInt(sample.MarketValue * m.count * Rand.Range(MissionConfig.processingFeeMin, MissionConfig.processingFeeMax)));
                sample.Destroy();
                return m;
            }
            return null;
        }

        private CorporateMission BuildPurgeOffer(Map map)
        {
            if (map == null || CorporateFaction == null || !MugirlGameUtility.Storyteller.difficulty.allowViolentQuests) return null;
            Faction faction = MugirlGameUtility.Factions.AllFactionsListForReading.Where(f => !f.IsPlayer && !f.def.hidden && !f.defeated
                && f.HostileTo(MugirlWildSlaveUtility.PlayerFaction) && f.HostileTo(CorporateFaction)
                && f.def.pawnGroupMakers != null && f.def.pawnGroupMakers.Any(p => p.kindDef == PawnGroupKindDefOf.Combat)).RandomElementWithFallback();
            if (faction == null || !TileFinder.TryFindNewSiteTile(out PlanetTile tile, map.Tile, 2, 10, allowCaravans: false)) return null;
            CorporateMission m = NewMission(CorporateMissionKind.Purge);
            m.enemy = faction;
            m.tile = tile;
            m.duration = MissionConfig.purgeDays * DayTicks;
            m.threatPoints = Mathf.Clamp(StorytellerUtility.DefaultThreatPointsNow(map) * 0.6f, 150f, 1600f);
            m.reward = Mathf.Clamp(Mathf.RoundToInt(m.threatPoints * 3f + 700), MissionConfig.purgeMinReward, MissionConfig.purgeMaxReward);
            m.goodwill = MissionConfig.purgeGoodwill;
            return m;
        }

        private CorporateMission BuildInvestmentOffer(bool high)
        {
            CorporateMission m = NewMission(CorporateMissionKind.Investment);
            m.highRisk = high;
            m.cost = Rand.RangeInclusive((high ? MissionConfig.highInvestmentMin : MissionConfig.investmentMin) / 100, (high ? MissionConfig.highInvestmentMax : MissionConfig.investmentMax) / 100) * 100;
            m.duration = (high ? MissionConfig.highInvestmentDays : MissionConfig.investmentDays) * DayTicks;
            return m;
        }

        private CorporateMission BuildDistributionOffer()
        {
            CorporateMission m = NewMission(CorporateMissionKind.Distribution);
            m.product = CorporateQuestDefs.DistributionProducts.RandomElement();
            m.count = Mathf.Clamp(Mathf.RoundToInt(Rand.Range(700f, 1800f) / m.product.BaseMarketValue), 10, 300);
            m.salesGoal = Mathf.Clamp(Mathf.CeilToInt(m.count * MissionConfig.salesTarget), 1, m.count);
            m.cost = Mathf.CeilToInt(m.product.BaseMarketValue * m.count * MissionConfig.wholesaleFactor);
            m.duration = MissionConfig.distributionDays * DayTicks;
            m.goodwill = MissionConfig.distributionGoodwill;
            return m;
        }

        private CorporateMission BuildNativeOffer(Map map)
        {
            if (map == null) return null;
            foreach (QuestScriptDef root in CorporateQuestDefs.NativeQuests.InRandomOrder())
            {
                Slate slate = MissionSlate(map);
                if (!root.CanRun(slate, map)) continue;
                CorporateMission m = NewMission(CorporateMissionKind.Native);
                m.nativeRoot = root;
                return m;
            }
            return null;
        }

        private static Slate MissionSlate(Map map)
        {
            Slate slate = new Slate();
            slate.Set("map", map);
            slate.Set("points", map != null ? Mathf.Max(200, StorytellerUtility.DefaultThreatPointsNow(map)) : 200f);
            return slate;
        }

        public bool CanAcceptMission(CorporateMission m, CorporateTradeContext context, out string reason)
        {
            if (!CanTrade(context, out reason)) return false;
            if (m == null || !missions.Contains(m) || m.state != CorporateMissionState.Available || Now >= m.acceptByTick)
                reason = "Mugirl.CQ.OfferGone".Translate();
            else if (!m.IsSide && ActiveMissionCount >= MissionConfig.activeLimit) reason = "Mugirl.CQ.Limit".Translate(MissionConfig.activeLimit);
            else if (context.SilverCount < m.cost) reason = "Mugirl.CQ.NeedSilver".Translate(m.cost);
            else if (m.kind == CorporateMissionKind.Processing && (m.recipe == null || !m.recipe.AvailableNow
                || !MugirlGameUtility.LoadedMaps.Where(x => x.IsPlayerHome).SelectMany(x => x.listerBuildings.allBuildingsColonist).OfType<Building_WorkTable>().Any(b => b.def.AllRecipes.Contains(m.recipe))))
                reason = "Mugirl.CQ.NoWorkshop".Translate();
            else if (m.kind == CorporateMissionKind.Native && (m.nativeRoot == null || ResolveMissionMap(context) == null))
                reason = "Mugirl.CQ.NoMap".Translate();
            return reason.NullOrEmpty();
        }

        private static Map ResolveMissionMap(CorporateTradeContext context) => context.Map ?? MugirlGameUtility.LoadedMaps.FirstOrDefault(m => m.IsPlayerHome);

        public bool AcceptMission(CorporateMission m, CorporateTradeContext context, out string reason)
        {
            if (!CanAcceptMission(m, context, out reason)) return false;
            if (m.kind == CorporateMissionKind.Native)
            {
                Map map = ResolveMissionMap(context);
                Slate slate = MissionSlate(map);
                if (!m.nativeRoot.CanRun(slate, map)) { reason = "Mugirl.CQ.GenerationFailed".Translate(); return false; }
                m.quest = QuestUtility.GenerateQuestAndMakeAvailable(m.nativeRoot, slate);
                if (m.quest == null) { reason = "Mugirl.CQ.GenerationFailed".Translate(); return false; }
                m.state = CorporateMissionState.Active;
                m.acceptedTick = Now;
                QuestUtility.SendLetterQuestAvailable(m.quest);
                return true;
            }
            if (m.kind == CorporateMissionKind.Purge || m.IsSide)
            {
                Map map = ResolveMissionMap(context);
                PlanetTile origin = context.Caravan?.Tile ?? (map != null ? map.Tile : PlanetTile.Invalid);
                if (!origin.Valid || !TileFinder.TryFindNewSiteTile(out PlanetTile tile, origin, 2, 10, allowCaravans: false))
                { reason = "Mugirl.CQ.NoSite".Translate(); return false; }
                if (!m.IsSide && (m.enemy == null || m.enemy.defeated || !m.enemy.HostileTo(MugirlWildSlaveUtility.PlayerFaction)))
                { reason = "Mugirl.CQ.GenerationFailed".Translate(); return false; }
                m.tile = tile;
                m.site = SiteMaker.MakeSite(m.IsSide ? CorporateQuestDefOf.Mugirl_CorporateResearchSite : CorporateQuestDefOf.Mugirl_CorporatePurgeSite,
                    tile, m.IsSide ? null : m.enemy, false, m.IsSide ? 0f : m.threatPoints);
                if (m.site == null) { reason = "Mugirl.CQ.GenerationFailed".Translate(); return false; }
                m.site.customLabel = m.Title;
                MugirlGameUtility.WorldObjects.Add(m.site);
            }
            DiscardPendingMissionGoods(m);
            try
            {
                if (m.kind == CorporateMissionKind.Processing)
                    foreach (ThingDefCountClass ingredient in m.ingredients) PrepareMissionGoods(m, ingredient.thingDef, ingredient.count);
                else if (m.kind == CorporateMissionKind.Distribution) PrepareMissionGoods(m, m.product, m.count);
            }
            catch (Exception e)
            {
                DiscardPendingMissionGoods(m);
                MugirlLog.WarningOnce("CorporateQuestGoods." + m.id, "Corporate contract goods could not be prepared: " + e.Message);
                reason = "Mugirl.CQ.GenerationFailed".Translate();
                return false;
            }
            if (m.cost > 0 && !context.TrySpendSilver(m.cost))
            {
                DiscardPendingMissionGoods(m);
                reason = "Mugirl.CQ.NeedSilver".Translate(m.cost);
                return false;
            }
            m.acceptedTick = Now;
            m.deadline = Now + m.duration;
            m.state = CorporateMissionState.Active;
            if (m.kind == CorporateMissionKind.Distribution && m.salesGoal <= 0)
                m.salesGoal = m.SalesTarget;
            if (m.kind == CorporateMissionKind.Investment)
            {
                float roll = Rand.Value;
                float multiplier = m.highRisk ? (roll < 0.45f ? 0.2f : roll < 0.8f ? 1.2f : 2.6f)
                    : (roll < 0.3f ? 0.6f : roll < 0.75f ? 1.05f : 1.6f);
                m.reward = Mathf.RoundToInt(m.cost * multiplier);
            }
            if (m.site != null)
            {
                Slate slate = MissionSlate(ResolveMissionMap(context));
                slate.Set("corporateMissionId", m.id);
                m.quest = QuestUtility.GenerateQuestAndMakeAvailable(CorporateQuestDefOf.Mugirl_CorporateMission, slate);
                m.quest.name = m.Title;
                m.quest.description = m.Description;
                m.quest.Accept(context.AvailablePawns.FirstOrDefault(p => p.IsFreeColonist));
            }
            Record("Mugirl.CQ.AcceptedRecord", m.Title, -m.cost);
            ReceiveMissionGoods(m, context);
            return true;
        }

        private void PrepareMissionGoods(CorporateMission m, ThingDef def, int count)
        {
            while (count > 0)
            {
                Thing thing = ThingMaker.MakeThing(def);
                thing.stackCount = Mathf.Min(count, def.stackLimit);
                count -= thing.stackCount;
                if (!Vault.TryAdd(thing, false))
                {
                    thing.Destroy();
                    throw new InvalidOperationException("Corporate vault rejected prepared contract goods.");
                }
                m.pendingGoods.Add(thing);
            }
        }

        private void DiscardPendingMissionGoods(CorporateMission m)
        {
            foreach (Thing thing in m.pendingGoods.ToList()) if (thing != null && !thing.Destroyed) thing.Destroy();
            m.pendingGoods.Clear();
        }

        public bool ReceiveMissionGoods(CorporateMission m, CorporateTradeContext context)
        {
            if (context == null || !context.IsValid || m == null || !missions.Contains(m) || m.state == CorporateMissionState.Available || m.state == CorporateMissionState.Expired) return false;
            foreach (Thing thing in m.pendingGoods.ToList())
            {
                if (thing == null || thing.Destroyed) { m.pendingGoods.Remove(thing); continue; }
                if (!context.Deliver(thing)) return false;
                m.pendingGoods.Remove(thing);
            }
            return true;
        }

        public bool SubmitMissionProducts(CorporateMission m, CorporateTradeContext context)
        {
            if (context == null || !context.IsValid || m == null || !missions.Contains(m)
                || m.kind != CorporateMissionKind.Processing || m.state != CorporateMissionState.Active || Now >= m.deadline) return false;
            int before = m.progress;
            foreach (Thing t in context.AvailableContractThings.Where(t => t.def == m.product && (m.stuff == null || t.Stuff == m.stuff)
                && (!t.def.useHitPoints || t.HitPoints >= t.MaxHitPoints * 0.9f) && !t.IsNotFresh()
                && (!(t is Apparel apparel) || !apparel.WornByCorpse)
                && (!t.TryGetQuality(out QualityCategory quality) || quality >= QualityCategory.Normal)).ToList())
            {
                int count = Mathf.Min(m.count - m.progress, t.stackCount);
                if (count <= 0) break;
                if (!context.TryTakeContractThing(t, count, out Thing taken)) continue;
                m.progress += taken.stackCount;
                taken.Destroy();
            }
            if (m.progress >= m.count) MissionReady(m);
            return m.progress > before;
        }

        public bool ClaimMission(CorporateMission m, CorporateTradeContext context)
        {
            if (m == null || !missions.Contains(m) || m.state != CorporateMissionState.Ready || context == null || !context.IsValid) return false;
            if (m.kind == CorporateMissionKind.Purge || (m.IsSide && m.choice == CorporateResearchChoice.Execute))
            {
                // A captured target released before settlement no longer satisfies the contract.
                if (m.targets.Any(p => p == null || (!p.Dead && !p.IsPrisonerOfColony)))
                {
                    m.state = CorporateMissionState.Active;
                    return false;
                }
            }
            int payment = m.reward + (m.kind == CorporateMissionKind.Processing ? m.cost : 0);
            if (payment > 0 && !context.DeliverSilver(payment)) return false;
            m.state = CorporateMissionState.Completed;
            Record("Mugirl.CQ.ClaimedRecord", m.Title, payment);
            if (m.goodwill != 0) CorporateFaction?.TryAffectGoodwillWith(MugirlWildSlaveUtility.PlayerFaction, m.goodwill, reason: null);
            EndMissionQuest(m, QuestEndOutcome.Success);
            return true;
        }

        public bool AbandonMission(CorporateMission m)
        {
            if (m == null || !missions.Contains(m) || m.state != CorporateMissionState.Active || m.kind == CorporateMissionKind.Investment || m.kind == CorporateMissionKind.Native) return false;
            FailMission(m, CorporateMissionState.Abandoned);
            return true;
        }

        private void MissionReady(CorporateMission m)
        {
            if (m.state != CorporateMissionState.Active) return;
            m.state = CorporateMissionState.Ready;
            MugirlGameUtility.Letters.ReceiveLetter("Mugirl.CQ.ReadyLetter".Translate(), "Mugirl.CQ.ReadyText".Translate(m.Title), LetterDefOf.PositiveEvent);
        }

        private void FailMission(CorporateMission m, CorporateMissionState status = CorporateMissionState.Failed)
        {
            m.state = status;
            if (m.kind == CorporateMissionKind.Processing) CorporateFaction?.TryAffectGoodwillWith(MugirlWildSlaveUtility.PlayerFaction, MissionConfig.processingFailureGoodwill);
            Record("Mugirl.CQ.FailedRecord", m.Title);
            EndMissionQuest(m, QuestEndOutcome.Fail);
            if (m.site != null && !m.site.HasMap && m.site.Spawned) m.site.Destroy();
            MugirlGameUtility.Letters.ReceiveLetter("Mugirl.CQ.FailedLetter".Translate(), "Mugirl.CQ.FailedText".Translate(m.Title), LetterDefOf.NegativeEvent);
        }

        private static void EndMissionQuest(CorporateMission m, QuestEndOutcome result)
        {
            RepairMissionQuestRoot(m);
            if (m.kind != CorporateMissionKind.Native && m.quest?.State == QuestState.Ongoing) m.quest.End(result, false, false);
        }

        private static void RepairMissionQuestRoot(CorporateMission m)
        {
            // 早期开发存档和夹具可能含有缺根任务，仅补入企业合同自己的根。
            // 原版转介仍保留其原始任务身份。
            if (m.kind != CorporateMissionKind.Native && m.quest != null && m.quest.root == null)
                m.quest.root = CorporateQuestDefOf.Mugirl_CorporateMission;
        }

        public void NotifyDistributionSale(ThingDef def, ThingDef stuff, int count)
        {
            foreach (CorporateMission m in missions)
            {
                if (count <= 0) break;
                if (m.kind != CorporateMissionKind.Distribution || m.state != CorporateMissionState.Active || m.deadline <= Now
                    || m.product != def || m.stuff != stuff) continue;
                int take = Mathf.Min(count, m.SalesTarget - m.progress);
                m.progress += take;
                count -= take;
                if (m.progress >= m.SalesTarget) MissionReady(m);
            }
        }

        partial void QuestsTick()
        {
            if (Now < nextMissionCheck) return;
            nextMissionCheck = Now + 250;
            TickFusionInvitation();
            foreach (CorporateMission m in missions.ToList())
            {
                if (m.state == CorporateMissionState.Available && Now >= m.acceptByTick) m.state = CorporateMissionState.Expired;
                if (m.state == CorporateMissionState.Ready && (m.kind == CorporateMissionKind.Purge || m.IsSide))
                {
                    if (m.targets.Any(p => p == null || (!p.Dead && !p.IsPrisonerOfColony))) m.state = CorporateMissionState.Active;
                }
                if (m.state != CorporateMissionState.Active) continue;
                if (m.kind == CorporateMissionKind.Native)
                {
                    if (m.quest == null) FailMission(m);
                    else if (m.quest.State == QuestState.EndedSuccess) m.state = CorporateMissionState.Completed;
                    else if (m.quest.State == QuestState.EndedFailed || m.quest.State == QuestState.EndedOfferExpired || m.quest.State == QuestState.EndedUnknownOutcome) m.state = CorporateMissionState.Failed;
                    continue;
                }
                if (m.kind == CorporateMissionKind.Investment)
                {
                    if (Now >= m.deadline) MissionReady(m);
                    continue;
                }
                if (Now >= m.deadline) { FailMission(m); continue; }
                if (m.kind == CorporateMissionKind.Purge || m.IsSide) TickMissionSite(m);
            }
        }

        private void TickFusionInvitation()
        {
            MugirlStoryState story = MugirlGameUtility.GameComponent<MugirlStoryState>();
            if (!fusionQualificationSeen && story?.fusionInvestmentAccepted == true && !story.fusionInvestmentPending) fusionQualificationSeen = true;
            if (!fusionQualificationSeen || fusionInvitationCreated || !Unlocked) return;
            if (fusionInvitationTick < 0) fusionInvitationTick = Now + Rand.RangeInclusive(MissionConfig.fusionInviteMinDays * DayTicks, MissionConfig.fusionInviteMaxDays * DayTicks);
            if (Now < fusionInvitationTick) return;
            CorporateMission m = NewMission(CorporateMissionKind.Fusion);
            m.acceptByTick = Now + MissionConfig.fusionOfferDays * DayTicks;
            m.duration = MissionConfig.fusionActionDays * DayTicks;
            m.reward = MissionConfig.fusionReward;
            m.goodwill = MissionConfig.fusionGoodwill;
            missions.Add(m);
            fusionInvitationCreated = true;
            MugirlGameUtility.Letters.ReceiveLetter("Mugirl.CQ.FusionInvitation".Translate(), m.Description, LetterDefOf.NeutralEvent);
        }
    }
}
