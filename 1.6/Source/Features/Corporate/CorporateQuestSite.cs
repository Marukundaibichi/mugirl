using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Mugirl
{
    public class QuestNode_Root_CorporateMission : QuestNode
    {
        protected override bool TestRunInt(Slate slate) => CorporateNetwork.Current?.FindMission(slate.Get<int>("corporateMissionId")) != null;
        protected override void RunInt()
        {
            QuestGen.quest.AddPart(new QuestPart_CorporateMission { missionId = QuestGen.slate.Get<int>("corporateMissionId") });
        }
    }

    public class QuestPart_CorporateMission : QuestPart
    {
        public int missionId;
        public override string DescriptionPart => CorporateNetwork.Current?.FindMission(missionId)?.Description ?? "";
        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                CorporateMission m = CorporateNetwork.Current?.FindMission(missionId);
                if (m?.site != null && m.site.Spawned) yield return m.site;
            }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref missionId, "missionId");
        }
    }

    public class SitePartWorker_Corporate : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            CorporateNetwork.Current?.SpawnMissionSite(map);
        }
    }

    public partial class CorporateNetwork
    {
        internal void SpawnMissionSite(Map map)
        {
            CorporateMission m = missions.FirstOrDefault(x => x.site == map.Parent && x.state == CorporateMissionState.Active);
            if (m == null || m.spawned) return;
            IntVec3 center = map.Center;
            try
            {
                if (m.IsSide)
                {
                    SpawnFusionResearchSite(map, center);
                    center = CellFinder.RandomClosewalkCellNear(center, map, 12);
                    Pawn original = retainedFusionInvestor;
                    if (original != null && !original.Destroyed && !original.Dead && !original.Spawned && original.IsWorldPawn()
                        && !original.IsColonist && !original.IsPrisoner && !QuestUtility.IsReservedByQuestOrQuestBeingGenerated(original))
                    {
                        MugirlGameUtility.WorldPawns.RemovePawn(original);
                        m.originalInvestor = true;
                        m.investor = original;
                    }
                    else m.investor = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, null,
                        PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true, developmentalStages: DevelopmentalStage.Adult));
                    m.targets.Add(m.investor);
                    if (m.investor.Faction != null) m.investor.SetFaction(null);
                    for (int i = 0; i < 3; i++)
                        m.targets.Add(PawnGenerator.GeneratePawn(new PawnGenerationRequest(CorporateQuestDefs.Guard, null,
                            PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true, developmentalStages: DevelopmentalStage.Adult)));
                }
                else
                {
                    PawnGroupMakerParms parms = new PawnGroupMakerParms
                    {
                        faction = m.enemy, groupKind = PawnGroupKindDefOf.Combat,
                        points = m.threatPoints, tile = map.Tile, generateFightersOnly = true
                    };
                    m.targets.AddRange(PawnGroupMakerUtility.GeneratePawns(parms));
                }
                m.targets.RemoveAll(p => p == null);
                if (m.targets.Count == 0) { FailMission(m); return; }
                foreach (Pawn pawn in m.targets)
                    GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(center, map, 9), map);
                m.count = m.targets.Count;
                m.spawned = true;
                LordMaker.MakeNewLord(m.IsSide ? null : m.enemy, new LordJob_DefendPoint(center), map, m.targets);
            }
            catch (Exception e)
            {
                string mapContext = map == null
                    ? "map=null"
                    : "tile=" + map.Tile + ", biome=" + (map.Biome?.defName ?? "null")
                        + ", size=" + map.Size + ", parent=" + (map.Parent?.def?.defName ?? "null");
                MugirlLog.WarningOnce("CorporateQuestSite." + m.id,
                    "Corporate mission site generation failed (mission=" + m.id + ", " + mapContext + "): " + e);
                foreach (Pawn pawn in m.targets.Where(p => p != null && !p.Spawned).ToList()) MugirlGeneratedPawnUtility.TryPassToWorld(pawn);
                FailMission(m);
            }
        }

        internal static void DevSpawnFusionResearchSite(Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            SpawnFusionResearchSite(map, map.Center);
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
            Tile tile = MugirlGameUtility.WorldGrid[candidate];
            return tile is SurfaceTile surface && !tile.WaterCovered && !tile.IsCoastal
                && (tile.hilliness == Hilliness.Flat || tile.hilliness == Hilliness.SmallHills)
                && tile.temperature > 0f && tile.swampiness < 0.25f
                && surface.Rivers.NullOrEmpty() && tile.Mutators.Count == 0;
        }

        private static void SpawnFusionResearchSite(Map map, IntVec3 center)
        {
            PrefabDef prefab = CorporateQuestDefOf.Mugirl_CorporateFusionResearchSite;
            if (prefab == null)
                throw new InvalidOperationException("Corporate fusion research prefab is missing.");

            CellRect siteRect = GenAdj.OccupiedRect(center, Rot4.North, prefab.size);
            CellRect clearanceBounds = siteRect.ExpandedBy(12);
            clearanceBounds.ClipInsideMap(map);
            foreach (IntVec3 cell in siteRect.Cells)
            {
                if (!cell.InBounds(map))
                    throw new InvalidOperationException("Corporate fusion research prefab footprint exceeds the map bounds.");
                ClearFusionResearchCell(map, cell, removePlants: true);
            }
            foreach (IntVec3 cell in clearanceBounds.Cells)
            {
                float distance = DistanceOutside(cell, siteRect);
                float edgeNoise = Mathf.PerlinNoise(cell.x * 0.115f + 17.3f, cell.z * 0.115f + 41.9f);
                if (distance > 0f && distance <= 4f + edgeNoise * 7f)
                    ClearFusionResearchCell(map, cell, removePlants: false);
            }
            ClearFusionResearchApproaches(map, siteRect);
            EnsureFusionResearchTerrainSupport(prefab, map, center);

            if (!PrefabUtility.CanSpawnPrefab(prefab, map, center, Rot4.North))
            {
                // 自定义地表或地图修饰器仍可能拒绝放置；整平本区域后继续生成，不能消耗任务并留下空图。
                MugirlLog.DiagnosticMessage("Corporate fusion research site requires forced terrain preparation: "
                    + DescribePrefabSpawnFailure(prefab, map, center));
                CellRect fallbackRect = siteRect.ExpandedBy(1);
                fallbackRect.ClipInsideMap(map);
                foreach (IntVec3 cell in fallbackRect)
                {
                    ClearFusionResearchCell(map, cell, removePlants: true);
                    if (map.terrainGrid.FoundationAt(cell) != null)
                        map.terrainGrid.RemoveFoundation(cell, doLeavings: false);
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                }
                EnsureFusionResearchTerrainSupport(prefab, map, center);
            }
            PrefabUtility.SpawnPrefab(prefab, map, center, Rot4.North, onSpawned: InitializeFusionResearchThing);
            EnsureFusionResearchThings(prefab, map, center);
        }

        private static void EnsureFusionResearchThings(PrefabDef prefab, Map map, IntVec3 center)
        {
            // 原版 SpawnPrefab 会静默跳过不能正常放置的单件物品。此固定蓝图必须完整生成；
            // 补建只针对本次生成中缺失的条目，保留已经生成的建筑，不对旧地图执行。
            foreach (var entry in PrefabUtility.GetThings(prefab, center, Rot4.North))
            {
                PrefabThingData data = entry.Item1;
                IntVec3 cell = entry.Item2;
                if (cell.GetThingList(map).Any(existing => existing.def == data.def && existing.Position == cell)) continue;
                Thing thing = ThingMaker.MakeThing(data.def, data.stuff);
                thing.stackCount = data.stackCountRange.RandomInRange;
                if (data.hp > 0) thing.HitPoints = data.hp;
                if (data.quality.HasValue)
                    thing.TryGetComp<CompQuality>()?.SetQuality(data.quality.Value, ArtGenerationContext.Outsider);
                GenSpawn.Spawn(thing, cell, map, entry.Item3);
                InitializeFusionResearchThing(thing);
            }
        }

        private static void EnsureFusionResearchTerrainSupport(PrefabDef prefab, Map map, IntVec3 center)
        {
            // CanSpawnPrefab 先检查当前地面，SpawnPrefab 才铺设 terrain。
            // 先铺研究站的固定地板，避免薄冰等原地表在地板落地前阻止整座站点生成。
            IntVec3 root = PrefabUtility.GetRoot(prefab, center, Rot4.North);
            foreach (var entry in prefab.GetTerrain())
            {
                IntVec3 cell = root + entry.cell;
                if (!cell.InBounds(map))
                    throw new InvalidOperationException("Corporate fusion research floor plan exceeds the map bounds.");
                // 薄冰属于独立的临时地形层，仅 SetTerrain 不会移除它，也不会露出新地板。
                map.terrainGrid.RemoveTempTerrain(cell, doLeavings: false, preventDestroyEffects: true);
                map.terrainGrid.SetTerrain(cell, entry.data.def);
            }
            foreach (var entry in PrefabUtility.GetThings(prefab, center, Rot4.North))
            {
                PrefabThingData data = entry.Item1;
                if (data.def.category != ThingCategory.Building || data.def.building?.isAttachment == true) continue;
                TerrainAffordanceDef affordance = data.def.GetTerrainAffordanceNeed(data.stuff);
                if (affordance == null) continue;
                foreach (IntVec3 cell in GenAdj.OccupiedRect(entry.Item2, entry.Item3, data.def.Size))
                {
                    if (!cell.InBounds(map))
                        throw new InvalidOperationException("Corporate fusion research building footprint exceeds the map bounds.");
                    map.terrainGrid.RemoveTempTerrain(cell, doLeavings: false, preventDestroyEffects: true);
                    // RimWorld 1.6 的 FoundationAt 优先于表层 TerrainAt 提供承载力。
                    // 仅铺 Concrete 会留下轻型桥梁等 foundation，原版仍会判定建筑不可放置。
                    TerrainDef foundation = map.terrainGrid.FoundationAt(cell);
                    if (foundation != null && !foundation.affordances.Contains(affordance))
                        map.terrainGrid.RemoveFoundation(cell, doLeavings: false);
                    if (!cell.GetAffordances(map).Contains(affordance))
                        map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                }
            }
        }

        private static void InitializeFusionResearchThing(Thing thing)
        {
            CompRefuelable refuelable = thing.TryGetComp<CompRefuelable>();
            if (refuelable != null && !refuelable.IsFull)
                refuelable.Refuel(refuelable.Props.fuelCapacity);
        }

        private static string DescribePrefabSpawnFailure(PrefabDef prefab, Map map, IntVec3 center)
        {
            foreach (var entry in PrefabUtility.GetThings(prefab, center, Rot4.North))
            {
                PrefabThingData data = entry.Item1;
                IntVec3 cell = entry.Item2;
                Rot4 rot = entry.Item3;
                if ((data.def.building == null || !data.def.building.isAttachment)
                    && !GenSpawn.CanSpawnAt(data.def, cell, map, rot))
                {
                    if (data.def.category == ThingCategory.Building && !GenConstruct.CanBuildOnTerrain(data.def, cell, map, rot))
                        return "Corporate fusion research prefab terrain validation failed for " + data.def.defName + " at " + cell
                            + " on terrain " + cell.GetTerrain(map).defName + ".";
                    if (data.def.HasSingleOrMultipleInteractionCells && !GenConstruct.InteractionCellStandable(data.def, cell, rot, map))
                        return "Corporate fusion research prefab interaction cells are blocked for " + data.def.defName + " at " + cell + ".";
                    if (!GenConstruct.NotBlockingAnyInteractionCells(data.def, cell, rot, map))
                        return "Corporate fusion research prefab would block an existing interaction cell near " + data.def.defName + " at " + cell + ".";
                    string blockers = string.Join(", ", cell.GetThingList(map).Select(t => t.def.defName).Distinct().ToArray());
                    return "Corporate fusion research prefab cannot spawn " + data.def.defName + " at " + cell
                        + " on terrain " + cell.GetTerrain(map).defName
                        + (blockers.NullOrEmpty() ? "." : "; cell contains: " + blockers + ".");
                }
            }
            return "Corporate fusion research prefab failed validation for an unknown nested-prefab condition.";
        }

        private static void ClearFusionResearchApproaches(Map map, CellRect siteRect)
        {
            int centerX = (siteRect.minX + siteRect.maxX) / 2;
            int centerZ = (siteRect.minZ + siteRect.maxZ) / 2;
            for (int z = 0; z <= siteRect.minZ; z++)
                ClearVerticalApproach(map, centerX, z, 3.7f);
            for (int z = siteRect.maxZ; z < map.Size.z; z++)
                ClearVerticalApproach(map, centerX, z, 29.1f);
            for (int x = 0; x <= siteRect.minX; x++)
                ClearHorizontalApproach(map, centerZ, x, 53.4f);
            for (int x = siteRect.maxX; x < map.Size.x; x++)
                ClearHorizontalApproach(map, centerZ, x, 77.8f);
        }

        private static void ClearVerticalApproach(Map map, int centerX, int z, float seed)
        {
            int offset = Mathf.RoundToInt((Mathf.PerlinNoise(z * 0.055f + seed, seed) - 0.5f) * 8f);
            int halfWidth = Mathf.PerlinNoise(z * 0.12f + seed, seed + 11f) > 0.68f ? 2 : 1;
            for (int x = centerX + offset - halfWidth; x <= centerX + offset + halfWidth; x++)
                ClearFusionResearchCell(map, new IntVec3(x, 0, z), removePlants: false);
        }

        private static void ClearHorizontalApproach(Map map, int centerZ, int x, float seed)
        {
            int offset = Mathf.RoundToInt((Mathf.PerlinNoise(x * 0.055f + seed, seed) - 0.5f) * 8f);
            int halfWidth = Mathf.PerlinNoise(x * 0.12f + seed, seed + 11f) > 0.68f ? 2 : 1;
            for (int z = centerZ + offset - halfWidth; z <= centerZ + offset + halfWidth; z++)
                ClearFusionResearchCell(map, new IntVec3(x, 0, z), removePlants: false);
        }

        private static float DistanceOutside(IntVec3 cell, CellRect rect)
        {
            int dx = cell.x < rect.minX ? rect.minX - cell.x : cell.x > rect.maxX ? cell.x - rect.maxX : 0;
            int dz = cell.z < rect.minZ ? rect.minZ - cell.z : cell.z > rect.maxZ ? cell.z - rect.maxZ : 0;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        internal static void ClearFusionResearchCell(Map map, IntVec3 cell, bool removePlants)
        {
            if (!cell.InBounds(map)) return;
            // Destroying mineables and buildings can leave fresh rubble after the original thing-list
            // snapshot was taken, so repeat a few bounded passes. PassThroughOnly items such as stone
            // chunks also make GenSpawn.CanSpawnAt reject an otherwise wipeable prefab building.
            for (int pass = 0; pass < 4; pass++)
            {
                List<Thing> blockers = cell.GetThingList(map)
                    .Where(thing => ShouldClearFusionResearchThing(thing, removePlants)).ToList();
                if (blockers.Count == 0) break;
                foreach (Thing thing in blockers)
                    if (!thing.Destroyed) DestroyFusionResearchBlocker(thing);
            }
            map.roofGrid.SetRoof(cell, null);
            // 临时冰层或其他临时地表会遮住新地板，也可能在人员进场后融化成水。
            map.terrainGrid.RemoveTempTerrain(cell, doLeavings: false, preventDestroyEffects: true);
            if (cell.GetTerrain(map).passability == Traversability.Impassable)
            {
                if (map.terrainGrid.FoundationAt(cell) != null)
                    map.terrainGrid.RemoveFoundation(cell, doLeavings: false);
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
            }
        }

        private static bool ShouldClearFusionResearchThing(Thing thing, bool removePlants)
        {
            if (thing == null || thing.Destroyed || thing is Pawn) return false;
            return thing is Building
                || thing.def.category == ThingCategory.Filth
                || thing.def.entityDefToBuild is TerrainDef
                || removePlants && thing.def.category == ThingCategory.Plant
                || thing.def.passability != Traversability.Standable;
        }

        private static void DestroyFusionResearchBlocker(Thing thing)
        {
            if (thing.def.destroyable)
            {
                thing.Destroy(DestroyMode.Vanish);
                return;
            }

            // Natural blockers such as SteamGeyser are deliberately non-destroyable during play.
            // Vanilla LayoutWorker temporarily enables the same guard while wiping a structure site.
            bool previous = Thing.allowDestroyNonDestroyable;
            try
            {
                Thing.allowDestroyNonDestroyable = true;
                thing.Destroy(DestroyMode.Vanish);
            }
            finally
            {
                Thing.allowDestroyNonDestroyable = previous;
            }
        }

        private void TickMissionSite(CorporateMission m)
        {
            if (m.site == null || !m.site.Spawned) { FailMission(m); return; }
            if (!m.spawned) return;
            if (m.targets.Any(p => p == null)) { FailMission(m); return; }
            foreach (Pawn pawn in m.targets.Where(p => p.Dead).ToList()) { m.targets.Remove(pawn); m.progress++; }
            if (m.IsSide && m.choice == CorporateResearchChoice.Undecided)
            {
                // 玩家主动攻击会在伤害前明确转入执行；其他原因致死只能中断现场。
                if (m.investor == null || m.investor.Dead || m.progress > 0) { FailMission(m); return; }
                if (!m.dialogueShown && m.site.HasMap && m.site.Map.mapPawns.FreeColonistsSpawned.Any())
                {
                    m.dialogueShown = true;
                    ShowResearchDialogue(m);
                }
                return;
            }
            if (m.IsSide && m.choice == CorporateResearchChoice.Protect) return;
            if (m.targets.Any(p => !p.Spawned && !p.IsPrisonerOfColony)) { FailMission(m); return; }
            if (m.progress + m.targets.Count(p => p.IsPrisonerOfColony) >= m.count) MissionReady(m);
        }

        public void ShowResearchDialogue(CorporateMission m)
        {
            if (m == null || !m.IsSide || m.state != CorporateMissionState.Active || m.choice != CorporateResearchChoice.Undecided
                || m.site?.HasMap != true || !m.site.Map.mapPawns.FreeColonistsSpawned.Any()) return;
            string identity = m.originalInvestor ? "Mugirl.CQ.ResearchOriginal".Translate() : "Mugirl.CQ.ResearchRepresentative".Translate(retainedFusionInvestorName ?? "Mugirl.CQ.OriginalTeam".Translate());
            DiaNode root = new DiaNode(identity + "\n\n" + "Mugirl.CQ.ResearchDialogue".Translate());
            DiaNode more = new DiaNode("Mugirl.CQ.ResearchMore".Translate());
            more.options.Add(new DiaOption("Mugirl.CQ.Back".Translate()) { link = root });
            root.options.Add(new DiaOption("Mugirl.CQ.AskResearch".Translate()) { link = more });
            DiaNode execute = new DiaNode("Mugirl.CQ.ExecuteConfirm".Translate());
            execute.options.Add(new DiaOption("Mugirl.CQ.Execute".Translate()) { action = () => BeginResearchCombat(m), resolveTree = true });
            execute.options.Add(new DiaOption("Mugirl.CQ.Back".Translate()) { link = root });
            root.options.Add(new DiaOption("Mugirl.CQ.Execute".Translate()) { link = execute });
            Faction corporation = CorporateFaction;
            int goodwill = corporation?.PlayerGoodwill ?? 0;
            int basePenalty = MissionConfig.fusionRefusalGoodwill;
            int adjustedPenalty = corporation?.CalculateAdjustedGoodwillChange(MugirlWildSlaveUtility.PlayerFaction, basePenalty) ?? basePenalty;
            int projectedBase = Mathf.Clamp((corporation?.BaseGoodwillWith(MugirlWildSlaveUtility.PlayerFaction) ?? goodwill) + adjustedPenalty, -100, 100);
            // 负向变更保留原版自然好感加成及情势上限，显示实际预计损失。
            int projectedGoodwill = Mathf.Min(goodwill, projectedBase);
            DiaNode protect = new DiaNode("Mugirl.CQ.ProtectConfirm".Translate(basePenalty, projectedGoodwill - goodwill, goodwill, projectedGoodwill,
                FusionProtectionRewardDescription));
            protect.options.Add(new DiaOption("Mugirl.CQ.Protect".Translate()) { action = () => ProtectResearchers(m), resolveTree = true });
            protect.options.Add(new DiaOption("Mugirl.CQ.Back".Translate()) { link = root });
            root.options.Add(new DiaOption("Mugirl.CQ.Protect".Translate()) { link = protect });
            root.options.Add(new DiaOption("Mugirl.CQ.Later".Translate()) { resolveTree = true });
            MugirlGameUtility.Windows.Add(new Dialog_NodeTree(root, true, true, "Mugirl.CQ.ResearchTitle".Translate()));
        }

        private void BeginResearchCombat(CorporateMission m)
        {
            if (m.state != CorporateMissionState.Active || m.choice != CorporateResearchChoice.Undecided) return;
            m.choice = CorporateResearchChoice.Execute;
            List<Pawn> active = m.targets.Where(p => p != null && p.Spawned && !p.Dead && !p.IsPrisonerOfColony).ToList();
            foreach (Pawn pawn in active)
            {
                pawn.GetLord()?.RemovePawn(pawn);
                pawn.SetFaction(Faction.OfAncientsHostile);
            }
            if (active.Count > 0) LordMaker.MakeNewLord(Faction.OfAncientsHostile, new LordJob_DefendPoint(active[0].Position), active[0].Map, active);
            MugirlGameUtility.Letters.ReceiveLetter("Mugirl.CQ.CombatLetter".Translate(), "Mugirl.CQ.CombatText".Translate(), LetterDefOf.ThreatSmall, m.site);
        }

        internal static string FusionProtectionRewardDescription => string.Join(", ", MissionConfig.fusionProtectionRewards
            .Select(reward => reward.count + " × " + reward.thingDef.LabelCap).ToArray());

        private void ProtectResearchers(CorporateMission m)
        {
            if (m == null || !missions.Contains(m) || !m.IsSide || m.state != CorporateMissionState.Active
                || m.choice != CorporateResearchChoice.Undecided) return;
            try
            {
                // 复用 Vault 和 pendingGoods 的存档所有权；准备成功后才锁定结局，重进对白不会重发。
                foreach (ThingDefCountClass reward in MissionConfig.fusionProtectionRewards)
                    PrepareMissionGoods(m, reward.thingDef, reward.count);
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
            CorporateFaction?.TryAffectGoodwillWith(MugirlWildSlaveUtility.PlayerFaction, MissionConfig.fusionRefusalGoodwill);
            Record("Mugirl.CQ.ProtectedRecord", m.Title);
            EndMissionQuest(m, QuestEndOutcome.Success);
            MugirlGameUtility.Letters.ReceiveLetter("Mugirl.CQ.ThanksTitle".Translate(), "Mugirl.CQ.ThanksText".Translate(FusionProtectionRewardDescription), LetterDefOf.PositiveEvent, m.site);
        }

        internal void ResearcherAttacked(Pawn pawn)
        {
            CorporateMission m = missions.FirstOrDefault(x => x.IsSide && x.spawned && x.targets.Contains(pawn));
            if (m == null) return;
            if (m.choice == CorporateResearchChoice.Undecided) BeginResearchCombat(m);
            else if (m.choice == CorporateResearchChoice.Protect && !m.gratitudeRevoked)
            {
                m.gratitudeRevoked = true;
                DiscardPendingMissionGoods(m);
                MugirlGameUtility.Letters.ReceiveLetter("Mugirl.CQ.ThanksRevokedTitle".Translate(), "Mugirl.CQ.ThanksRevokedText".Translate(), LetterDefOf.NegativeEvent, m.site);
            }
        }
    }

    internal static class CorporateQuestSiteDebugActions
    {
        [DebugAction("Mugirl", "Test map: fusion research site", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnFusionResearchSiteAtMapCenter()
        {
            Map map = MugirlGameUtility.LoadedMaps?.FirstOrDefault(MugirlGameUtility.IsCurrentMap);
            if (!Prefs.DevMode || map == null)
            {
                Messages.Message("Mugirl.CQ.DebugMapUnavailable".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            try
            {
                CorporateNetwork.DevSpawnFusionResearchSite(map);
                Messages.Message("Mugirl.CQ.DebugMapSpawned".Translate(), MessageTypeDefOf.PositiveEvent, historical: false);
            }
            catch (Exception e)
            {
                MugirlLog.WarningOnce("CorporateQuestSite.Debug", "Corporate fusion research map debug spawn failed: " + e.Message);
                Messages.Message("Mugirl.CQ.DebugMapFailed".Translate(e.Message), MessageTypeDefOf.RejectInput, historical: false);
            }
        }
    }

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.ResolveTrade))]
    public static class Harmony_CorporateDistributionSale
    {
        public sealed class Sale { public ThingDef def; public ThingDef stuff; public int count; }
        public static void Prefix(Tradeable __instance, out Sale __state)
        {
            __state = null;
            CorporateNetwork network = CorporateNetwork.Current;
            if (network == null || TradeSession.giftMode || __instance.ActionToDo != TradeAction.PlayerSells
                || !__instance.HasAnyThing || __instance.IsCurrency || TradeSession.trader?.Faction == network.CorporateFaction) return;
            __state = new Sale { def = __instance.ThingDef, stuff = __instance.StuffDef, count = Mathf.Abs(__instance.CountToTransfer) };
        }
        public static void Postfix(Sale __state)
        {
            if (__state != null) CorporateNetwork.Current?.NotifyDistributionSale(__state.def, __state.stuff, __state.count);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Harmony_CorporateResearchAttacked
    {
        public static void Prefix(Thing __instance, DamageInfo dinfo)
        {
            if (__instance is Pawn pawn && MugirlWildSlaveUtility.IsPlayerFaction(dinfo.Instigator?.Faction) && dinfo.Amount > 0)
                CorporateNetwork.Current?.ResearcherAttacked(pawn);
        }
    }

    [HarmonyPatch(typeof(Site), nameof(Site.GetGizmos))]
    public static class Harmony_CorporateResearchGizmos
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Site __instance)
        {
            foreach (Gizmo gizmo in __result) yield return gizmo;
            CorporateNetwork network = CorporateNetwork.Current;
            CorporateMission mission = network?.Missions.FirstOrDefault(m => m.IsSide && m.site == __instance && m.state == CorporateMissionState.Active
                && m.choice == CorporateResearchChoice.Undecided);
            if (mission != null && __instance.HasMap && __instance.Map.mapPawns.FreeColonistsSpawned.Any())
                yield return new Command_Action { defaultLabel = "Mugirl.CQ.ResearchTitle".Translate(), defaultDesc = "Mugirl.CQ.ReopenDialogue".Translate(),
                    action = () => network.ShowResearchDialogue(mission), icon = TexCommand.ForbidOff };
        }
    }
}
