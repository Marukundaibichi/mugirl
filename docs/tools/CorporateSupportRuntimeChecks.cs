// 仅在隔离 quicktest 和显式命令行开关下运行，不创建 GameComponent 或玩家存档数据。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Mugirl
{
    [HarmonyPatch(typeof(Game), nameof(Game.UpdatePlay))]
    internal static class CorporateSupportRuntimeChecks
    {
        // StaticCacheLifecycle: 单次隔离验证进程内保留检查结果及存读档 ID，退出即释放。
        private static int phase;
        private static int failures;
        private static int checks;
        private static Game savedGame;
        private static int medicId;
        private static int patientId;
        private static int legacyFightId;
        private static int legacyFleeId;
        private static int legacyTendJobId;
        private static string outputRoot;

        private static void Postfix()
        {
            if (!GenCommandLine.CommandLineArgPassed("mugirlCorporateSupportChecks") || phase == 3
                || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
            outputRoot = Path.GetFullPath(GenFilePaths.SaveDataFolderPath);
            string allowed = Path.GetFullPath(Path.Combine(MugirlMod.ContentRoot, "TMP")) + Path.DirectorySeparatorChar;
            if (!outputRoot.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) return;
            if (phase == 1 && ReferenceEquals(savedGame, Current.Game)) return;
            try
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                if (phase == 0)
                {
                    phase = 2;
                    Run();
                }
                else if (phase == 1)
                {
                    phase = 2;
                    VerifyReload();
                    Finish();
                }
            }
            catch (Exception ex)
            {
                Check("runtime exception: " + ex, false);
                Finish();
            }
        }

        private static void Check(string label, bool passed)
        {
            checks++;
            if (!passed) failures++;
            File.AppendAllText(Path.Combine(outputRoot, "support-checks.txt"), (passed ? "PASS " : "FAIL ") + label + Environment.NewLine);
        }

        private static Pawn Spawn(Map map, Faction faction, IntVec3 center, bool diehard = true)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(MugirlContentDefOf.Mugirl_CorporateSupport,
                faction, forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                canGeneratePawnRelations: false, allowPregnant: false, developmentalStages: DevelopmentalStage.Adult));
            if (diehard) CorporateDiehardUtility.MakeDiehard(pawn);
            CorporateSupportUtility.EquipFieldSupplies(pawn);
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(center, map, 5), map);
            pawn.skills.GetSkill(SkillDefOf.Medicine).Level = 20;
            return pawn;
        }

        private static void Hurt(Pawn pawn)
        {
            Hediff injury = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, pawn.RaceProps.body.corePart);
            injury.Severity = 2f;
            pawn.health.AddHediff(injury);
        }

        private sealed class TendGiver : JobGiver_CorporateSupportTend
        {
            internal Job Get(Pawn pawn) => base.TryGiveJob(pawn);
        }

        private static void TickUntil(Func<bool> done, int limit = 2000)
        {
            for (int i = 0; i < limit && !done(); i++) Find.TickManager.DoSingleTick();
        }

        private static void Run()
        {
            Map map = Find.CurrentMap;
            Faction faction = CorporateNetwork.Current.CorporateFaction;
            Faction.OfPlayer.TryAffectGoodwillWith(faction, 60 - Faction.OfPlayer.BaseGoodwillWith(faction), false, false);
            IntVec3 center = map.mapPawns.FreeColonistsSpawned.First().Position;
            var apparel = new XmlDocument();
            apparel.Load(Path.Combine(MugirlMod.ContentRoot, "1.6", "Defs", "Apparel", "Apparel_0920.xml"));
            foreach (XmlNode node in apparel.SelectNodes("/Defs/ThingDef/defName"))
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamed(node.InnerText);
                Check(def.defName + " runtime age filter is Adult", def.apparel.developmentalStageFilter == DevelopmentalStage.Adult);
            }

            foreach (int count in new[] { 4, 8 })
            {
                var troops = Enumerable.Range(0, count).Select(_ => Spawn(map, faction, center)).ToList();
                Lord lord = LordMaker.MakeNewLord(faction, new LordJob_CorporateSupport(center), map, troops);
                Check(count + " troopers have no automatic flee state", !lord.Graph.lordToils.Any(t => t is LordToil_PanicFlee));
                Check(count + " troopers cannot start individual panic", !troops[0].mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.PanicFlee, forceWake: true));
                for (int i = 0; i < count - 1; i++)
                {
                    troops[i].Kill(null);
                    lord.LordTick();
                    Check(count + " troopers keep fighting after casualty " + (i + 1), lord.CurLordToil is LordToil_CorporateSupportHunt);
                }
                for (int tick = 0; tick <= CorporateNetwork.DayTicks; tick++) lord.LordTick();
                Check(count + " troopers still leave after their support day", lord.CurLordToil is LordToil_ExitMap);
                troops.Last().Destroy();
            }

            Pawn medic = Spawn(map, faction, center);
            Pawn patient = Spawn(map, Faction.OfPlayer, center);
            LordMaker.MakeNewLord(faction, new LordJob_CorporateSupport(center), map, new[] { medic });
            CheckGoodwill(faction, medic, patient);

            Hurt(patient);
            Job walk = JobMaker.MakeJob(JobDefOf.Goto, CellFinder.RandomClosewalkCellNear(center, map, 18));
            patient.jobs.StartJob(walk, JobCondition.InterruptForced);
            Job treatment = new TendGiver().Get(medic);
            Check("moving injured colonist is selected for field treatment", treatment?.targetA.Pawn == patient);
            medic.jobs.StartJob(treatment, JobCondition.InterruptForced);
            Check("patient waits during approach", patient.CurJobDef == JobDefOf.Wait_MaintainPosture && !patient.pather.Moving);
            int waitId = patient.CurJob.loadID;
            TickUntil(() => !patient.health.HasHediffsNeedingTend());
            Check("moving patient receives a completed dressing", !patient.health.HasHediffsNeedingTend());
            Check("completed treatment releases patient wait", patient.CurJob?.loadID != waitId);

            Hurt(medic);
            Job selfTend = new TendGiver().Get(medic);
            Check("injured medic can select self-treatment", selfTend?.targetA.Pawn == medic);
            medic.jobs.StartJob(selfTend, JobCondition.InterruptForced);
            TickUntil(() => !medic.health.HasHediffsNeedingTend());
            Check("self-treatment completes without a separate patient wait", !medic.health.HasHediffsNeedingTend());

            Hurt(patient);
            patient.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait_Wander, patient.Position), JobCondition.InterruptForced);
            Job withoutMedicine = new TendGiver().Get(medic);
            Check("used-up field medicine falls back to hand tending", withoutMedicine != null && !withoutMedicine.targetB.HasThing);
            medic.jobs.StartJob(withoutMedicine, JobCondition.InterruptForced);
            waitId = patient.CurJob.loadID;
            medic.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
            Check("interrupted doctor releases exactly its patient wait", patient.CurJob?.loadID != waitId);

            medic.jobs.StartJob(new TendGiver().Get(medic), JobCondition.InterruptForced);
            Job ordered = JobMaker.MakeJob(JobDefOf.Goto, CellFinder.RandomClosewalkCellNear(center, map, 15));
            ordered.playerForced = true;
            patient.jobs.StartJob(ordered, JobCondition.InterruptForced);
            TickUntil(() => medic.CurJobDef != MugirlContentDefOf.Mugirl_CorporateSupportTend, 10);
            Check("new patient order cancels pursuit without cancelling the order", medic.CurJobDef != MugirlContentDefOf.Mugirl_CorporateSupportTend
                && patient.CurJob == ordered);
            Check("field treatment respects an existing player order", new TendGiver().Get(medic) == null);

            patient.drafter.Drafted = true;
            Check("field treatment does not interrupt drafted patients", new TendGiver().Get(medic) == null);
            patient.drafter.Drafted = false;
            patient.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait_Wander, patient.Position), JobCondition.InterruptForced);
            medic.jobs.StartJob(new TendGiver().Get(medic), JobCondition.InterruptForced);
            medicId = medic.thingIDNumber;
            patientId = patient.thingIDNumber;

            Lord fighting = LegacyLord(map, faction, center, false);
            Lord fleeing = LegacyLord(map, faction, center, true);
            legacyFightId = fighting.ownedPawns[0].thingIDNumber;
            legacyFleeId = fleeing.ownedPawns[0].thingIDNumber;
            savedGame = Current.Game;
            GameDataSaveLoader.SaveGame("CorporateSupportRoundtrip");
            Check("support and active treatment saved", File.Exists(GenFilePaths.FilePathForSavedGame("CorporateSupportRoundtrip")));
            phase = 1;
            GameDataSaveLoader.LoadGame("CorporateSupportRoundtrip");
        }

        private static void CheckGoodwill(Faction faction, Pawn diehard, Pawn attacker)
        {
            int before = Faction.OfPlayer.BaseGoodwillWith(faction);
            faction.Notify_MemberTookDamage(diehard, new DamageInfo(DamageDefOf.Bullet, 20f, instigator: attacker));
            Check("real friendly-fire callback applies 95 percent goodwill discount", Faction.OfPlayer.BaseGoodwillWith(faction) == before - 1);
            before = Faction.OfPlayer.BaseGoodwillWith(faction);
            int ordinaryPenalty = Faction.OfPlayer.CalculateAdjustedGoodwillChange(faction, -20);
            Faction.OfPlayer.TryAffectGoodwillWith(faction, -20, false, false, HistoryEventDefOf.AttackedMember, null);
            Check("null goodwill target safely retains normal penalty", Faction.OfPlayer.BaseGoodwillWith(faction) == before + ordinaryPenalty);
            before = Faction.OfPlayer.BaseGoodwillWith(faction);
            ordinaryPenalty = Faction.OfPlayer.CalculateAdjustedGoodwillChange(faction, -20);
            Faction.OfPlayer.TryAffectGoodwillWith(faction, -20, false, false, HistoryEventDefOf.AttackedMember,
                new GlobalTargetInfo(diehard.Position, diehard.Map));
            Check("cell goodwill target safely retains normal penalty", Faction.OfPlayer.BaseGoodwillWith(faction) == before + ordinaryPenalty);
            Faction.OfPlayer.TryAffectGoodwillWith(faction, 60 - Faction.OfPlayer.BaseGoodwillWith(faction), false, false);
        }

        private static Lord LegacyLord(Map map, Faction faction, IntVec3 center, bool fleeing)
        {
            Pawn trooper = Spawn(map, faction, center);
            var job = new LordJob_CorporateSupport(center);
            Lord lord = LordMaker.MakeNewLord(faction, job, map, new[] { trooper });
            // 重建修复前原版注入的两条高优先级分支和第 3 个 toil，以旧索引实际存读档。
            typeof(LordJob_CorporateSupport).GetField("supportGraphVersion", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(job, 0);
            var flee = new LordToil_PanicFlee { lord = lord, useAvoidGrid = true };
            foreach (LordToil source in lord.Graph.lordToils.ToList())
            {
                var transition = new Transition(source, flee);
                transition.AddTrigger(new Trigger_FractionPawnsLost(0.5f));
                lord.Graph.AddTransition(transition, highPriority: true);
            }
            lord.Graph.AddToil(flee);
            if (fleeing) lord.GotoToil(flee);
            else
            {
                lord.ticksInToil = 123;
                Pawn wounded = Spawn(map, faction, center);
                lord.AddPawn(wounded);
                Hurt(wounded);
                Job oldTend = JobMaker.MakeJob(JobDefOf.TendPatient, wounded);
                oldTend.endAfterTendedOnce = true;
                trooper.jobs.StartJob(oldTend, JobCondition.InterruptForced);
                legacyTendJobId = oldTend.loadID;
                Check("legacy squad saves an active native tending job", trooper.CurJob == oldTend);
            }
            return lord;
        }

        private static void VerifyReload()
        {
            Map map = Find.CurrentMap;
            Pawn medic = map.mapPawns.AllPawnsSpawned.First(p => p.thingIDNumber == medicId);
            Pawn patient = map.mapPawns.AllPawnsSpawned.First(p => p.thingIDNumber == patientId);
            Check("active field treatment driver survives save/load", medic.jobs.curDriver is JobDriver_CorporateSupportTend);
            Check("patient wait survives save/load", patient.CurJobDef == JobDefOf.Wait_MaintainPosture);
            foreach (int id in new[] { legacyFightId, legacyFleeId })
            {
                Lord lord = map.mapPawns.AllPawnsSpawned.First(p => p.thingIDNumber == id).GetLord();
                lord.LordTick();
                Check("legacy squad " + id + " safely removes automatic flee graph", !lord.Graph.lordToils.Any(t => t is LordToil_PanicFlee));
                Check("legacy squad " + id + " resumes combat duty", lord.CurLordToil is LordToil_CorporateSupportHunt);
                if (id == legacyFightId)
                {
                    Check("legacy combat elapsed time is retained", lord.ticksInToil >= 123);
                    Check("legacy moving-patient job is reassigned after load", lord.ownedPawns[0].CurJob?.loadID != legacyTendJobId);
                }
            }
            TickUntil(() => !patient.health.HasHediffsNeedingTend());
            Check("saved field treatment completes", !patient.health.HasHediffsNeedingTend());
        }

        private static void Finish()
        {
            phase = 3;
            File.WriteAllText(Path.Combine(outputRoot, "checks-complete.txt"),
                (failures == 0 ? "PASS" : "FAIL") + " checks=" + checks + " failures=" + failures + " " + DateTime.UtcNow.ToString("O"));
            Application.Quit();
        }
    }
}
