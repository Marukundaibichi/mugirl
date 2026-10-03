// 仅以 EnableGrazeValidation 构建，在隔离 quicktest 中执行真实进食 Job。
using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Mugirl
{
    [HarmonyPatch(typeof(Game), nameof(Game.UpdatePlay))]
    internal static class GrazeRuntimeValidation
    {
        // StaticCacheLifecycle: 单次隔离进程的测试状态，退出后清理，不进入存档。
        private static int phase;
        private static int failures;
        private static string outputRoot;
        private static Pawn pawn;
        private static Plant plant;
        private static Job ingestJob;
        private static float initialFood;
        private static int startTick;
        private static IntVec3 origin;
        private static bool sawPolicyGraze;

        private static void Postfix()
        {
            if (!GenCommandLine.CommandLineArgPassed("mugirlGrazeChecks")
                || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null || phase == 8)
                return;
            outputRoot = Path.GetFullPath(GenFilePaths.SaveDataFolderPath);
            string allowed = Path.GetFullPath(Path.Combine(MugirlMod.ContentRoot, "TMP")) + Path.DirectorySeparatorChar;
            if (!outputRoot.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                if (phase == 0)
                {
                    PrepareFixture();
                    CheckReleaseRules();
                    StartIngest(0.1f, true);
                    phase = 1;
                }
                else if (phase == 1 && FinishedOrTimedOut())
                {
                    Check("hungry manual grazing consumes grass", plant.Destroyed || plant.Growth < 0.99f);
                    Check("hungry manual grazing gains nutrition", pawn.needs.food.CurLevel > initialFood + 0.05f);
                    Check("hungry manual grazing finishes its job", pawn.CurJob?.def != JobDefOf.Ingest);
                    StartIngest(0.99f, true);
                    phase = 2;
                }
                else if (phase == 2 && FinishedOrTimedOut())
                {
                    Check("nearly full grazing finishes its job", pawn.CurJob?.def != JobDefOf.Ingest);
                    Check("nearly full grazing reduces growth without needing to destroy grass", plant.Destroyed || plant.Growth < 0.99f);
                    StartIngest(1f, true);
                    phase = 3;
                }
                else if (phase == 3 && FinishedOrTimedOut())
                {
                    Check("full pawn grazing finishes instead of looping", pawn.CurJob?.def != JobDefOf.Ingest);
                    StartIngest(0.1f, false);
                    for (int i = 1; i <= 12; i++) SpawnGrass(origin + new IntVec3(i % 6 + 1, 0, i / 6 + 1));
                    phase = 4;
                }
                else if (phase == 4 && (pawn.needs.food.CurLevel >= pawn.needs.food.MaxLevel - 0.02f
                    || Find.TickManager.TicksGame - startTick > 6500))
                {
                    Check("automatic chain reaches full food", pawn.needs.food.CurLevel >= pawn.needs.food.MaxLevel - 0.02f);
                    Check("automatic chain consumes first grass", plant.Destroyed || plant.Growth < 0.99f);
                    // 留出一次咀嚼的时间，确认吃饱后没有继续执行进食 Job。
                    startTick = Find.TickManager.TicksGame;
                    phase = 5;
                }
                else if (phase == 5 && Find.TickManager.TicksGame - startTick > 900)
                {
                    Check("automatic chain stops after filling food", pawn.CurJob?.def != JobDefOf.Ingest);
                    StartPolicyGraze();
                    phase = 6;
                }
                else if (phase == 6)
                {
                    if (pawn.CurJob?.def == JobDefOf.Ingest
                        && pawn.CurJob.targetA.Thing is Plant) sawPolicyGraze = true;
                    if (pawn.needs.food.CurLevel >= pawn.needs.food.MaxLevel - 0.02f
                        || Find.TickManager.TicksGame - startTick > 8500)
                    {
                        Check("colonist Grass policy automatically starts plant ingest", sawPolicyGraze);
                        Check("colonist Grass policy fills food", pawn.needs.food.CurLevel >= pawn.needs.food.MaxLevel - 0.02f);
                        startTick = Find.TickManager.TicksGame;
                        phase = 7;
                    }
                }
                else if (phase == 7 && Find.TickManager.TicksGame - startTick > 900)
                {
                    Check("colonist Grass policy stops ingest after filling food", pawn.CurJob?.def != JobDefOf.Ingest);
                    Finish();
                }
                if (phase != 8) Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            }
            catch (Exception exception)
            {
                failures++;
                File.AppendAllText(Path.Combine(outputRoot, "graze-checks.txt"), exception + Environment.NewLine);
                Finish();
            }
        }

        private static void PrepareFixture()
        {
            Map map = Find.CurrentMap;
            origin = CellFinder.StandableCellNear(map.Center, map, 20);
            foreach (IntVec3 cell in CellRect.CenteredOn(origin, 9).Cells)
            {
                if (!cell.InBounds(map)) continue;
                foreach (Thing thing in cell.GetThingList(map).ToList())
                    if (thing is Plant || thing is Building)
                    {
                        if (thing.def.destroyable) thing.Destroy();
                        else thing.DeSpawn();
                    }
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                map.fogGrid.Unfog(cell);
            }
            map.pathing.RecalculateAllPerceivedPathCosts();
            PawnKindDef kind = DefDatabase<PawnKindDef>.AllDefsListForReading.First(x => x.race == Mugirl_DefOf.Mugirl);
            pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, Faction.OfPlayer,
                PawnGenerationContext.NonPlayer, fixedBiologicalAge: 25f, fixedGender: Gender.Female));
            GenSpawn.Spawn(pawn, origin, map);
            pawn.drafter.Drafted = true;
            pawn.jobs.StopAll();
        }

        private static void CheckReleaseRules()
        {
            Check("mechanical pawn and race are disabled", DefDatabase<PawnKindDef>.GetNamedSilentFail("Mugirl_MechanoidBulldog") == null
                && DefDatabase<ThingDef>.GetNamedSilentFail("Mugirl_MechanoidBulldog") == null);
            Check("throw Def requires violence", Mugirl_DefOf.Mugirl_Throw.casterMustBeCapableOfViolence);
            BackstoryDef previous = pawn.story.Adulthood;
            pawn.story.Adulthood = DefDatabase<BackstoryDef>.AllDefsListForReading.First(x => (x.workDisables & WorkTags.Violent) != 0);
            pawn.Notify_DisabledWorkTypesChanged();
            Thing chunk = GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("ChunkGranite"), origin + IntVec3.West, pawn.Map);
            string reason;
            Check("violence disabled fixture", pawn.WorkTagIsDisabled(WorkTags.Violent));
            Check("violence disabled rejects direct pickup", !MugirlThrowUtility.CanPickUp(pawn, chunk, out reason));
            pawn.abilities.GainAbility(Mugirl_DefOf.Mugirl_Throw);
            Check("violence disabled greys out throw ability", pawn.abilities.GetAbility(Mugirl_DefOf.Mugirl_Throw).GizmoDisabled(out reason));
            chunk.Destroy();
            pawn.story.Adulthood = previous;
            pawn.Notify_DisabledWorkTypesChanged();
        }

        private static Plant SpawnGrass(IntVec3 cell)
        {
            Plant result = (Plant)GenSpawn.Spawn(ThingDefOf.Plant_Grass, cell, pawn.Map);
            result.Growth = 1f;
            return result;
        }

        private static void StartIngest(float foodPercent, bool forced)
        {
            pawn.jobs.StopAll();
            if (plant != null && !plant.Destroyed) plant.Destroy();
            pawn.Position = origin;
            pawn.needs.food.CurLevel = pawn.needs.food.MaxLevel * foodPercent;
            initialFood = pawn.needs.food.CurLevel;
            plant = SpawnGrass(origin + IntVec3.East);
            ingestJob = JobMaker.MakeJob(JobDefOf.Ingest, plant);
            ingestJob.count = 1;
            ingestJob.playerForced = forced;
            // 征召只用于让手动用例结束后停留原地；自动链必须解除征召。
            pawn.drafter.Drafted = forced;
            pawn.jobs.StartJob(ingestJob, JobCondition.InterruptForced);
            startTick = Find.TickManager.TicksGame;
        }

        private static bool FinishedOrTimedOut()
        {
            int elapsed = Find.TickManager.TicksGame - startTick;
            if (elapsed % 300 < 4)
                File.AppendAllText(Path.Combine(outputRoot, "graze-progress.txt"),
                    "phase=" + phase + " tick=" + elapsed + " food=" + pawn.needs.food.CurLevel
                    + " growth=" + (plant.Destroyed ? "destroyed" : plant.Growth.ToString())
                    + " job=" + pawn.CurJob + " ticksLeft=" + pawn.jobs.curDriver?.ticksLeftThisToil + Environment.NewLine);
            // Job 会进入原版对象池，旧引用可能已被重用为 Wait_Combat，不能按引用判定结束。
            return pawn.CurJob?.def != JobDefOf.Ingest || elapsed > 2200;
        }

        private static void StartPolicyGraze()
        {
            pawn.drafter.Drafted = false;
            pawn.jobs.StopAll();
            pawn.Position = origin;
            pawn.needs.food.CurLevel = pawn.needs.food.MaxLevel * 0.1f;
            MugirlFoodPolicyUtility.EnsureGrassPolicy(Current.Game.foodRestrictionDatabase);
            string label = "Mugirl.FoodPolicy.Grass.Label".Translate();
            pawn.foodRestriction.CurrentFoodPolicy = Current.Game.foodRestrictionDatabase.AllFoodRestrictions.First(x => x.label == label);
            foreach (IntVec3 cell in CellRect.CenteredOn(origin, 7).Cells)
                foreach (Plant existing in cell.GetThingList(pawn.Map).OfType<Plant>().ToList()) existing.Destroy();
            for (int i = 1; i <= 14; i++) SpawnGrass(origin + new IntVec3(i % 6 + 1, 0, i / 6 + 1));
            // 不下达 Ingest Job，让殖民者原版食物搜索自然选择“草”方案中的植物。
            startTick = Find.TickManager.TicksGame;
        }

        private static void Check(string label, bool passed)
        {
            if (!passed) failures++;
            File.AppendAllText(Path.Combine(outputRoot, "graze-checks.txt"), (passed ? "PASS " : "FAIL ") + label + Environment.NewLine);
        }

        private static void Finish()
        {
            phase = 8;
            File.WriteAllText(Path.Combine(outputRoot, "graze-complete.txt"), "failures=" + failures);
            Application.Quit();
        }
    }
}
