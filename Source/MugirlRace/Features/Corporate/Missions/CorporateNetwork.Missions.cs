using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Mugirl.Features.Incidents;
using RimWorld.Planet;
using RimWorld.QuestGen;

namespace Mugirl.Features.Corporate;

// 委托板：每周报价（加工、清剿、投资、分销、原版转介、核聚变支线）、接受、提交、领取与失败的生命周期。
public partial class CorporateNetwork
{
    private bool missionBoardRead;

    private const int MissionCheckIntervalTicks = 250;
    private const int FinishedMissionHistory = 36;

    public IReadOnlyList<CorporateMission> Missions => missionBoard.missions;
    internal static CorporateMissionConfigDef MissionConfig => CorporateDefOf.Mugirl_CorporateMissionConfig;

    public int ActiveMissionCount => missionBoard.ActiveCount;

    private void QuestsExposeData()
    {
        CorporateStateScribe.Look(missionBoard, "missionBoard", ref missionBoardRead);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            foreach (CorporateMission mission in missionBoard.missions)
            {
                RepairMissionQuestRoot(mission);
            }

            RefreshFusionInvitationAvailability();
            missionBoard.nextCheck = 0;
        }
    }

    private void RefreshFusionInvitationAvailability()
    {
        // 仅在加载时重新评估邀请。只有最近一份核聚变合同已经进入真实地图、
        // 却从未生成研究团队时才补发；正常过期、放弃以及生成后的玩法失败仍是最终结局。
        CorporateMission? latest = missionBoard.missions.Where(m => m.IsSide).OrderByDescending(m => m.id).FirstOrDefault();
        if (latest == null || latest.state != CorporateMissionState.Failed || latest.spawned || latest.site?.HasMap != true)
        {
            return;
        }

        missionBoard.fusionInvited = false;
        missionBoard.fusionDueTick = Now;
    }

    public void RememberFusionInvestor(Pawn? pawn)
    {
        if (pawn == null)
        {
            return;
        }

        missionBoard.retainedInvestor = pawn;
        missionBoard.retainedInvestorName = pawn.LabelShortCap;
    }

    public CorporateMission? FindMission(int id) => missionBoard.Find(id);

    private CorporateMission NewMission(CorporateMissionKind kind) => missionBoard.Create(kind, Now);

    // 已加载地图中的玩家家园，按 LoadedMaps 顺序。
    private static List<Map> PlayerHomeMaps()
    {
        var homes = new List<Map>();
        List<Map>? maps = GameUtility.LoadedMaps;
        if (maps != null)
        {
            foreach (Map map in maps)
            {
                if (map.IsPlayerHome)
                {
                    homes.Add(map);
                }
            }
        }

        return homes;
    }

    private static Map? FirstPlayerHomeMap() => GameUtility.LoadedMaps?.Find(m => m.IsPlayerHome);

    private void QuestsRefreshWeekly()
    {
        foreach (CorporateMission old in missionBoard.missions)
        {
            if (!old.IsSide && old.state == CorporateMissionState.Available)
            {
                old.state = CorporateMissionState.Expired;
            }
        }

        // 保留有限的可读历史；仍持有资产的合同与剧情合同不会被清理。
        List<CorporateMission> history = missionBoard.missions.Where(m => !m.IsSide && m.IsFinished && m.pendingGoods.Count == 0)
            .OrderByDescending(m => m.id).Skip(FinishedMissionHistory).ToList();
        foreach (CorporateMission old in history)
        {
            missionBoard.missions.Remove(old);
        }

        Map? map = FirstPlayerHomeMap();
        CorporateMission? processing = BuildProcessingOffer();
        if (processing != null)
        {
            missionBoard.missions.Add(processing);
        }

        CorporateMission? purge = BuildPurgeOffer(map);
        if (purge != null)
        {
            missionBoard.missions.Add(purge);
        }

        missionBoard.missions.Add(BuildInvestmentOffer(false));
        missionBoard.missions.Add(BuildInvestmentOffer(true));
        missionBoard.missions.Add(BuildDistributionOffer());
        CorporateMission? native = BuildNativeOffer(map);
        if (native != null)
        {
            missionBoard.missions.Add(native);
        }

        while (missionBoard.missions.Count(m => !m.IsSide && m.state == CorporateMissionState.Available) < MissionConfig.weeklyOffers)
        {
            missionBoard.missions.Add(BuildInvestmentOffer(false));
        }
    }

    private CorporateMission? BuildProcessingOffer()
    {
        CorporateMissionConfigDef config = MissionConfig;
        List<RecipeDef> recipes = PlayerHomeMaps()
            .SelectMany(m => m.listerBuildings.allBuildingsColonist).OfType<Building_WorkTable>()
            .SelectMany(b => b.def.AllRecipes).Distinct()
            .Where(r => r.AvailableNow && r.products != null && r.products.Count == 1 && r.ingredients != null
                && r.ingredients.Count > 0 && r.Worker.GetType() == typeof(RecipeWorker)
                && r.products[0].thingDef.category == ThingCategory.Item && r.products[0].count > 0).InRandomOrder().ToList();
        foreach (RecipeDef r in recipes)
        {
            ThingDef product = r.products[0].thingDef;
            CorporateMission m = NewMission(CorporateMissionKind.Processing);
            m.recipe = r;
            m.product = product;
            m.batches = Rand.RangeInclusive(config.batchMin, config.batchMax);
            m.duration = config.processingDays * DayTicks;
            m.count = m.batches * r.products[0].count;
            if (!TryAssignProcessingIngredients(m, r, product) || (product.MadeFromStuff && m.stuff == null))
            {
                continue;
            }

            m.cost = UnityMathf.CeilToInt(m.ingredients.Sum(x => x.thingDef.BaseMarketValue * x.count));
            if (m.cost < 1 || m.cost > config.processingMaxDeposit)
            {
                continue;
            }

            Thing sample = ThingMaker.MakeThing(product, m.stuff);
            try
            {
                // 使用配方实际工作量与材质修正；仅生成新报价时计算，不重算已签合同。
                m.reward = ProcessingReward(r, sample, m.batches,
                    Rand.Range(config.processingFeeMin, config.processingFeeMax));
            }
            finally
            {
                DiscardUnspawnedProduct(sample);
            }

            if (m.reward <= 0)
            {
                continue;
            }

            return m;
        }

        return null;
    }

    // 每个原料槽取允许范围内最便宜的普通物品；任一槽无可用原料或数量异常时整份报价作废。
    private static bool TryAssignProcessingIngredients(CorporateMission m, RecipeDef r, ThingDef product)
    {
        foreach (IngredientCount slot in r.ingredients)
        {
            ThingDef? ingredient = slot.filter.AllowedThingDefs.Where(d => d.category == ThingCategory.Item && !d.MadeFromStuff
                && d.BaseMarketValue > 0 && d != ThingDefOf.Silver && r.fixedIngredientFilter.Allows(d)
                && (m.stuff != null || !product.MadeFromStuff || !d.IsStuff || product.stuffCategories.Any(c => d.stuffProps.categories.Contains(c))))
                .OrderBy(d => d.BaseMarketValue).FirstOrDefault();
            if (ingredient == null)
            {
                return false;
            }

            int amount = slot.CountRequiredOfFor(ingredient, r) * m.batches;
            if (amount <= 0 || amount > 5000)
            {
                return false;
            }

            if (product.MadeFromStuff && ingredient.IsStuff && m.stuff == null)
            {
                m.stuff = ingredient;
            }

            ThingDefCountClass? existing = m.ingredients.Find(x => x.thingDef == ingredient);
            if (existing == null)
            {
                m.ingredients.Add(new ThingDefCountClass(ingredient, amount));
            }
            else
            {
                existing.count += amount;
            }
        }

        return true;
    }

    internal static int ProcessingReward(RecipeDef recipe, Thing sample, int batches, float marketFactor)
    {
        float work = recipe.WorkAmountTotal(sample);
        float marketValue = sample.MarketValue;
        CorporateMissionConfigDef config = MissionConfig;
        float hourlyFee = config.processingFeePerWorkHour;
        if (batches <= 0 || work < 0f || float.IsNaN(work) || float.IsInfinity(work)
            || marketValue <= 0f || float.IsNaN(marketValue) || float.IsInfinity(marketValue)
            || hourlyFee < 0f || float.IsNaN(hourlyFee) || float.IsInfinity(hourlyFee)
            || marketFactor < 0f || float.IsNaN(marketFactor) || float.IsInfinity(marketFactor))
        {
            return 0;
        }

        double marketFee = Math.Round((double)marketValue * recipe.products[0].count * batches * marketFactor);
        double workFee = Math.Ceiling((double)work * batches * hourlyFee / 2500d);
        double reward = Math.Max(config.processingMinimumFee, Math.Max(marketFee, workFee));
        // 沿用金融金额边界，拒绝异常配方，不截断成一个看似正常的合同。
        return reward > (double)CorporateMoney.MaxTotal ? 0 : (int)reward;
    }

    private CorporateMission? BuildPurgeOffer(Map? map)
    {
        Faction? corporation = CorporateFaction;
        if (map == null || corporation == null || GameUtility.Storyteller?.difficulty.allowViolentQuests != true)
        {
            return null;
        }

        Faction? player = PlayerFactionUtility.PlayerFaction;
        Faction? enemy = GameUtility.Factions?.AllFactionsListForReading.Where(f => !f.IsPlayer && !f.def.hidden && !f.defeated
            && f.HostileTo(player) && f.HostileTo(corporation)
            && f.def.pawnGroupMakers != null && f.def.pawnGroupMakers.Any(p => p.kindDef == PawnGroupKindDefOf.Combat)).RandomElementWithFallback();
        if (enemy == null || !TileFinder.TryFindNewSiteTile(out PlanetTile tile, map.Tile, 2, 10, allowCaravans: false))
        {
            return null;
        }

        CorporateMissionConfigDef config = MissionConfig;
        CorporateMission m = NewMission(CorporateMissionKind.Purge);
        m.enemy = enemy;
        m.tile = tile;
        m.duration = config.purgeDays * DayTicks;
        m.threatPoints = UnityMathf.Clamp(StorytellerUtility.DefaultThreatPointsNow(map) * 0.6f, 150f, 1600f);
        // UnityMathf.Clamp 在配置的下限大于上限时按顺序钳制，不抛异常。
        m.reward = UnityMathf.Clamp(UnityMathf.RoundToInt((m.threatPoints * 3f) + 700), config.purgeMinReward, config.purgeMaxReward);
        m.goodwill = config.purgeGoodwill;
        return m;
    }

    private CorporateMission BuildInvestmentOffer(bool high)
    {
        CorporateMissionConfigDef config = MissionConfig;
        CorporateMission m = NewMission(CorporateMissionKind.Investment);
        m.highRisk = high;
        m.cost = Rand.RangeInclusive((high ? config.highInvestmentMin : config.investmentMin) / 100,
            (high ? config.highInvestmentMax : config.investmentMax) / 100) * 100;
        m.duration = (high ? config.highInvestmentDays : config.investmentDays) * DayTicks;
        return m;
    }

    private CorporateMission BuildDistributionOffer()
    {
        CorporateMissionConfigDef config = MissionConfig;
        CorporateMission m = NewMission(CorporateMissionKind.Distribution);
        ThingDef product = CorporateMissionContent.DistributionProducts.RandomElement();
        m.product = product;
        m.count = UnityMathf.Clamp(UnityMathf.RoundToInt(Rand.Range(700f, 1800f) / product.BaseMarketValue), 10, 300);
        // 销售目标是“数量 × 配置比例”的精确向上取整（decimal），不受 float 乘积在整数边界上的误差影响。
        m.salesGoal = UnityMathf.Clamp(CorporateMoney.CeilingShare(m.count, config.salesTarget), 1, m.count);
        m.cost = UnityMathf.CeilToInt(product.BaseMarketValue * m.count * config.wholesaleFactor);
        m.duration = config.distributionDays * DayTicks;
        m.goodwill = config.distributionGoodwill;
        return m;
    }

    private CorporateMission? BuildNativeOffer(Map? map)
    {
        if (map == null)
        {
            return null;
        }

        foreach (QuestScriptDef root in CorporateMissionContent.NativeQuests.InRandomOrder())
        {
            Slate slate = MissionSlate(map);
            if (!root.CanRun(slate, map))
            {
                continue;
            }

            CorporateMission m = NewMission(CorporateMissionKind.Native);
            m.nativeRoot = root;
            return m;
        }

        return null;
    }

    private static Slate MissionSlate(Map? map)
    {
        Slate slate = new Slate();
        slate.Set("map", map);
        slate.Set("points", map != null ? UnityMathf.Max(200f, StorytellerUtility.DefaultThreatPointsNow(map)) : 200f);
        return slate;
    }

    public bool CanAcceptMission([NotNullWhen(true)] CorporateMission? m, [NotNullWhen(true)] CorporateTradeContext? context,
        [NotNullWhen(false)] out string? reason)
    {
        if (!CanTrade(context, out reason))
        {
            return false;
        }

        if (m == null || !missionBoard.missions.Contains(m) || m.state != CorporateMissionState.Available || Now >= m.acceptByTick)
        {
            reason = "Mugirl.CQ.OfferGone".Translate();
        }
        else if (!m.IsSide && ActiveMissionCount >= MissionConfig.activeLimit)
        {
            reason = "Mugirl.CQ.Limit".Translate(MissionConfig.activeLimit);
        }
        else if (context.SilverCount < m.cost)
        {
            reason = "Mugirl.CQ.NeedSilver".Translate(m.cost);
        }
        else if (m.kind == CorporateMissionKind.Processing && (m.recipe == null || !m.recipe.AvailableNow || !HomeHasWorkshopFor(m.recipe)))
        {
            reason = "Mugirl.CQ.NoWorkshop".Translate();
        }
        else if (m.kind == CorporateMissionKind.Native && (m.nativeRoot == null || ResolveMissionMap(context) == null))
        {
            reason = "Mugirl.CQ.NoMap".Translate();
        }

        if (string.IsNullOrEmpty(reason))
        {
            reason = null;
            return true;
        }

        return false;
    }

    // 界面每帧为选中的加工合同调用：直接遍历家园工作台，不分配 LINQ 迭代器。
    private static bool HomeHasWorkshopFor(RecipeDef recipe)
    {
        List<Map>? maps = GameUtility.LoadedMaps;
        if (maps == null)
        {
            return false;
        }

        foreach (Map map in maps)
        {
            if (!map.IsPlayerHome)
            {
                continue;
            }

            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i] is Building_WorkTable table && table.def.AllRecipes.Contains(recipe))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Map? ResolveMissionMap(CorporateTradeContext context) => context.Map ?? FirstPlayerHomeMap();

    public bool AcceptMission(CorporateMission? m, CorporateTradeContext? context, [NotNullWhen(false)] out string? reason)
    {
        if (!CanAcceptMission(m, context, out reason))
        {
            return false;
        }

        if (m.kind == CorporateMissionKind.Native)
        {
            return AcceptNativeMission(m, context, out reason);
        }

        // 站点在付款与生成任务之前创建；之后任一步失败都撤销本次接受，不留下无合同的世界站点。
        PlanetTile previousTile = m.tile;
        if ((m.kind == CorporateMissionKind.Purge || m.IsSide) && !TryCreateMissionSite(m, context, out reason))
        {
            m.tile = previousTile;
            return false;
        }

        DiscardPendingMissionGoods(m);
        try
        {
            if (m.kind == CorporateMissionKind.Processing)
            {
                foreach (ThingDefCountClass ingredient in m.ingredients)
                {
                    PrepareMissionGoods(m, ingredient.thingDef, ingredient.count);
                }
            }
            else if (m.kind == CorporateMissionKind.Distribution)
            {
                PrepareMissionGoods(m, m.product ?? throw new InvalidOperationException("Distribution contract has no product."), m.count);
            }
        }
        catch (Exception e)
        {
            DiscardPendingMissionGoods(m);
            RemoveMissionSite(m, previousTile);
            MugirlLog.WarningOnce("CorporateQuestGoods." + m.id, "Corporate contract goods could not be prepared: " + e.Message);
            reason = "Mugirl.CQ.GenerationFailed".Translate();
            return false;
        }

        if (m.cost > 0 && !context.TrySpendSilver(m.cost))
        {
            DiscardPendingMissionGoods(m);
            RemoveMissionSite(m, previousTile);
            reason = "Mugirl.CQ.NeedSilver".Translate(m.cost);
            return false;
        }

        int now = Now;
        int previousAcceptedTick = m.acceptedTick;
        int previousDeadline = m.deadline;
        int previousSalesGoal = m.salesGoal;
        m.acceptedTick = now;
        m.deadline = now + m.duration;
        m.state = CorporateMissionState.Active;

        if (m.kind == CorporateMissionKind.Investment)
        {
            float roll = Rand.Value;
            float multiplier = m.highRisk ? (roll < 0.45f ? 0.2f : roll < 0.8f ? 1.2f : 2.6f)
                : (roll < 0.3f ? 0.6f : roll < 0.75f ? 1.05f : 1.6f);
            m.reward = UnityMathf.RoundToInt(m.cost * multiplier);
        }

        if (m.site != null)
        {
            Slate slate = MissionSlate(ResolveMissionMap(context));
            slate.Set("corporateMissionId", m.id);
            Quest? quest = QuestUtility.GenerateQuestAndMakeAvailable(CorporateDefOf.Mugirl_CorporateMission, slate);
            if (quest == null)
            {
                // 原版 QuestGen 出错时返回 null（已记录错误）：退款并撤销，合同保持可接受。
                m.state = CorporateMissionState.Available;
                m.acceptedTick = previousAcceptedTick;
                m.deadline = previousDeadline;
                m.salesGoal = previousSalesGoal;
                if (m.cost > 0)
                {
                    context.DeliverSilver(m.cost);
                }

                DiscardPendingMissionGoods(m);
                RemoveMissionSite(m, previousTile);
                reason = "Mugirl.CQ.GenerationFailed".Translate();
                return false;
            }

            m.quest = quest;
            quest.name = m.Title;
            quest.description = m.Description;
            quest.Accept(context.AvailablePawns.FirstOrDefault(p => p.IsFreeColonist));
        }

        Record("Mugirl.CQ.AcceptedRecord", m.Title, -m.cost);
        ReceiveMissionGoods(m, context);
        return true;
    }

    private bool AcceptNativeMission(CorporateMission m, CorporateTradeContext context, [NotNullWhen(false)] out string? reason)
    {
        Map? map = ResolveMissionMap(context);
        Slate slate = MissionSlate(map);
        if (m.nativeRoot == null || !m.nativeRoot.CanRun(slate, map))
        {
            reason = "Mugirl.CQ.GenerationFailed".Translate();
            return false;
        }

        m.quest = QuestUtility.GenerateQuestAndMakeAvailable(m.nativeRoot, slate);
        if (m.quest == null)
        {
            reason = "Mugirl.CQ.GenerationFailed".Translate();
            return false;
        }

        m.state = CorporateMissionState.Active;
        m.acceptedTick = Now;
        QuestUtility.SendLetterQuestAvailable(m.quest);
        reason = null;
        return true;
    }

    private bool TryCreateMissionSite(CorporateMission m, CorporateTradeContext context, [NotNullWhen(false)] out string? reason)
    {
        Map? map = ResolveMissionMap(context);
        PlanetTile origin = context.Caravan?.Tile ?? (map != null ? map.Tile : PlanetTile.Invalid);
        PlanetTile tile = PlanetTile.Invalid;
        if (!origin.Valid || !(m.IsSide ? TryFindFusionResearchSiteTile(origin, out tile)
            : TileFinder.TryFindNewSiteTile(out tile, origin, 2, 10, allowCaravans: false)))
        {
            reason = "Mugirl.CQ.NoSite".Translate();
            return false;
        }

        if (!m.IsSide && (m.enemy == null || m.enemy.defeated || !m.enemy.HostileTo(PlayerFactionUtility.PlayerFaction)))
        {
            reason = "Mugirl.CQ.GenerationFailed".Translate();
            return false;
        }

        m.tile = tile;
        m.site = SiteMaker.MakeSite(m.IsSide ? CorporateDefOf.Mugirl_CorporateResearchSite : CorporateDefOf.Mugirl_CorporatePurgeSite,
            tile, m.IsSide ? null : m.enemy, false, m.IsSide ? 0f : m.threatPoints);
        if (m.site == null)
        {
            reason = "Mugirl.CQ.GenerationFailed".Translate();
            return false;
        }

        m.site.customLabel = m.Title;
        GameUtility.WorldObjects.Add(m.site);
        reason = null;
        return true;
    }

    // 撤销 AcceptMission 中途创建的站点；站点尚未生成地图，直接从世界移除。
    private static void RemoveMissionSite(CorporateMission m, PlanetTile previousTile)
    {
        if (m.site is { } site)
        {
            if (site.Spawned && !site.HasMap)
            {
                site.Destroy();
            }

            m.site = null;
            m.tile = previousTile;
        }
    }

    private void PrepareMissionGoods(CorporateMission m, ThingDef def, int count)
    {
        // stackLimit 不为正的异常 Def 按 1 件一堆处理，否则数量永不减少、循环不会结束。
        int stackLimit = Math.Max(1, def.stackLimit);
        while (count > 0)
        {
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = Math.Min(count, stackLimit);
            count -= thing.stackCount;
            if (!Vault.TryAdd(thing, false))
            {
                thing.Destroy();
                throw new InvalidOperationException("Corporate vault rejected prepared contract goods.");
            }

            m.pendingGoods.Add(thing);
        }
    }

    private static void DiscardPendingMissionGoods(CorporateMission m)
    {
        foreach (Thing thing in m.pendingGoods)
        {
            if (thing != null && !thing.Destroyed)
            {
                thing.Destroy();
            }
        }

        m.pendingGoods.Clear();
    }

    public bool ReceiveMissionGoods(CorporateMission? m, CorporateTradeContext? context)
    {
        if (context == null || !context.IsValid || m == null || !missionBoard.missions.Contains(m)
            || m.state == CorporateMissionState.Available || m.state == CorporateMissionState.Expired)
        {
            return false;
        }

        m.pendingGoods.RemoveAll(t => t == null || t.Destroyed);
        foreach (Thing thing in new List<Thing>(m.pendingGoods))
        {
            if (!context.Deliver(thing))
            {
                return false;
            }

            m.pendingGoods.Remove(thing);
        }

        return true;
    }

    private List<Thing> MissionSubmissionCandidates(CorporateMission? m, CorporateTradeContext? context)
    {
        if (context == null || !context.IsValid || m == null || !missionBoard.missions.Contains(m)
            || m.kind != CorporateMissionKind.Processing || m.state != CorporateMissionState.Active || Now >= m.deadline)
        {
            return new List<Thing>();
        }

        return context.AvailableContractThings.Where(t => t.def == m.product && (m.stuff == null || t.Stuff == m.stuff)
            && (!t.def.useHitPoints || t.HitPoints >= t.MaxHitPoints * 0.9f) && !t.IsNotFresh()
            && (t is not Apparel apparel || !apparel.WornByCorpse)
            && MissionProductQuality(t) >= QualityCategory.Normal)
            .OrderBy(MissionProductQuality).ThenBy(t => t.MarketValue).ToList();
    }

    private static QualityCategory MissionProductQuality(Thing thing) =>
        thing.TryGetQuality(out QualityCategory quality) ? quality : QualityCategory.Normal;

    public bool MissionSubmissionNeedsQualityConfirmation(CorporateMission? m, CorporateTradeContext? context)
    {
        int remaining = m == null ? 0 : m.count - m.progress;
        foreach (Thing thing in MissionSubmissionCandidates(m, context))
        {
            if (remaining <= 0)
            {
                break;
            }

            if (MissionProductQuality(thing) > QualityCategory.Normal)
            {
                return true;
            }

            remaining -= thing.stackCount;
        }

        return false;
    }

    public bool SubmitMissionProducts(CorporateMission m, CorporateTradeContext? context, bool allowHigherQuality = false)
    {
        List<Thing> candidates = MissionSubmissionCandidates(m, context);
        if (candidates.Count == 0 || context == null)
        {
            return false;
        }

        int before = m.progress;
        foreach (Thing t in candidates)
        {
            // 默认调用也保护高品质库存；只有明确确认后的这次提交可以使用。
            if (!allowHigherQuality && MissionProductQuality(t) > QualityCategory.Normal)
            {
                continue;
            }

            int count = Math.Min(m.count - m.progress, t.stackCount);
            if (count <= 0)
            {
                break;
            }

            if (!context.TryTakeContractThing(t, count, out Thing? taken))
            {
                continue;
            }

            m.progress += taken.stackCount;
            taken.Destroy();
        }

        if (m.progress >= m.count)
        {
            MissionReady(m);
        }

        return m.progress > before;
    }

    public bool ClaimMission(CorporateMission? m, CorporateTradeContext? context)
    {
        if (m == null || !missionBoard.missions.Contains(m) || m.state != CorporateMissionState.Ready || context == null || !context.IsValid)
        {
            return false;
        }

        // 结算前释放的俘虏不再满足合同。
        if ((m.kind == CorporateMissionKind.Purge || (m.IsSide && m.choice == CorporateResearchChoice.Execute)) && HasEscapedTarget(m))
        {
            m.state = CorporateMissionState.Active;
            return false;
        }

        int payment = m.reward + (m.kind == CorporateMissionKind.Processing ? m.cost : 0);
        if (payment > 0 && !context.DeliverSilver(payment))
        {
            return false;
        }

        m.state = CorporateMissionState.Completed;
        Record("Mugirl.CQ.ClaimedRecord", m.Title, payment);
        if (m.goodwill != 0)
        {
            CorporateFaction?.TryAffectGoodwillWith(PlayerFactionUtility.PlayerFaction, m.goodwill, reason: null);
        }

        EndMissionQuest(m, QuestEndOutcome.Success);
        return true;
    }

    // 目标引用失效，或仍活着却不是本殖民地的囚犯。
    private static bool HasEscapedTarget(CorporateMission m)
    {
        foreach (Pawn? target in m.targets)
        {
            if (target == null || (!target.Dead && !target.IsPrisonerOfColony))
            {
                return true;
            }
        }

        return false;
    }

    public bool AbandonMission(CorporateMission? m)
    {
        if (m == null || !missionBoard.missions.Contains(m) || m.state != CorporateMissionState.Active
            || m.kind == CorporateMissionKind.Investment || m.kind == CorporateMissionKind.Native)
        {
            return false;
        }

        FailMission(m, CorporateMissionState.Abandoned);
        return true;
    }

    private static void MissionReady(CorporateMission m)
    {
        if (m.state != CorporateMissionState.Active)
        {
            return;
        }

        m.state = CorporateMissionState.Ready;
        GameUtility.Letters.ReceiveLetter("Mugirl.CQ.ReadyLetter".Translate(), "Mugirl.CQ.ReadyText".Translate(m.Title), LetterDefOf.PositiveEvent);
    }

    private void FailMission(CorporateMission m, CorporateMissionState status = CorporateMissionState.Failed)
    {
        m.state = status;
        if (m.kind == CorporateMissionKind.Processing)
        {
            CorporateFaction?.TryAffectGoodwillWith(PlayerFactionUtility.PlayerFaction, MissionConfig.processingFailureGoodwill);
        }

        Record("Mugirl.CQ.FailedRecord", m.Title);
        EndMissionQuest(m, QuestEndOutcome.Fail);
        if (m.site != null && !m.site.HasMap && m.site.Spawned)
        {
            m.site.Destroy();
        }

        GameUtility.Letters.ReceiveLetter("Mugirl.CQ.FailedLetter".Translate(), "Mugirl.CQ.FailedText".Translate(m.Title), LetterDefOf.NegativeEvent);
    }

    private static void EndMissionQuest(CorporateMission m, QuestEndOutcome result)
    {
        RepairMissionQuestRoot(m);
        if (m.kind != CorporateMissionKind.Native && m.quest?.State == QuestState.Ongoing)
        {
            m.quest.End(result, false, false);
        }
    }

    private static void RepairMissionQuestRoot(CorporateMission m)
    {
        // 早期开发存档和夹具可能含有缺根任务，仅补入企业合同自己的根。
        // 原版转介仍保留其原始任务身份。
        if (m.kind != CorporateMissionKind.Native && m.quest != null && m.quest.root == null)
        {
            m.quest.root = CorporateDefOf.Mugirl_CorporateMission;
        }
    }

    public void NotifyDistributionSale(ThingDef? def, ThingDef? stuff, int count)
    {
        int now = Now;
        foreach (CorporateMission m in missionBoard.missions)
        {
            if (count <= 0)
            {
                break;
            }

            if (m.kind != CorporateMissionKind.Distribution || m.state != CorporateMissionState.Active || m.deadline <= now
                || m.product != def || m.stuff != stuff)
            {
                continue;
            }

            int take = Math.Min(count, m.SalesTarget - m.progress);
            m.progress += take;
            count -= take;
            if (m.progress >= m.SalesTarget)
            {
                MissionReady(m);
            }
        }
    }

    private void QuestsTick()
    {
        int now = Now;
        if (now < missionBoard.nextCheck)
        {
            return;
        }

        missionBoard.nextCheck = now + MissionCheckIntervalTicks;
        TickFusionInvitation();
        foreach (CorporateMission m in new List<CorporateMission>(missionBoard.missions))
        {
            // 谢礼独立于巨企交易权限自动送往家园；无家园时保留；已完成且无待发货物时不补发。
            if (m.IsSide && m.state == CorporateMissionState.Completed && m.choice == CorporateResearchChoice.Protect
                && !m.gratitudeRevoked && m.pendingGoods.Count > 0 && FirstPlayerHomeMap() is { } home)
            {
                ReceiveMissionGoods(m, new CorporateTradeContext(home));
            }

            if (m.state == CorporateMissionState.Available && now >= m.acceptByTick)
            {
                m.state = CorporateMissionState.Expired;
            }

            if (m.state == CorporateMissionState.Ready && (m.kind == CorporateMissionKind.Purge || m.IsSide) && HasEscapedTarget(m))
            {
                m.state = CorporateMissionState.Active;
            }

            if (m.state != CorporateMissionState.Active)
            {
                continue;
            }

            if (m.kind == CorporateMissionKind.Native)
            {
                TickNativeMission(m);
                continue;
            }

            if (m.kind == CorporateMissionKind.Investment)
            {
                if (now >= m.deadline)
                {
                    MissionReady(m);
                }

                continue;
            }

            if (now >= m.deadline)
            {
                FailMission(m);
                continue;
            }

            if (m.kind == CorporateMissionKind.Purge || m.IsSide)
            {
                TickMissionSite(m);
            }
        }
    }

    private void TickNativeMission(CorporateMission m)
    {
        if (m.quest == null)
        {
            FailMission(m);
        }
        else if (m.quest.State == QuestState.EndedSuccess)
        {
            m.state = CorporateMissionState.Completed;
        }
        else if (m.quest.State == QuestState.EndedFailed || m.quest.State == QuestState.EndedOfferExpired
            || m.quest.State == QuestState.EndedUnknownOutcome)
        {
            m.state = CorporateMissionState.Failed;
        }
    }

    private void TickFusionInvitation()
    {
        GameComponent_Storyline? story = GameUtility.GameComponent<GameComponent_Storyline>();
        if (!missionBoard.fusionQualified && story?.InvestmentAccepted == true && !story.InvestmentPending)
        {
            missionBoard.fusionQualified = true;
        }

        if (!missionBoard.fusionQualified || missionBoard.fusionInvited || !Unlocked)
        {
            return;
        }

        CorporateMissionConfigDef config = MissionConfig;
        int now = Now;
        if (missionBoard.fusionDueTick < 0)
        {
            missionBoard.fusionDueTick = now + Rand.RangeInclusive(config.fusionInviteMinDays * DayTicks, config.fusionInviteMaxDays * DayTicks);
        }

        if (now < missionBoard.fusionDueTick)
        {
            return;
        }

        CorporateMission m = NewMission(CorporateMissionKind.Fusion);
        m.acceptByTick = now + (config.fusionOfferDays * DayTicks);
        m.duration = config.fusionActionDays * DayTicks;
        m.reward = config.fusionReward;
        m.goodwill = config.fusionGoodwill;
        missionBoard.missions.Add(m);
        missionBoard.fusionInvited = true;
        GameUtility.Letters.ReceiveLetter("Mugirl.CQ.FusionInvitation".Translate(), m.Description, LetterDefOf.NeutralEvent);
    }
}
