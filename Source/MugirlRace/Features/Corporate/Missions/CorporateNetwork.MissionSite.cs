using System.Linq;
using RimWorld.Planet;
using Verse.AI.Group;

namespace Mugirl.Features.Corporate;

// 清剿与核聚变研究站的现场：生成目标、低频检查进度、研究员对白、保护谢礼与执行分支。
public partial class CorporateNetwork
{
    internal void SpawnMissionSite(Map map)
    {
        CorporateMission? m = missionBoard.missions.Find(x => x.site == map.Parent && x.state == CorporateMissionState.Active);
        if (m == null || m.spawned)
        {
            return;
        }

        IntVec3 center = map.Center;
        try
        {
            if (m.IsSide)
            {
                CorporateFusionSiteBuilder.Spawn(map, center);
                center = CellFinder.RandomClosewalkCellNear(center, map, 12);
                SpawnResearchTeam(m, map);
            }
            else
            {
                PawnGroupMakerParms parms = new PawnGroupMakerParms
                {
                    faction = m.enemy,
                    groupKind = PawnGroupKindDefOf.Combat,
                    points = m.threatPoints,
                    tile = map.Tile,
                    generateFightersOnly = true,
                };
                m.targets.AddRange(PawnGroupMakerUtility.GeneratePawns(parms));
            }

            m.targets.RemoveAll(p => p == null);
            if (m.targets.Count == 0)
            {
                FailMission(m);
                return;
            }

            var spawnedTargets = new List<Pawn>(m.targets.Count);
            foreach (Pawn? pawn in m.targets)
            {
                if (pawn != null)
                {
                    GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(center, map, 9), map);
                    spawnedTargets.Add(pawn);
                }
            }

            m.count = m.targets.Count;
            m.spawned = true;
            LordMaker.MakeNewLord(m.IsSide ? null : m.enemy, new LordJob_DefendPoint(center), map, spawnedTargets);
        }
        catch (Exception e)
        {
            string mapContext = "tile=" + map.Tile + ", biome=" + (map.Biome?.defName ?? "null")
                + ", size=" + map.Size + ", parent=" + (map.Parent?.def?.defName ?? "null");
            MugirlLog.WarningOnce("CorporateQuestSite." + m.id,
                "Corporate mission site generation failed (mission=" + m.id + ", " + mapContext + "): " + e);
            foreach (Pawn? pawn in new List<Pawn?>(m.targets))
            {
                if (pawn != null && !pawn.Spawned)
                {
                    GeneratedPawnUtility.TryPassToWorld(pawn);
                }
            }

            FailMission(m);
        }
    }

    // 研究员（原投资人若仍在世界中则由其本人出面）与三名雇佣守卫，均为无派系。
    private void SpawnResearchTeam(CorporateMission m, Map map)
    {
        Pawn? original = missionBoard.retainedInvestor;
        Pawn investor;
        if (original != null && !original.Destroyed && !original.Dead && !original.Spawned && original.IsWorldPawn()
            && !original.IsColonist && !original.IsPrisoner && !QuestUtility.IsReservedByQuestOrQuestBeingGenerated(original))
        {
            GameUtility.WorldPawns.RemovePawn(original);
            m.originalInvestor = true;
            investor = original;
        }
        else
        {
            investor = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, null,
                PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true, developmentalStages: DevelopmentalStage.Adult));
        }

        m.investor = investor;
        m.targets.Add(investor);
        if (investor.Faction != null)
        {
            investor.SetFaction(null);
        }

        for (int i = 0; i < 3; i++)
        {
            m.targets.Add(PawnGenerator.GeneratePawn(new PawnGenerationRequest(CorporateMissionContent.Guard, null,
                PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true, developmentalStages: DevelopmentalStage.Adult)));
        }
    }

    internal static void DevSpawnFusionResearchSite(Map map)
    {
        if (map == null)
        {
            throw new ArgumentNullException(nameof(map));
        }

        CorporateFusionSiteBuilder.Spawn(map, map.Center);
    }

    internal static bool TryFindFusionResearchSiteTile(PlanetTile origin, out PlanetTile tile)
    {
        // 保持原有 2–10 格距离，优先避开山地、水域、冻土和特殊地貌。
        // 没有优选地块时仍接受普通可到达站点，局部整地负责兜底。
        return TileFinder.TryFindNewSiteTile(out tile, origin, 2, 10, allowCaravans: false,
                   selectLandmarkChance: 0f, validator: IsPreferredFusionResearchTile)
            || TileFinder.TryFindNewSiteTile(out tile, origin, 2, 10, allowCaravans: false, selectLandmarkChance: 0f);
    }

    internal static bool IsPreferredFusionResearchTile(PlanetTile candidate)
    {
        Tile tile = GameUtility.WorldGrid[candidate];
        return tile is SurfaceTile surface && !tile.WaterCovered && !tile.IsCoastal
            && (tile.hilliness == Hilliness.Flat || tile.hilliness == Hilliness.SmallHills)
            && tile.temperature > 0f && tile.swampiness < 0.25f
            && surface.Rivers.NullOrEmpty() && tile.Mutators.Count == 0;
    }

    private void TickMissionSite(CorporateMission m)
    {
        if (m.site == null || !m.site.Spawned)
        {
            FailMission(m);
            return;
        }

        if (!m.spawned)
        {
            return;
        }

        if (m.targets.Contains(null))
        {
            FailMission(m);
            return;
        }

        m.RecordDeadTargets();
        if (m.IsSide && m.choice == CorporateResearchChoice.Undecided)
        {
            // 玩家主动攻击会在伤害前明确转入执行；其他原因致死只能中断现场。
            if (m.investor == null || m.investor.Dead || m.progress > 0)
            {
                FailMission(m);
                return;
            }

            if (!m.dialogueShown && m.site.HasMap && m.site.Map.mapPawns.FreeColonistsSpawned.Any())
            {
                m.dialogueShown = true;
                ShowResearchDialogue(m);
            }

            return;
        }

        if (m.IsSide && m.choice == CorporateResearchChoice.Protect)
        {
            return;
        }

        int prisoners = 0;
        foreach (Pawn? target in m.targets)
        {
            if (target == null)
            {
                continue;
            }

            if (!target.Spawned && !target.IsPrisonerOfColony)
            {
                FailMission(m);
                return;
            }

            if (target.IsPrisonerOfColony)
            {
                prisoners++;
            }
        }

        if (m.progress + prisoners >= m.count)
        {
            MissionReady(m);
        }
    }

    public void ShowResearchDialogue(CorporateMission? m)
    {
        if (m == null || !m.IsSide || m.state != CorporateMissionState.Active || m.choice != CorporateResearchChoice.Undecided
            || m.site?.HasMap != true || !m.site.Map.mapPawns.FreeColonistsSpawned.Any())
        {
            return;
        }

        string identity = m.originalInvestor ? "Mugirl.CQ.ResearchOriginal".Translate()
            : "Mugirl.CQ.ResearchRepresentative".Translate(missionBoard.retainedInvestorName ?? "Mugirl.CQ.OriginalTeam".Translate());
        DiaNode root = new DiaNode(identity + "\n\n" + "Mugirl.CQ.ResearchDialogue".Translate());
        DiaNode more = new DiaNode("Mugirl.CQ.ResearchMore".Translate());
        more.options.Add(new DiaOption("Mugirl.CQ.Back".Translate()) { link = root });
        root.options.Add(new DiaOption("Mugirl.CQ.AskResearch".Translate()) { link = more });
        DiaNode execute = new DiaNode("Mugirl.CQ.ExecuteConfirm".Translate());
        execute.options.Add(new DiaOption("Mugirl.CQ.Execute".Translate()) { action = () => BeginResearchCombat(m), resolveTree = true });
        execute.options.Add(new DiaOption("Mugirl.CQ.Back".Translate()) { link = root });
        root.options.Add(new DiaOption("Mugirl.CQ.Execute".Translate()) { link = execute });
        Faction? corporation = CorporateFaction;
        Faction? player = PlayerFactionUtility.PlayerFaction;
        int goodwill = corporation?.PlayerGoodwill ?? 0;
        int basePenalty = MissionConfig.fusionRefusalGoodwill;
        int adjustedPenalty = corporation?.CalculateAdjustedGoodwillChange(player, basePenalty) ?? basePenalty;
        int projectedBase = UnityMathf.Clamp((corporation?.BaseGoodwillWith(player) ?? goodwill) + adjustedPenalty, -100, 100);
        // 负向变更保留原版自然好感加成及情势上限，显示实际预计损失。
        int projectedGoodwill = Math.Min(goodwill, projectedBase);
        DiaNode protect = new DiaNode("Mugirl.CQ.ProtectConfirm".Translate(basePenalty, projectedGoodwill - goodwill, goodwill, projectedGoodwill,
            FusionProtectionRewardDescription));
        protect.options.Add(new DiaOption("Mugirl.CQ.Protect".Translate()) { action = () => ProtectResearchers(m), resolveTree = true });
        protect.options.Add(new DiaOption("Mugirl.CQ.Back".Translate()) { link = root });
        root.options.Add(new DiaOption("Mugirl.CQ.Protect".Translate()) { link = protect });
        root.options.Add(new DiaOption("Mugirl.CQ.Later".Translate()) { resolveTree = true });
        GameUtility.TryAddWindow(new Dialog_NodeTree(root, true, true, "Mugirl.CQ.ResearchTitle".Translate()));
    }

    private static void BeginResearchCombat(CorporateMission m)
    {
        if (m.state != CorporateMissionState.Active || m.choice != CorporateResearchChoice.Undecided)
        {
            return;
        }

        m.choice = CorporateResearchChoice.Execute;
        var active = new List<Pawn>();
        foreach (Pawn? pawn in m.targets)
        {
            if (pawn != null && pawn.Spawned && !pawn.Dead && !pawn.IsPrisonerOfColony)
            {
                active.Add(pawn);
            }
        }

        foreach (Pawn pawn in active)
        {
            pawn.GetLord()?.RemovePawn(pawn);
            pawn.SetFaction(Faction.OfAncientsHostile);
        }

        if (active.Count > 0)
        {
            LordMaker.MakeNewLord(Faction.OfAncientsHostile, new LordJob_DefendPoint(active[0].Position), active[0].Map, active);
        }

        GameUtility.Letters.ReceiveLetter("Mugirl.CQ.CombatLetter".Translate(), "Mugirl.CQ.CombatText".Translate(), LetterDefOf.ThreatSmall, m.site);
    }

    internal static string FusionProtectionRewardDescription => string.Join(", ", MissionConfig.fusionProtectionRewards
        .Select(reward => reward.count + " × " + reward.thingDef.LabelCap));

    private void ProtectResearchers(CorporateMission m)
    {
        if (!missionBoard.missions.Contains(m) || !m.IsSide || m.state != CorporateMissionState.Active
            || m.choice != CorporateResearchChoice.Undecided)
        {
            return;
        }

        try
        {
            // 复用 Vault 和 pendingGoods 的存档所有权；准备成功后才锁定结局，重进对白不会重发。
            foreach (ThingDefCountClass reward in MissionConfig.fusionProtectionRewards)
            {
                PrepareMissionGoods(m, reward.thingDef, reward.count);
            }
        }
        catch (Exception e)
        {
            DiscardPendingMissionGoods(m);
            MugirlLog.WarningOnce("CorporateFusionGift." + m.id, "Mugirl.CQ.ThanksPreparationFailed".Translate(e.Message));
            Messages.Message("Mugirl.CQ.DeliveryFailed".Translate(), MessageTypeDefOf.RejectInput, false);
            return;
        }

        m.choice = CorporateResearchChoice.Protect;
        m.reward = 0;
        m.goodwill = 0;
        m.state = CorporateMissionState.Completed;
        CorporateFaction?.TryAffectGoodwillWith(PlayerFactionUtility.PlayerFaction, MissionConfig.fusionRefusalGoodwill);
        Record("Mugirl.CQ.ProtectedRecord", m.Title);
        EndMissionQuest(m, QuestEndOutcome.Success);
        GameUtility.Letters.ReceiveLetter("Mugirl.CQ.ThanksTitle".Translate(),
            "Mugirl.CQ.ThanksText".Translate(FusionProtectionRewardDescription), LetterDefOf.PositiveEvent, m.site);
    }

    // 玩家造成伤害前调用（Thing.TakeDamage 前缀，战斗中每次命中都会经过）：只做一次无分配的线性查找。
    internal void ResearcherAttacked(Pawn pawn)
    {
        CorporateMission? m = null;
        foreach (CorporateMission mission in missionBoard.missions)
        {
            if (mission.IsSide && mission.spawned && mission.targets.Contains(pawn))
            {
                m = mission;
                break;
            }
        }

        if (m == null)
        {
            return;
        }

        if (m.choice == CorporateResearchChoice.Undecided)
        {
            BeginResearchCombat(m);
        }
        else if (m.choice == CorporateResearchChoice.Protect && !m.gratitudeRevoked)
        {
            m.gratitudeRevoked = true;
            DiscardPendingMissionGoods(m);
            GameUtility.Letters.ReceiveLetter("Mugirl.CQ.ThanksRevokedTitle".Translate(), "Mugirl.CQ.ThanksRevokedText".Translate(),
                LetterDefOf.NegativeEvent, m.site);
        }
    }
}
