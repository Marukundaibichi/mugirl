using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
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
            IntVec3 center = CellFinder.RandomClosewalkCellNear(map.Center, map, 12);
            try
            {
                if (m.IsSide)
                {
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
                    SpawnResearchFurniture(map, center);
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
                MugirlLog.WarningOnce("CorporateQuestSite." + m.id, "Corporate mission site generation failed: " + e.Message);
                foreach (Pawn pawn in m.targets.Where(p => p != null && !p.Spawned).ToList()) MugirlGeneratedPawnUtility.TryPassToWorld(pawn);
                FailMission(m);
            }
        }

        private static void SpawnResearchFurniture(Map map, IntVec3 center)
        {
            ThingDef[] defs = { CorporateQuestDefs.ResearchBench, CorporateQuestDefs.Table, CorporateQuestDefs.Bed };
            for (int i = 0; i < defs.Length; i++)
            {
                ThingDef def = defs[i];
                if (def == null) continue;
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(center + new IntVec3(i * 4 - 4, 0, 3), map, 3);
                Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.WoodLog : null);
                GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near);
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
            DiaNode protect = new DiaNode("Mugirl.CQ.ProtectConfirm".Translate(basePenalty, projectedGoodwill - goodwill, goodwill, projectedGoodwill));
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

        private void ProtectResearchers(CorporateMission m)
        {
            if (m.state != CorporateMissionState.Active || m.choice != CorporateResearchChoice.Undecided) return;
            m.choice = CorporateResearchChoice.Protect;
            m.reward = 0;
            m.goodwill = 0;
            m.state = CorporateMissionState.Completed;
            CorporateFaction?.TryAffectGoodwillWith(MugirlWildSlaveUtility.PlayerFaction, MissionConfig.fusionRefusalGoodwill);
            Record("Mugirl.CQ.ProtectedRecord", m.Title);
            EndMissionQuest(m, QuestEndOutcome.Success);
            MugirlGameUtility.Letters.ReceiveLetter("Mugirl.CQ.ThanksTitle".Translate(), "Mugirl.CQ.ThanksText".Translate(), LetterDefOf.PositiveEvent, m.site);
        }

        internal void ResearcherAttacked(Pawn pawn)
        {
            CorporateMission m = missions.FirstOrDefault(x => x.IsSide && x.spawned && x.targets.Contains(pawn));
            if (m == null) return;
            if (m.choice == CorporateResearchChoice.Undecided) BeginResearchCombat(m);
            else if (m.choice == CorporateResearchChoice.Protect && !m.gratitudeRevoked)
            {
                m.gratitudeRevoked = true;
                MugirlGameUtility.Letters.ReceiveLetter("Mugirl.CQ.ThanksRevokedTitle".Translate(), "Mugirl.CQ.ThanksRevokedText".Translate(), LetterDefOf.NegativeEvent, m.site);
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
