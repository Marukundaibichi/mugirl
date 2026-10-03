// 仅在当前模组 TMP 隔离配置及显式开关下运行；独立编译，不进入正式 DLL。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Mugirl;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MugirlNewbornProbe
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        private static string output;
        private static bool ran;
        private static int passed, failed;

        static Bootstrap()
        {
            if (!GenCommandLine.CommandLineArgPassed("mugirlNewbornProbe")) return;
            string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(Mugirl_DefOf).Assembly.Location), "../..", "TMP"));
            output = Path.GetFullPath(GenFilePaths.SaveDataFolderPath);
            if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            var harmony = new Harmony("mugirl.newborn.runtime.probe");
            harmony.Patch(AccessTools.Method(typeof(Game), "UpdatePlay"), postfix: new HarmonyMethod(typeof(Bootstrap), "Update"));
            harmony.Patch(AccessTools.Method(typeof(PawnBioAndNameGenerator), "FillBackstorySlotShuffled"),
                prefix: new HarmonyMethod(typeof(Bootstrap), "BeforeBackstory") { priority = Priority.First });
            File.WriteAllText(Path.Combine(output, "progress.txt"), "Waiting for isolated quicktest");
            Application.runInBackground = true;
        }

        private static void Check(string label, bool success)
        {
            if (success) passed++; else failed++;
            File.AppendAllText(Path.Combine(output, "checks.txt"), (success ? "PASS " : "FAIL ") + label + Environment.NewLine);
        }

        private static void BeforeBackstory(Pawn pawn, BackstorySlot slot, List<BackstoryCategoryFilter> backstoryCategories)
        {
            if (!ran || slot != BackstorySlot.Childhood || !pawn.DevelopmentalStage.Baby()) return;
            string categories = string.Join("|", backstoryCategories.Select(f => string.Join(",", f.categories ?? new List<string>())));
            File.AppendAllText(Path.Combine(output, "generation.txt"), pawn.kindDef.defName + " race=" + pawn.def.defName
                + " gender=" + pawn.gender + " stage=" + pawn.DevelopmentalStage + " age=" + pawn.ageTracker.AgeBiologicalYearsFloat
                + " filters=" + categories + Environment.NewLine);
        }

        private static Pawn Generate(PawnKindDef kind, float age, DevelopmentalStage stage, Gender? gender = null)
        {
            return PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, Faction.OfPlayer, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, allowDowned: true, canGeneratePawnRelations: false,
                fixedBiologicalAge: age, fixedChronologicalAge: age, fixedGender: gender,
                forceNoIdeo: true, forcedXenotype: XenotypeDefOf.Baseliner, developmentalStages: stage));
        }

        private static void Update()
        {
            if (ran || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting
                || Find.CurrentMap?.mapPawns.FreeColonistsSpawned.Count < 1) return;
            ran = true;
            try
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                File.WriteAllText(Path.Combine(output, "progress.txt"), "Checking newborn generation and mixed births");
                foreach (string kindName in new[] { "Mugirl_Colony", "Mugirl_Slave", "Mugirl_EscapeSpaceSlave" })
                {
                    PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamed(kindName);
                    for (int i = 0; i < 8; i++)
                    {
                        Pawn baby = Generate(kind, 0f, DevelopmentalStage.Newborn);
                        Check(kindName + " newborn " + i + " gender=" + baby.gender + " childhood=" + baby.story.Childhood?.defName,
                            baby.story.Childhood == Mugirl_DefOf.Mugirl_Newborn);
                        Find.WorldPawns.PassToWorld(baby, PawnDiscardDecideMode.Discard);
                    }
                }
                PawnKindDef colonyKind = DefDatabase<PawnKindDef>.GetNamed("Mugirl_Colony");
                Pawn adult = Generate(colonyKind, 24f, DevelopmentalStage.Adult, Gender.Female);
                Check("Adult Mugirl keeps an adult-appropriate childhood", adult.story.Childhood != Mugirl_DefOf.Mugirl_Newborn);
                Pawn humanBaby = Generate(PawnKindDefOf.Colonist, 0f, DevelopmentalStage.Newborn, Gender.Female);
                Check("Human newborn keeps vanilla childhood", humanBaby.def == ThingDefOf.Human && humanBaby.story.Childhood != Mugirl_DefOf.Mugirl_Newborn);
                Map map = Find.CurrentMap;
                IntVec3 center = map.mapPawns.FreeColonistsSpawned.First().Position;
                GenSpawn.Spawn(adult, CellFinder.RandomClosewalkCellNear(center, map, 8), map);
                Pawn father = Generate(PawnKindDefOf.Colonist, 24f, DevelopmentalStage.Adult, Gender.Male);
                GenSpawn.Spawn(father, CellFinder.RandomClosewalkCellNear(center, map, 8), map);
                for (int i = 0; i < 4; i++)
                {
                    Thing result = PregnancyUtility.ApplyBirthOutcome(RitualOutcomeEffectDefOf.ChildBirth.BestOutcome, 1f,
                        null, PregnancyUtility.GetInheritedGenes(father, adult), adult, adult, father, preventLetter: true);
                    Pawn child = result as Pawn ?? (result as Corpse)?.InnerPawn;
                    Check("Mugirl mother + baseliner father birth " + i + " race=" + child?.def.defName + " childhood=" + child?.story.Childhood?.defName,
                        child?.def == Mugirl_DefOf.Mugirl && child.story.Childhood == Mugirl_DefOf.Mugirl_Newborn);
                    Check("Mixed birth retains both parents " + i, child != null
                        && child.relations.DirectRelationExists(PawnRelationDefOf.Parent, adult)
                        && child.relations.DirectRelationExists(PawnRelationDefOf.Parent, father));
                }
            }
            catch (Exception ex) { Check("runtime exception: " + ex, false); }
            File.WriteAllText(Path.Combine(output, "complete.txt"), "PASS=" + passed + " FAIL=" + failed);
            Application.Quit();
        }
    }
}
