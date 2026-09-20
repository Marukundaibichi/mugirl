// 仅以 EnableLanceValidation 构建，并在独立 quicktest 地图中运行，不加入存档组件。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Mugirl.Features.Lances;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Mugirl
{
    [HarmonyPatch(typeof(Game), nameof(Game.UpdatePlay))]
    internal static class LanceRuntimeValidation
    {
        // StaticCacheLifecycle: 仅供单次独立验证进程使用，退出时释放，不写入任何存档。
        private static readonly List<Pawn> pawns = new List<Pawn>();
        private static string outputRoot;
        private static int phase;
        private static int failures;
        private static float nextAt;
        private static Pawn liveCharger;
        private static Pawn liveTarget;
        private static IntVec3 validationOrigin;
        private static IntVec3 liveTargetStart;
        private static IntVec3 liveDestination;
        private static int liveStartTick;
        private static bool sawLanceFlyer;
        private static bool liveScreenshotRequested;
        private static Pawn pointCharger;
        private static Pawn pointTarget;
        private static IntVec3 pointOriginalTargetCell;
        private static IntVec3 pointMovedTargetCell;
        private static int pointTargetInjuriesBefore;
        private static float pointTargetHealthBefore;
        private static ThingWithComps pointLance;
        private static int pointLanceHitPointsBefore;
        private static int pointStartTick;
        private static bool sawPointFlyer;

        private static void Postfix()
        {
            if (!GenCommandLine.CommandLineArgPassed("mugirlLanceChecks")
                || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null || phase == 11) return;
            string allowedRoot = Path.GetFullPath(Path.Combine(MugirlMod.ContentRoot, "TMP")) + Path.DirectorySeparatorChar;
            outputRoot = Path.GetFullPath(GenFilePaths.SaveDataFolderPath);
            if (!outputRoot.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase)) return;
            if (Time.realtimeSinceStartup < nextAt) return;
            try
            {
                if (phase != 7 && phase != 9)
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                if (phase == 0)
                {
                    Run();
                    SelectForScreenshot(0);
                }
                else if (phase <= 5)
                {
                    if (phase % 2 == 1)
                        ScreenCapture.CaptureScreenshot(Path.Combine(outputRoot, "lance-" + ((phase + 1) / 2) + ".png"));
                    else
                        SelectForScreenshot(phase / 2);
                }
                else if (phase == 6)
                {
                    StartLiveChargeTest();
                    phase = 7;
                    nextAt = Time.realtimeSinceStartup + 0.02f;
                    return;
                }
                else if (phase == 7)
                {
                    TickLiveChargeTest();
                    return;
                }
                else if (phase == 8)
                {
                    StartPointChargeTest();
                    phase = 9;
                    nextAt = Time.realtimeSinceStartup + 0.02f;
                    return;
                }
                else if (phase == 9)
                {
                    TickPointChargeTest();
                    return;
                }
                else
                {
                    Finish();
                    return;
                }
                phase++;
                nextAt = Time.realtimeSinceStartup + 2f;
            }
            catch (Exception exception)
            {
                Check("runtime exception: " + exception, false);
                Finish();
            }
        }

        private static void Run()
        {
            Map map = Find.CurrentMap;
            PawnKindDef mugirlKind = DefDatabase<PawnKindDef>.GetNamed("Mugirl_Colony");
            string[] names = { "Mugirl_NormanLance", "Mugirl_HeavyKnightLance", "Mugirl_SteamKnightLance" };
            float[] sizes = { 1.35f, 1.55f, 1.65f };
            IntVec3 origin = CellFinder.StandableCellNear(map.Center, map, 20);
            validationOrigin = origin;
            // 可抛弃地图内清出测试走廊，保证目标距离和路径不受随机地形影响。
            foreach (IntVec3 cell in new CellRect(origin.x - 3, origin.z - 3, 33, 12).Cells)
            {
                if (!cell.InBounds(map)) continue;
                foreach (Thing thing in cell.GetThingList(map).ToList())
                    if (thing is Building || thing is Plant) thing.Destroy();
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                map.fogGrid.Unfog(cell);
            }
            map.pathing.RecalculateAllPerceivedPathCosts();
            Faction enemyFaction = Find.FactionManager.AllFactionsListForReading.First(f => !f.IsPlayer && f.HostileTo(Faction.OfPlayer));
            Pawn target = MakePawn(PawnKindDefOf.Colonist, enemyFaction, origin + new IntVec3(18, 0, 0), map);
            Pawn nearTarget = MakePawn(PawnKindDefOf.Colonist, enemyFaction, origin + new IntVec3(5, 0, 3), map);
            target.stances.stunner.StunFor(6000, null);
            nearTarget.stances.stunner.StunFor(6000, null);
            float mugirlChargeSpeedOffset = MoveSpeedOffset(Mugirl_DefOf.Mugirl_Charge);
            ThingDef flyerDef = Mugirl_DefOf.Mugirl_LanceChargeFlyer;
            Check("lance charge uses the dedicated direct PawnFlyer",
                flyerDef?.thingClass == typeof(PawnFlyer_LanceCharge)
                && flyerDef.pawnFlyer?.Worker is PawnFlyerWorker_LanceCharge);
            for (int index = 0; index < names.Length; index++)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamed(names[index]);
                Pawn pawn = MakePawn(mugirlKind, Faction.OfPlayer, origin + new IntVec3(0, 0, index * 2), map);
                pawns.Add(pawn);
                ThingWithComps weapon = Equip(pawn, def);
                CompLanceCharge comp = weapon.TryGetComp<CompLanceCharge>();
                if (index == 0)
                {
                    float snowChargeSpeed = pawn.GetStatValue(StatDefOf.MoveSpeed) + mugirlChargeSpeedOffset;
                    Check("direct lance flight speed exceeds Mugirl charge speed ("
                        + flyerDef.pawnFlyer.flightSpeed + " > " + snowChargeSpeed + ")",
                        flyerDef.pawnFlyer.flightSpeed > snowChargeSpeed);
                }
                Check(names[index] + ": fixture is alive, spawned, hostile and beyond minimum range (distance="
                    + pawn.Position.DistanceTo(target.Position) + ", faction=" + target.Faction?.def.defName + ")",
                    target.Spawned && !target.Dead && target.Map == pawn.Map && pawn.HostileTo(target)
                    && pawn.Position.DistanceTo(target.Position) >= 15f
                    && pawn.Position.DistanceTo(target.Position) <= comp.Props.maximumRange);
                int expected = index == 0 ? 1 : 2;
                Find.Selector.ClearSelection();
                Find.Selector.Select(pawn);
                pawn.drafter.Drafted = false;
                List<Command_ActionWithCooldown> commands = Commands(pawn);
                Check(names[index] + ": undrafted pawn exposes disabled charge buttons", commands.Count == expected && commands.All(c => c.Disabled));
                pawn.drafter.Drafted = true;
                pawn.jobs.StopAll();
                Check(names[index] + ": cleared test corridor is reachable", pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly));
                commands = Commands(pawn);
                Check(names[index] + ": drafted Pawn.GetGizmos exposes exactly " + expected + " enabled charge buttons", commands.Count == expected && commands.All(c => !c.Disabled));
                Check(names[index] + ": command icons loaded", commands.All(c => c.icon != null && c.icon != BaseContent.BadTex));
                Check(names[index] + ": actual weapon graphic scale is " + sizes[index],
                    Mathf.Approximately(weapon.Graphic.drawSize.x, sizes[index]) && Mathf.Approximately(weapon.Graphic.drawSize.y, sizes[index]));
                Check(names[index] + ": Simplified Chinese combat-log tool label is 枪尖",
                    !def.tools.NullOrEmpty() && def.tools[0].label == "枪尖");
                foreach (Command_ActionWithCooldown command in commands)
                {
                    bool point = command.defaultLabel == "Mugirl.Lance.PointCharge.Label".Translate().ToString();
                    LocalTargetInfo groundTarget = new LocalTargetInfo(pawn.Position + new IntVec3(18, 0, 0));
                    LocalTargetInfo outOfRangeTarget = new LocalTargetInfo(pawn.Position + new IntVec3(21, 0, 0));
                    command.action();
                    Check(names[index] + ": button enters targeting", Find.Targeter.IsTargeting);
                    Check(names[index] + ": targeting uses the lance source for standard range UI and invalid icon",
                        Find.Targeter.targetingSource == comp);
                    Check(names[index] + ": maximum charge range is 19.9", Mathf.Approximately(comp.Props.maximumRange, 19.9f));
                    Check(names[index] + ": " + (point ? "point" : "line") + " charge ground targeting rule",
                        comp.targetParams.canTargetLocations == !point && comp.CanHitTarget(groundTarget) == !point);
                    if (point)
                    {
                        Check(names[index] + ": point charge selects a nearby hostile pawn without an old minimum range",
                            Mathf.Approximately(comp.Props.minimumPointRange, 0f)
                            && comp.CanHitTarget(new LocalTargetInfo(nearTarget)));
                    }
                    Check(names[index] + ": targeting rejects cells beyond maximum range", !comp.CanHitTarget(outOfRangeTarget));
                    LocalTargetInfo acceptedTarget = point ? new LocalTargetInfo(target) : groundTarget;
                    Check(names[index] + ": valid " + (point ? "hostile Pawn" : "fixed ground cell") + " is accepted",
                        comp.CanHitTarget(acceptedTarget));
                    Find.Targeter.StopTargeting();
                    Check(names[index] + ": direct charge does not apply the legacy walking-speed Hediff",
                        pawn.health.hediffSet.GetFirstHediffOfDef(Mugirl_DefOf.Mugirl_LanceChargeSpeed) == null);
                }
                pawn.equipment.Remove(weapon);
                Check(names[index] + ": unequipping removes charge buttons", Commands(pawn).Count == 0);
                Equip(pawn, def);
                pawn.Rotation = Rot4.South;
            }
            Pawn human = MakePawn(PawnKindDefOf.Colonist, Faction.OfPlayer, origin + new IntVec3(-2, 0, 0), map);
            human.drafter.Drafted = true;
            Equip(human, DefDatabase<ThingDef>.GetNamed(names[0]));
            Check("ordinary human with Norman lance has no charge buttons", Commands(human).Count == 0);
            Equip(target, DefDatabase<ThingDef>.GetNamed(names[1]));
            Check("hostile pawn has no player charge buttons", Commands(target).Count == 0);
            NeuralArmorRuntimeChecks.Run(pawns[0], target, Check);
        }

        private static void StartLiveChargeTest()
        {
            Map map = Find.CurrentMap;
            Faction enemyFaction = Find.FactionManager.AllFactionsListForReading.First(f => !f.IsPlayer && f.HostileTo(Faction.OfPlayer));
            IntVec3 origin = validationOrigin + new IntVec3(0, 0, 7);
            liveDestination = origin + new IntVec3(18, 0, 0);
            liveCharger = MakePawn(DefDatabase<PawnKindDef>.GetNamed("Mugirl_Colony"), Faction.OfPlayer, origin, map);
            liveTarget = MakePawn(PawnKindDefOf.Colonist, enemyFaction, origin + new IntVec3(9, 0, 0), map);
            liveTargetStart = liveTarget.Position;
            liveTarget.stances.stunner.StunFor(6000, null);
            liveCharger.drafter.Drafted = true;
            ThingWithComps weapon = Equip(liveCharger, DefDatabase<ThingDef>.GetNamed("Mugirl_NormanLance"));
            CompLanceCharge comp = weapon.TryGetComp<CompLanceCharge>();
            AccessTools.Method(typeof(CompLanceCharge), "StartChargeJob").Invoke(
                comp,
                new object[] { liveCharger, new LocalTargetInfo(liveDestination), false });
            sawLanceFlyer = map.listerThings.ThingsOfDef(Mugirl_DefOf.Mugirl_LanceChargeFlyer).Count > 0;
            liveScreenshotRequested = false;
            Check("live line-charge fixture immediately enters direct flight", !liveCharger.Spawned && sawLanceFlyer);
            PawnFlyer_LanceCharge flyer = map.listerThings.ThingsOfDef(Mugirl_DefOf.Mugirl_LanceChargeFlyer)
                .OfType<PawnFlyer_LanceCharge>()
                .FirstOrDefault();
            Check("live line charge exposes a forward lance angle while flying",
                flyer != null && flyer.TryGetLanceAimAngle(liveCharger, weapon, out float aimAngle)
                && Mathf.Abs(Mathf.DeltaAngle(aimAngle, (liveDestination - origin).ToVector3().AngleFlat())) < 0.1f);
            liveStartTick = Find.TickManager.TicksGame;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
        }

        private static void TickLiveChargeTest()
        {
            Map map = Find.CurrentMap;
            List<Thing> flyers = map.listerThings.ThingsOfDef(Mugirl_DefOf.Mugirl_LanceChargeFlyer);
            sawLanceFlyer |= flyers.Count > 0;
            int elapsed = Find.TickManager.TicksGame - liveStartTick;
            if (!liveScreenshotRequested && elapsed >= 10 && flyers.Count > 0)
            {
                PawnFlyer_LanceCharge lanceFlyer = flyers[0] as PawnFlyer_LanceCharge;
                Check("live line charge renders historical Pawn copies instead of relying on ShockwaveFast",
                    lanceFlyer?.AfterimagesDrawnLastFrame >= 4);
                Check("live line charge explicitly draws the lance in its forward pose",
                    lanceFlyer?.ForwardLanceDrawnLastFrame == true);
                Find.CameraDriver.SetRootPosAndSize(flyers[0].DrawPos, 8f);
                ScreenCapture.CaptureScreenshot(Path.Combine(outputRoot, "lance-direct-flight.png"));
                liveScreenshotRequested = true;
            }
            if (elapsed < 180)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                nextAt = Time.realtimeSinceStartup + 0.05f;
                return;
            }

            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Check("live charge spawned the dedicated direct flyer", sawLanceFlyer);
            Check("live charge landed at the selected fixed endpoint",
                liveCharger?.Spawned == true && liveCharger.Position.DistanceTo(liveDestination) <= 1f);
            Check("live line charge hit and knocked the passing enemy away",
                liveTarget != null && !liveTarget.Dead && liveTarget.Position != liveTargetStart
                && liveTarget.health.hediffSet.hediffs.Any(h => h is Hediff_Injury));
            Check("live charge completed without the legacy walking-speed Hediff",
                liveCharger?.health?.hediffSet.GetFirstHediffOfDef(Mugirl_DefOf.Mugirl_LanceChargeSpeed) == null);
            phase = 8;
            nextAt = Time.realtimeSinceStartup + 0.5f;
        }

        private static void StartPointChargeTest()
        {
            Map map = Find.CurrentMap;
            Faction enemyFaction = Find.FactionManager.AllFactionsListForReading.First(f => !f.IsPlayer && f.HostileTo(Faction.OfPlayer));
            IntVec3 origin = validationOrigin + new IntVec3(0, 0, -2);
            pointOriginalTargetCell = origin + new IntVec3(12, 0, 0);
            pointMovedTargetCell = pointOriginalTargetCell + new IntVec3(0, 0, 2);
            pointCharger = MakePawn(DefDatabase<PawnKindDef>.GetNamed("Mugirl_Colony"), Faction.OfPlayer, origin, map);
            pointTarget = MakePawn(PawnKindDefOf.Colonist, enemyFaction, pointOriginalTargetCell, map);
            foreach (Apparel apparel in pointTarget.apparel.WornApparel.ToList())
            {
                pointTarget.apparel.Remove(apparel);
                apparel.Destroy();
            }
            pointTarget.stances.stunner.StunFor(6000, null);
            pointTargetInjuriesBefore = pointTarget.health.hediffSet.hediffs.Count(h => h is Hediff_Injury);
            pointTargetHealthBefore = pointTarget.health.summaryHealth.SummaryHealthPercent;
            pointCharger.drafter.Drafted = true;
            pointLance = Equip(pointCharger, DefDatabase<ThingDef>.GetNamed("Mugirl_SteamKnightLance"));
            pointLanceHitPointsBefore = pointLance.HitPoints;
            CompLanceCharge comp = pointLance.TryGetComp<CompLanceCharge>();
            AccessTools.Method(typeof(CompLanceCharge), "StartChargeJob").Invoke(
                comp,
                new object[] { pointCharger, new LocalTargetInfo(pointTarget), true });
            sawPointFlyer = map.listerThings.ThingsOfDef(Mugirl_DefOf.Mugirl_LanceChargeFlyer).Count > 0;
            Check("live point-charge fixture immediately enters direct flight", !pointCharger.Spawned && sawPointFlyer);

            // 在冲锋者起飞后移动锁定目标，复现旧实现“落地时不再相邻就不结算”。
            pointTarget.DeSpawn(DestroyMode.WillReplace);
            GenSpawn.Spawn(pointTarget, pointMovedTargetCell, map, WipeMode.Vanish);
            pointTarget.stances.stunner.StunFor(6000, null);
            Check("point target moved outside the selected landing cell's adjacency during flight",
                pointTarget.Position == pointMovedTargetCell);
            pointStartTick = Find.TickManager.TicksGame;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
        }

        private static void TickPointChargeTest()
        {
            Map map = Find.CurrentMap;
            sawPointFlyer |= map.listerThings.ThingsOfDef(Mugirl_DefOf.Mugirl_LanceChargeFlyer).Count > 0;
            int elapsed = Find.TickManager.TicksGame - pointStartTick;
            if (elapsed < 180)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                nextAt = Time.realtimeSinceStartup + 0.05f;
                return;
            }

            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            int injuriesAfter = pointTarget?.health?.hediffSet?.hediffs.Count(h => h is Hediff_Injury) ?? 0;
            float healthAfter = pointTarget?.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
            Check("live point charge spawned the dedicated direct flyer", sawPointFlyer);
            Check("live point charge still damages its locked target after that target moves",
                pointTarget != null && (pointTarget.Dead || healthAfter < pointTargetHealthBefore
                    || injuriesAfter > pointTargetInjuriesBefore));
            Check("live point charge spends lance durability exactly once",
                pointLance != null && !pointLance.Destroyed
                && pointLance.HitPoints == pointLanceHitPointsBefore - pointLance.TryGetComp<CompLanceCharge>().Props.pointDurabilityCost);
            Check("live point charge lands beside the originally selected target cell",
                pointCharger?.Spawned == true && pointCharger.Position.AdjacentTo8WayOrInside(pointOriginalTargetCell));
            phase = 10;
            nextAt = Time.realtimeSinceStartup + 0.5f;
        }

        private static Pawn MakePawn(PawnKindDef kind, Faction faction, IntVec3 cell, Map map)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer,
                map.Tile, forceGenerateNewPawn: true, allowDead: false, allowDowned: false, canGeneratePawnRelations: false,
                allowPregnant: false, forceNoIdeo: true, developmentalStages: DevelopmentalStage.Adult));
            GenSpawn.Spawn(pawn, cell, map);
            return pawn;
        }

        private static ThingWithComps Equip(Pawn pawn, ThingDef def)
        {
            foreach (ThingWithComps old in pawn.equipment.AllEquipmentListForReading.ToList()) pawn.equipment.Remove(old);
            ThingWithComps weapon = (ThingWithComps)ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
            pawn.equipment.AddEquipment(weapon);
            return weapon;
        }

        private static List<Command_ActionWithCooldown> Commands(Pawn pawn)
        {
            string point = "Mugirl.Lance.PointCharge.Label".Translate();
            string line = "Mugirl.Lance.LineCharge.Label".Translate();
            return pawn.GetGizmos().OfType<Command_ActionWithCooldown>().Where(c => c.defaultLabel == point || c.defaultLabel == line).ToList();
        }

        private static float MoveSpeedOffset(HediffDef def)
        {
            StatModifier modifier = def?.stages?
                .SelectMany(stage => stage.statOffsets ?? new List<StatModifier>())
                .FirstOrDefault(entry => entry.stat == StatDefOf.MoveSpeed);
            return modifier?.value ?? 0f;
        }

        private static void SelectForScreenshot(int index)
        {
            CameraJumper.TryJumpAndSelect(pawns[index]);
            Find.CameraDriver.SetRootPosAndSize(pawns[index].DrawPos, 6f);
        }

        private static void Check(string label, bool passed)
        {
            if (!passed) failures++;
            string line = (passed ? "PASS " : "FAIL ") + label;
            File.AppendAllText(Path.Combine(outputRoot, "lance-checks.txt"), line + Environment.NewLine);
            Log.Message("[LanceValidation] " + line);
        }

        private static void Finish()
        {
            phase = 11;
            File.WriteAllText(Path.Combine(outputRoot, "lance-complete.txt"),
                (failures == 0 ? "PASS" : "FAIL") + " failures=" + failures + " " + DateTime.UtcNow.ToString("O"));
            Application.Quit();
        }
    }
}
