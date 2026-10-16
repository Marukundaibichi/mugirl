// 独立回归探针，不编入正式 DLL；仅在显式开关和本模组 TMP 隔离配置下运行。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Mugirl.Features.WeaponWheel;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MugirlHuntRegression
{
    [StaticConstructorOnStartup]
    public static class Probe
    {
        private static readonly string[] Cases = {
            "control", "identical-rifles", "rifle-to-pistol", "pistol-to-rifle", "reserve-reorder",
            "empty-primary", "melee-primary", "drop-primary", "automatic-cycle", "non-hunt-job",
            "save-after-swap", "legacy-stale-save"
        };
        private static readonly HashSet<string> firedWeapons = new HashSet<string>();
        private static string output, expectedWeaponId;
        private static Pawn hunter, victim;
        private static Comp_WeaponWheel wheel;
        private static Verb initialVerb;
        private static Job hunt;
        private static Game savedGame;
        private static int caseIndex = -1, startTick, depth, maxDepth, hunterId, victimId, savedTick, passed;
        private static bool acted, cutoff, done, waitingReload, verifyReload;
        private static float bootTime, nextStatus;
        private static int Tick => Find.TickManager.TicksGame;
        private static string Case => Cases[caseIndex];

        static Probe()
        {
            if (!GenCommandLine.CommandLineArgPassed("mugirlHuntRegression")) return;
            string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(Comp_WeaponWheel).Assembly.Location), "../..", "TMP"));
            output = Path.GetFullPath(GenFilePaths.SaveDataFolderPath);
            if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            var harmony = new Harmony("mugirl.weapon-wheel.hunt.regression");
            harmony.Patch(AccessTools.Method(typeof(Game), "UpdatePlay"), postfix: new HarmonyMethod(typeof(Probe), nameof(Update)));
            harmony.Patch(AccessTools.Method(typeof(Root), "Update"), postfix: new HarmonyMethod(typeof(Probe), nameof(Status)));
            harmony.Patch(AccessTools.Method(typeof(JobDriver), "TryActuallyStartNextToil"),
                prefix: new HarmonyMethod(typeof(Probe), nameof(EnterToil)), postfix: new HarmonyMethod(typeof(Probe), nameof(ExitToil)));
            harmony.Patch(AccessTools.Method(typeof(Verb), "TryCastNextBurstShot"), postfix: new HarmonyMethod(typeof(Probe), nameof(Shot)));
            harmony.Patch(AccessTools.Method(typeof(Pawn_JobTracker), "EndCurrentJob"), prefix: new HarmonyMethod(typeof(Probe), nameof(JobEnded)));
            bootTime = Time.realtimeSinceStartup;
            Application.runInBackground = true;
            File.WriteAllText(Path.Combine(output, "checks.txt"), "Weapon wheel hunting regression; recursion guard stops at depth 80.\n");
        }

        private static void Note(string text) => File.AppendAllText(Path.Combine(output, "checks.txt"), text + Environment.NewLine);
        private static void Require(bool success, string label)
        {
            if (!success) throw new InvalidOperationException(label + " " + State());
        }
        private static string State() => "case=" + caseIndex + " tick=" + Tick + " primary=" + hunter?.equipment?.Primary?.ThingID
            + " cached=" + hunter?.CurJob?.verbToUse?.EquipmentSource?.ThingID + " job=" + hunter?.CurJob?.def?.defName
            + " pos=" + hunter?.Position + " maxDepth=" + maxDepth;

        private static bool EnterToil(JobDriver __instance, ref bool __state)
        {
            __state = false;
            if (__instance.pawn?.thingIDNumber != hunterId || __instance.job?.def != JobDefOf.Hunt) return true;
            if (cutoff) return false;
            __state = true;
            maxDepth = Math.Max(maxDepth, ++depth);
            if (depth < 80) return true;
            cutoff = true;
            File.WriteAllText(Path.Combine(output, "recursion-stack.txt"), Environment.StackTrace);
            return false;
        }
        private static void ExitToil(bool __state) { if (__state) depth--; }
        private static void JobEnded(Pawn ___pawn, JobCondition condition)
        {
            if (___pawn?.thingIDNumber != hunterId || ___pawn.CurJob?.def != JobDefOf.Hunt || acted) return;
            Note("EARLY_HUNT_END condition=" + condition + " " + State());
            File.AppendAllText(Path.Combine(output, "early-end-stack.txt"), Environment.StackTrace + Environment.NewLine);
        }
        private static void Shot(Verb __instance)
        {
            if (__instance.CasterPawn?.thingIDNumber != hunterId || __instance.LastShotTick != Tick
                || __instance.state != VerbState.Idle || __instance.EquipmentSource == null) return;
            firedWeapons.Add(__instance.EquipmentSource.ThingID);
        }
        private static void Status()
        {
            if (done || output == null || Time.realtimeSinceStartup < nextStatus) return;
            nextStatus = Time.realtimeSinceStartup + 5f;
            File.WriteAllText(Path.Combine(output, "status.txt"), "state=" + Current.ProgramState + " longEvent=" + LongEventHandler.AnyEventNowOrWaiting
                + " elapsed=" + (Time.realtimeSinceStartup - bootTime) + " " + (Find.CurrentMap == null ? "no map" : State()));
            if (Time.realtimeSinceStartup - bootTime > 300f) Finish("FAIL timeout");
        }

        private static void Update()
        {
            if (done || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting || Find.CurrentMap == null) return;
            try
            {
                if (caseIndex < 0) { StartCase(); return; }
                Require(!cutoff, "Recursion guard triggered");
                if (waitingReload)
                {
                    if (ReferenceEquals(Current.Game, savedGame)) return;
                    hunter = Find.CurrentMap.mapPawns.AllPawnsSpawned.Single(p => p.thingIDNumber == hunterId);
                    victim = Find.CurrentMap.mapPawns.AllPawnsSpawned.Single(p => p.thingIDNumber == victimId);
                    wheel = hunter.TryGetComp<Comp_WeaponWheel>();
                    hunt = hunter.CurJob;
                    waitingReload = false;
                    verifyReload = true;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                    return;
                }
                if (verifyReload)
                {
                    if (Tick <= savedTick) { Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast; return; }
                    AssertCurrentHunt();
                    Require(hunt.verbToUse.EquipmentSource.ThingID == expectedWeaponId, "Reload did not repair the cached weapon");
                    Note("RELOADED " + Case + " " + State());
                    verifyReload = false;
                }
                Require(Tick - startTick < 3500, "Case exceeded 3500 ticks");
                if (!acted && Tick - startTick >= 70 && !wheel.IsBusy && caseIndex != 0 && caseIndex != 8)
                {
                    ApplyAction();
                    return;
                }
                if (caseIndex == 8)
                {
                    AssertCurrentHunt();
                    if (firedWeapons.Count >= 2 && wheel.ActiveSlotIndex == 0)
                    {
                        Pass("both weapons fired and returned to slot 0; shots=" + string.Join(",", firedWeapons));
                        return;
                    }
                }
                else if ((caseIndex == 0 || acted) && hunter.stances.curStance is Stance_Warmup)
                {
                    AssertCurrentHunt();
                    Require(hunt.verbToUse.EquipmentSource.ThingID == expectedWeaponId, "Warmup used the wrong weapon");
                    Require(hunt.verbToUse.CanHitTarget(victim), "Did not reposition for the new weapon range");
                    Pass("reached warmup with current weapon; distance=" + hunter.Position.DistanceTo(victim.Position));
                    return;
                }
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            }
            catch (Exception ex) { Note(ex.ToString()); Finish("FAIL"); }
        }

        private static void AssertCurrentHunt()
        {
            Require(ReferenceEquals(hunter.CurJob, hunt) && hunt.def == JobDefOf.Hunt && hunt.targetA.Thing == victim,
                "Hunt or target was replaced");
            Require(hunt.verbToUse == hunter.equipment.PrimaryEq.PrimaryVerb && hunt.verbToUse.IsStillUsableBy(hunter),
                "Hunt retained an unusable or non-primary verb");
        }

        private static void ApplyAction()
        {
            string reason;
            acted = true;
            if (caseIndex == 7)
            {
                Require(wheel.TryDropSlot(0, out reason), "Drop rejected: " + reason);
                Require(hunter.CurJob?.def != JobDefOf.Hunt, "Hunt continued without its weapon");
                Pass("dropping primary safely ended hunt");
                return;
            }
            int source = caseIndex == 4 ? 1 : 0;
            int destination = caseIndex == 4 || caseIndex == 5 ? 2 : 1;
            Require(wheel.TrySwapSlots(source, destination, out reason), "Swap rejected: " + reason);
            if (caseIndex == 5 || caseIndex == 6)
            {
                Require(hunter.CurJob?.def != JobDefOf.Hunt, "Hunt continued with empty/melee primary");
                Pass("invalid hunting primary safely ended hunt");
                return;
            }
            if (caseIndex == 9)
            {
                Require(ReferenceEquals(hunter.CurJob, hunt) && hunt.verbToUse == initialVerb, "Non-hunt job was changed");
                Pass("non-hunt job and its verb were preserved");
                return;
            }
            AssertCurrentHunt();
            expectedWeaponId = hunter.equipment.Primary.ThingID;
            Require((caseIndex == 4) == ReferenceEquals(initialVerb, hunt.verbToUse), "Reserve-only or primary swap changed the wrong reference");
            Note("SWAPPED " + Case + " " + State());
            if (caseIndex >= 10)
            {
                // 构造旧版可写入的存档状态，验证读档归位路径能修复历史遗留引用。
                if (caseIndex == 11) hunt.verbToUse = initialVerb;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                savedTick = Tick;
                savedGame = Current.Game;
                waitingReload = true;
                string name = "HuntWheel-" + Case;
                GameDataSaveLoader.SaveGame(name);
                GameDataSaveLoader.LoadGame(name);
            }
        }

        private static void StartCase()
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Pawn oldHunter = hunter;
            hunter = null;
            hunterId = 0;
            if (oldHunter != null && !oldHunter.Destroyed)
            {
                // 被其他 Pawn 记住的测试角色仍可能进入存档；销毁前清掉冷却中的武器引用。
                oldHunter.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                oldHunter.stances.SetStance(new Stance_Mobile());
                oldHunter.Destroy(DestroyMode.Vanish);
            }
            if (victim != null && !victim.Destroyed) victim.Destroy(DestroyMode.Vanish);
            if (++caseIndex >= Cases.Length) { Finish("PASS all " + passed + " cases"); return; }
            acted = cutoff = waitingReload = verifyReload = false;
            depth = maxDepth = 0;
            firedWeapons.Clear();
            Map map = Find.CurrentMap;
            // 专项测试固定舒适温度，避免随机冷/热地图的 Danger 限制先拒绝狩猎。
            map.Biome.constantOutdoorTemperature = 21f;
            IntVec3 middle = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2 + 60);
            foreach (IntVec3 cell in CellRect.CenteredOn(middle, 40, 4).Cells)
            {
                if (!cell.InBounds(map)) continue;
                foreach (Thing thing in cell.GetThingList(map).ToList())
                    if (thing is Building || thing is Plant) thing.Destroy(DestroyMode.Vanish);
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                map.fogGrid.Unfog(cell);
            }
            // 清掉随机地图的障碍后立即刷新区域，避免仍用旧区域图拒绝射击站位。
            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
            hunter = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("Mugirl_Colony"), Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, mustBeCapableOfViolence: true,
                fixedGender: Gender.Female, fixedBiologicalAge: 24f, fixedChronologicalAge: 24f, forceNoGear: true));
            GenSpawn.Spawn(hunter, middle + new IntVec3(-30, 0, 0), map);
            hunterId = hunter.thingIDNumber;
            hunter.skills.GetSkill(SkillDefOf.Shooting).Level = 10;
            wheel = hunter.TryGetComp<Comp_WeaponWheel>();
            Require(wheel != null && !hunter.WorkTagIsDisabled(WorkTags.Violent), "Invalid generated hunter");
            for (int slot = 0; slot < wheel.MaxSlots; slot++)
            {
                string reason;
                Require(wheel.TryDropSlot(slot, out reason), "Initial cleanup: " + reason);
            }
            AddWeapon(caseIndex == 3 || caseIndex == 8 ? "Gun_Autopistol" : "Gun_BoltActionRifle", 0);
            AddWeapon(caseIndex == 6 ? "MeleeWeapon_Knife" : caseIndex == 2 || caseIndex == 8 || caseIndex >= 10 ? "Gun_Autopistol" : "Gun_BoltActionRifle", 1);
            if (caseIndex == 4) AddWeapon("Gun_Autopistol", 2);
            expectedWeaponId = hunter.equipment.Primary.ThingID;
            victim = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Cow"));
            victimId = victim.thingIDNumber;
            GenSpawn.Spawn(victim, middle + new IntVec3(caseIndex == 8 ? -20 : 30, 0, 0), map);
            victim.SetForbidden(false, false);
            Job wait = JobMaker.MakeJob(JobDefOf.Wait);
            wait.expiryInterval = 10000;
            victim.jobs.StartJob(wait, JobCondition.InterruptForced);
            hunt = caseIndex == 9 ? JobMaker.MakeJob(JobDefOf.Goto, middle + new IntVec3(30, 0, 0)) : JobMaker.MakeJob(JobDefOf.Hunt, victim);
            hunt.ignoreDesignations = true;
            hunt.playerForced = true;
            startTick = Tick;
            Note("FIXTURE " + Case + " targetForbidden=" + victim.IsForbidden(hunter) + " downed=" + victim.Downed
                + " selectedVerb=" + hunter.TryGetAttackVerb(victim)?.EquipmentSource?.ThingID);
            IntVec3 testSpot = victim.Position + new IntVec3(-10, 0, 0);
            Note("POSITION temp=" + hunter.AmbientTemperature
                + " reachable=" + map.reachability.CanReach(hunter.Position, testSpot, PathEndMode.OnCell, TraverseParms.For(hunter, Danger.Some))
                + " reachableDeadly=" + map.reachability.CanReach(hunter.Position, testSpot, PathEndMode.OnCell, TraverseParms.For(hunter, Danger.Deadly))
                + " canHit=" + hunter.equipment.PrimaryEq.PrimaryVerb.CanHitTargetFrom(testSpot, victim));
            hunter.jobs.StartJob(hunt, JobCondition.InterruptForced);
            if (caseIndex == 9) hunt.verbToUse = hunter.equipment.PrimaryEq.PrimaryVerb;
            initialVerb = hunt.verbToUse;
            Require(initialVerb != null, "Job did not acquire its verb");
            Note("START " + Case + " " + State());
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
        }

        private static void AddWeapon(string name, int slot)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamed(name);
            ThingWithComps weapon = (ThingWithComps)ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Steel : null);
            GenSpawn.Spawn(weapon, hunter.Position + new IntVec3(0, 0, 1), hunter.Map);
            string reason;
            Require(wheel.TryAttachGroundWeapon(weapon, slot, out reason), "Attach rejected: " + reason);
        }
        private static void Pass(string detail)
        {
            Require(!cutoff, "Unexpected recursion cutoff");
            Note("PASS " + Case + ": " + detail + " " + State());
            passed++;
            StartCase();
        }
        private static void Finish(string result)
        {
            done = true;
            File.WriteAllText(Path.Combine(output, "complete.txt"), result);
            Note(result);
            Application.Quit();
        }
    }
}
