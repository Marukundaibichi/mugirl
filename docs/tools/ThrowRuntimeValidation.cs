// 仅以 EnableThrowValidation 构建；quicktest 实际生成 Pawn、建筑、山体与投掷控制器。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Mugirl
{
    [HarmonyPatch(typeof(Game), nameof(Game.UpdatePlay))]
    internal static class ThrowRuntimeValidation
    {
        // StaticCacheLifecycle: 单次隔离 quicktest 进程的状态；退出进程即清理，不进入存档。
        private static string outputRoot;
        private static int phase;
        private static int failures;
        private static float nextAt;
        private static Pawn caster;
        private static IntVec3 origin;
        private static Building thrownStool;
        private static Thing_MugirlThrownObject thrownController;
        private static IntVec3 throwDestination;
        private static int throwStartTick;
        private static int throwFlightDurationTicks;
        private static int firstThrowFlightTick = -1;
        private static int expectedThrowImpactTick = -1;
        private static int observedThrowImpactTick = -1;
        private static int impactProgressTicks = -1;
        private static int lastFlightAdvance;
        private static Pawn victim;
        private static IntVec3 victimOrigin;
        private static BodyPartRecord victimHead;
        private static Thing_MugirlDunkProp dunkProp;
        private static Thing_MugirlDunkHead headBall;
        private static FleckDef dunkBloodFleck;
        private static Vector2 expectedHeadDrawSize;
        private static Vector2 expectedHairDrawSize;
        private static int bloodStage;
        private static int severBloodFlecks;
        private static int dribbleBloodFlecks;
        private static int slamBloodFlecks;
        private static IntVec3 dunkDestination;
        private static Vector3 dribbleStart;
        private static int dribbleStartTick;
        private static int dunkStartTick;
        private static bool sawDunkFlyer;
        private static bool checkedDunkImpact;
        private static IntVec3 dunkFlyerStartCell;
        private static int dunkHandFollowSamples;
        private static int dunkFlightHandFollowSamples;
        private static int dunkMissingFlyerSamples;
        private static float dunkLargestHeadFlyerGap;
        private static float dunkFlyerGroundTravel;
        private static int dunkLastFlightTick;
        private static Vector3 dunkLastFlightHeadPos;
        private static float dunkLastFlightStep;
        private static float dunkFlightToSlamStep;
        private static float dunkFlightToSlamZStep;
        private static float dunkSlamFirstStep;
        private static float dunkSlamSecondStep;
        private static float dunkSlamFirstZStep;
        private static float dunkSlamSecondZStep;
        private static int dunkSlamStartTick;
        private static int dunkSlamImpactTick;
        private static float dunkLastHeldFlyerProgress;
        private static float dunkSlamStartFlyerProgress;
        private static float dunkSlamImpactFlyerProgress;
        private static int dunkSlamSamples;
        private static float dunkSlamStartDistance;
        private static float dunkSlamLastDistance;
        private static float dunkSlamContactDistance;
        private static float dunkSlamPathDistance;
        private static float dunkSlamFlyerPathDistance;
        private static Vector3 dunkSlamLastPos;
        private static Vector3 dunkSlamLastFlyerPos;
        private static bool dunkSlamHeadIntact;
        private static bool dunkSlamCasterAirborne;
        private static bool dunkSlamFlyerPresent;
        private static bool dunkSlamNoEarlyBlood;
        private static Vector3 dunkSlamImpactHeadPos;
        private static Vector3 dunkSlamImpactFlyerPos;
        private static float dunkSlamImpactForward;
        private static float dunkSlamImpactScreenBelow;
        private static bool previousDevMode;
        private static Ability dunkAbility;
        private static Pawn jobVictim;
        private static IntVec3 jobVictimOrigin;
        private static Thing_MugirlDunkProp jobProp;
        private static Thing_MugirlDunkHead jobHead;
        private static int jobStartTick;
        private static bool checkedJobRunUp;
        private static bool sawJobHeadPickup;
        private static bool sawJobSequence;
        private static bool sawJobFlyer;

        private static void Postfix()
        {
            if (!GenCommandLine.CommandLineArgPassed("mugirlThrowChecks")
                || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null || phase == 9)
            {
                return;
            }

            string allowedRoot = Path.GetFullPath(Path.Combine(MugirlMod.ContentRoot, "TMP"))
                + Path.DirectorySeparatorChar;
            outputRoot = Path.GetFullPath(GenFilePaths.SaveDataFolderPath);
            if (!outputRoot.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase)
                || Time.realtimeSinceStartup < nextAt)
            {
                return;
            }

            try
            {
                if (phase == 0)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    PrepareFixture();
                    TestWeightAndConnections();
                    phase = 1;
                }
                else if (phase == 1)
                {
                    StartThrow();
                    phase = 2;
                }
                else if (phase == 2)
                {
                    TickThrow();
                    return;
                }
                else if (phase == 3)
                {
                    StartDunk();
                    phase = 4;
                }
                else if (phase == 4)
                {
                    TickDribble();
                    return;
                }
                else if (phase == 5)
                {
                    TickDunk();
                    return;
                }
                else if (phase == 6)
                {
                    StartJobDunk();
                    phase = 7;
                }
                else if (phase == 7)
                {
                    TickJobDunk();
                    return;
                }
                else
                {
                    Finish();
                    return;
                }
                nextAt = Time.realtimeSinceStartup + 0.25f;
            }
            catch (Exception exception)
            {
                Check("runtime exception: " + exception, false);
                Finish();
            }
        }

        private static void PrepareFixture()
        {
            Map map = Find.CurrentMap;
            previousDevMode = Prefs.DevMode;
            origin = CellFinder.StandableCellNear(map.Center, map, 20);
            // 只修改一次性地图上的小块场地，测试墙体连接关系不受地图生成物干扰。
            foreach (IntVec3 cell in new CellRect(origin.x - 5, origin.z - 8, 44, 29).Cells)
            {
                if (!cell.InBounds(map)) continue;
                foreach (Thing thing in cell.GetThingList(map).ToList())
                {
                    if (thing.def.category == ThingCategory.Building)
                    {
                        if (thing.def.destroyable) thing.Destroy();
                        else thing.DeSpawn(DestroyMode.Vanish);
                    }
                    else if (thing is Plant)
                    {
                        thing.Destroy();
                    }
                }
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                map.fogGrid.Unfog(cell);
            }
            map.pathing.RecalculateAllPerceivedPathCosts();
            IntVec3 wallCell = origin + new IntVec3(2, 0, -2);
            IntVec3[] isolatedWallArea =
            {
                wallCell, wallCell + IntVec3.North, wallCell + IntVec3.East,
                wallCell + IntVec3.South, wallCell + IntVec3.West
            };
            Check("isolated wall and stool fixture cells contain no preexisting buildings",
                isolatedWallArea.All(cell => cell.InBounds(map)
                    && !cell.GetThingList(map).Any(thing => thing.def.category == ThingCategory.Building)));
            caster = MakePawn(DefDatabase<PawnKindDef>.GetNamed("Mugirl_Colony"), Faction.OfPlayer, origin, map);
            caster.drafter.Drafted = true;
            dunkBloodFleck = Mugirl_DefOf.Mugirl_DunkBloodSplash;
            Check("throw fixture spawned a Mugirl on a clear map", caster.Spawned && caster.Map == map);
            Check("tintable dunk blood fleck exists for the dunk presentation", dunkBloodFleck != null);
            Check("heavy dunk impact sound resolves from the game SoundDef database",
                Mugirl_DefOf.Pawn_Melee_BigBash_HitPawn != null);

            // 活着的 HAR 雪牛娘有自己的头部网格缩放；只创建临时道具，不摘头或启动灌篮。
            caster.Drawer.renderer.EnsureGraphicsInitialized();
            PawnRenderTree casterTree = caster.Drawer.renderer.renderTree;
            PawnRenderNode casterHeadNode = null;
            bool hasHeadNode = casterTree != null
                && casterTree.TryGetNodeByTag(PawnRenderNodeTagDefOf.Head, out casterHeadNode)
                && casterHeadNode != null;
            Check("live Mugirl render tree exposes its actual head node", hasHeadNode);
            if (hasHeadNode)
            {
                Mesh originalHeadMesh = casterHeadNode.MeshSetFor(caster)?.MeshAt(Rot4.South);
                Thing_MugirlDunkHead previewHead = ThingMaker.MakeThing(Mugirl_DefOf.Mugirl_DunkHead)
                    as Thing_MugirlDunkHead;
                Check("temporary head preview exists without removing the live Mugirl's head", previewHead != null);
                if (previewHead != null && originalHeadMesh != null)
                {
                    Vector3 meshBounds = originalHeadMesh.bounds.size;
                    Vector2 renderedSize = new Vector2(meshBounds.x, meshBounds.z);
                    previewHead.Initialize(caster);
                    Check("held Mugirl head preserves the live HAR head node mesh size (expected="
                        + renderedSize + ", actual=" + previewHead.HeadDrawSize + ")",
                        (previewHead.HeadDrawSize - renderedSize).sqrMagnitude < 0.000001f
                        && caster.health.hediffSet.HasHead && !caster.Dead);
                    Check("live Mugirl head node applies its enlarged HAR mesh size (width="
                        + renderedSize.x + ")", renderedSize.x > 1.5f);
                }
                else
                {
                    Check("live Mugirl head node supplies a renderable mesh", false);
                }
                if (previewHead != null && !previewHead.Destroyed) previewHead.Destroy();
            }
        }

        private static void TestWeightAndConnections()
        {
            Map map = Find.CurrentMap;
            float strength = MugirlThrowUtility.ThrowStrength(caster);
            Check("throw strength is the live CarryingCapacity stat",
                strength > 0f && Mathf.Approximately(strength, caster.GetStatValue(StatDefOf.CarryingCapacity)));

            // 临时改变隔离进程中钢的 Def 重量，让真实物品堆分别等于和超过当前 Pawn 的承重。
            // 两堆先各自生成并调用实际 StatWorker，随后立即还原 Def；正式资源从不受影响。
            StatModifier steelMass = ThingDefOf.Steel.statBases.First(m => m.stat == StatDefOf.Mass);
            float originalMass = steelMass.value;
            Thing equalStack = null;
            Thing heavyStack = null;
            try
            {
                steelMass.value = strength / 2f;
                equalStack = SpawnStack(ThingDefOf.Steel, 2, origin + new IntVec3(2, 0, -5), map);
                heavyStack = SpawnStack(ThingDefOf.Steel, 3, origin + new IntVec3(4, 0, -5), map);
                float equalMass = MugirlThrowUtility.EffectiveMass(equalStack);
                float heavyMass = MugirlThrowUtility.EffectiveMass(heavyStack);
                string reason;
                Check("two-item stack has exactly CarryingCapacity mass (" + equalMass + " / " + strength + ")",
                    Mathf.Abs(equalMass - strength) < 0.01f);
                Check("a stack at the limit is liftable", MugirlThrowUtility.CanPickUp(caster, equalStack, out reason));
                Check("three-item stack exceeds CarryingCapacity (" + heavyMass + " / " + strength + ")",
                    heavyMass > strength);
                Check("the heavier stack is rejected by the live pickup rule",
                    !MugirlThrowUtility.CanPickUp(caster, heavyStack, out reason)
                    && reason == "Mugirl.Throw.TooHeavy".Translate(heavyMass.ToString("0.#"), strength.ToString("0.#")).ToString());
            }
            finally
            {
                if (equalStack != null && !equalStack.Destroyed) equalStack.Destroy();
                if (heavyStack != null && !heavyStack.Destroyed) heavyStack.Destroy();
                steelMass.value = originalMass;
            }

            Building isolated = SpawnWall(origin + new IntVec3(2, 0, -2), map);
            string connectionReason;
            float wallMass = MugirlThrowUtility.EffectiveMass(isolated);
            bool wallAllowed = MugirlThrowUtility.CanPickUp(caster, isolated, out connectionReason);
            Check("isolated plasteel Wall effective mass=" + wallMass + " kg; capacity=" + strength
                + " kg; allowed=" + wallAllowed + "; reason=" + connectionReason,
                Mathf.Approximately(wallMass, isolated.MaxHitPoints * 10f)
                && wallMass > strength && !wallAllowed
                && connectionReason == "Mugirl.Throw.TooHeavy".Translate(wallMass.ToString("0.#"), strength.ToString("0.#")).ToString());
            Building neighbour = SpawnWall(isolated.Position + IntVec3.East, map);
            Check("cardinally connected wall is rejected before weight check",
                !MugirlThrowUtility.CanPickUp(caster, isolated, out connectionReason)
                && connectionReason == "Mugirl.Throw.ConnectedTarget".Translate(isolated.LabelCap).ToString());
            neighbour.Destroy();
            Check("isolated wall remains too heavy after removing its neighbor",
                !MugirlThrowUtility.CanPickUp(caster, isolated, out connectionReason)
                && connectionReason == "Mugirl.Throw.TooHeavy".Translate(wallMass.ToString("0.#"), strength.ToString("0.#")).ToString());
            Thing conduit = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PowerConduit"));
            GenSpawn.Spawn(conduit, isolated.Position, map, WipeMode.Vanish);
            Check("under-wall conduit and wall actually share a cell",
                conduit.Spawned && isolated.Spawned && conduit.Position == isolated.Position);
            Check("under-wall conduit prevents lifting the wall",
                !MugirlThrowUtility.CanPickUp(caster, isolated, out connectionReason)
                && connectionReason == "Mugirl.Throw.ConnectedTarget".Translate(isolated.LabelCap).ToString());
            conduit.Destroy();
            Check("wall remains too heavy after removing its under-wall conduit",
                !MugirlThrowUtility.CanPickUp(caster, isolated, out connectionReason)
                && connectionReason == "Mugirl.Throw.TooHeavy".Translate(wallMass.ToString("0.#"), strength.ToString("0.#")).ToString());

            // 只在隔离 quicktest 中提高 Def 基值，并立即恢复；验证重墙仍受真实承重上限控制。
            StatModifier carryingBase = caster.def.statBases.First(m => m.stat == StatDefOf.CarryingCapacity);
            float originalCarryingBase = carryingBase.value;
            try
            {
                carryingBase.value = wallMass;
                float boostedStrength = MugirlThrowUtility.ThrowStrength(caster);
                Check("isolated Wall becomes liftable above its computed mass (" + wallMass
                    + " / " + boostedStrength + ")",
                    boostedStrength > wallMass
                    && MugirlThrowUtility.CanPickUp(caster, isolated, out connectionReason));
                Building boostedNeighbour = SpawnWall(isolated.Position + IntVec3.East, map);
                Check("connected Wall still rejects at boosted carrying capacity",
                    !MugirlThrowUtility.CanPickUp(caster, isolated, out connectionReason)
                    && connectionReason == "Mugirl.Throw.ConnectedTarget".Translate(isolated.LabelCap).ToString());
                boostedNeighbour.Destroy();
                bool lifted = Thing_MugirlThrownObject.TryCreate(caster, isolated, null);
                Thing_MugirlThrownObject boostedController = MugirlThrowUtility.FindHeldController(caster);
                Check("boosted capacity actually lifts the isolated no-Mass Wall",
                    lifted && boostedController != null && !isolated.Spawned);
                Find.Targeter.StopTargeting();
                Check("cancel restores the lifted Wall before capacity resets",
                    boostedController != null && boostedController.Destroyed && isolated.Spawned);
            }
            finally
            {
                Find.Targeter.StopTargeting();
                carryingBase.value = originalCarryingBase;
            }
            Check("test restores normal carrying capacity",
                Mathf.Approximately(MugirlThrowUtility.ThrowStrength(caster), strength));
            isolated.Destroy();

            Building light = SpawnStool(origin + new IntVec3(2, 0, -2), map);
            Check("explicit-Mass stool is light enough to lift",
                light.def.statBases.StatListContains(StatDefOf.Mass)
                && MugirlThrowUtility.EffectiveMass(light) <= strength
                && MugirlThrowUtility.CanPickUp(caster, light, out connectionReason));
            Building stoolNeighbour = SpawnWall(light.Position + IntVec3.East, map);
            Check("a connected light building is rejected despite being within capacity",
                !MugirlThrowUtility.CanPickUp(caster, light, out connectionReason)
                && connectionReason == "Mugirl.Throw.ConnectedTarget".Translate(light.LabelCap).ToString());
            stoolNeighbour.Destroy();
            Check("light building becomes liftable after removing its neighbor",
                MugirlThrowUtility.CanPickUp(caster, light, out connectionReason));
            bool heldForCancel = Thing_MugirlThrownObject.TryCreate(caster, light, null);
            Thing_MugirlThrownObject cancelController = MugirlThrowUtility.FindHeldController(caster);
            Check("cancel fixture actually lifts the isolated stool", heldForCancel && cancelController != null);
            Find.Targeter.StopTargeting();
            Check("cancel returns the stool without spawning it on the caster",
                cancelController != null && cancelController.Destroyed && light.Spawned
                && light.Position != caster.Position && caster.Position.Standable(map));
            light.Destroy();

            ThingDef mineableDef = DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(def =>
                def.thingClass != null && typeof(Mineable).IsAssignableFrom(def.thingClass)
                && def.category == ThingCategory.Building && def.destroyable
                && !def.statBases.StatListContains(StatDefOf.Mass));
            Check("a loaded Mineable Def without Mass exists", mineableDef != null);
            if (mineableDef == null) return;

            Mineable rock = (Mineable)ThingMaker.MakeThing(mineableDef);
            GenSpawn.Spawn(rock, origin + new IntVec3(6, 0, -2), map, WipeMode.Vanish);
            float fullMass = rock.MaxHitPoints * 10f;
            Check("Mass-less mountain uses MaxHitPoints times ten",
                Mathf.Approximately(MugirlThrowUtility.EffectiveMass(rock), fullMass));
            rock.HitPoints = Math.Max(1, rock.MaxHitPoints / 4);
            Check("damaging a mountain does not reduce its lift mass",
                Mathf.Approximately(MugirlThrowUtility.EffectiveMass(rock), fullMass));
            bool isolatedAllowed = MugirlThrowUtility.CanPickUp(caster, rock, out connectionReason);
            Check("isolated mountain follows its actual mass threshold", isolatedAllowed == (fullMass <= strength));
            Building rockNeighbour = SpawnWall(rock.Position + IntVec3.North, map);
            Check("mountain connected to another building is rejected",
                !MugirlThrowUtility.CanPickUp(caster, rock, out connectionReason)
                && connectionReason == "Mugirl.Throw.ConnectedTarget".Translate(rock.LabelCap).ToString());
            rockNeighbour.Destroy();
            rock.Destroy(DestroyMode.Vanish);

            Mineable granite = (Mineable)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Granite"));
            GenSpawn.Spawn(granite, origin + new IntVec3(9, 0, -2), map, WipeMode.Vanish);
            float graniteMass = MugirlThrowUtility.EffectiveMass(granite);
            bool graniteAllowed = MugirlThrowUtility.CanPickUp(caster, granite, out connectionReason);
            Check("isolated Granite effective mass=" + graniteMass + " kg; capacity=" + strength
                + " kg; allowed=" + graniteAllowed + "; reason=" + connectionReason,
                graniteMass == granite.MaxHitPoints * 10f && graniteMass > strength
                && !graniteAllowed
                && connectionReason == "Mugirl.Throw.TooHeavy".Translate(graniteMass.ToString("0.#"), strength.ToString("0.#")).ToString());
            // 模拟其他模组给天然岩体注入错误的轻 Mass，隔离进程内验证并恢复 Def。
            StatModifier injectedMass = new StatModifier { stat = StatDefOf.Mass, value = 1f };
            granite.def.statBases.Add(injectedMass);
            try
            {
                float injectedEffectiveMass = MugirlThrowUtility.EffectiveMass(granite);
                Check("Granite with explicit Mass=1 retains its mountain floor of " + graniteMass + " kg",
                    granite.def.statBases.StatListContains(StatDefOf.Mass)
                    && Mathf.Approximately(injectedEffectiveMass, graniteMass)
                    && !MugirlThrowUtility.CanPickUp(caster, granite, out connectionReason));
            }
            finally
            {
                granite.def.statBases.Remove(injectedMass);
            }
            granite.Destroy(DestroyMode.Vanish);
        }

        private static void StartThrow()
        {
            Map map = Find.CurrentMap;
            Find.Targeter.StopTargeting();
            IntVec3 pickupCell = origin + IntVec3.East;
            thrownStool = SpawnStool(pickupCell, map);
            throwDestination = origin + new IntVec3(11, 0, 3);
            if (caster.abilities.GetAbility(Mugirl_DefOf.Mugirl_Throw) == null)
            {
                caster.abilities.GainAbility(Mugirl_DefOf.Mugirl_Throw);
            }
            Ability ability = caster.abilities.GetAbility(Mugirl_DefOf.Mugirl_Throw);
            Check("throw controller stays drawable while its payload flies offscreen",
                Mugirl_DefOf.Mugirl_ThrowController.drawOffscreen);
            CompAbilityEffect_PickupThrow pickup = ability?.CompOfType<CompAbilityEffect_PickupThrow>();
            Check("adjacent stool is within the real 1.5-cell ability entry and passes its effect validation",
                pickup != null && caster.Position.DistanceTo(thrownStool.Position) <= ability.def.verbProperties.range
                && pickup.Valid(new LocalTargetInfo(thrownStool)));
            bool created = Thing_MugirlThrownObject.TryCreate(caster, thrownStool, ability);
            thrownController = MugirlThrowUtility.FindHeldController(caster);
            Check("live pickup creates a held controller with the original stool inside",
                created && thrownController != null && thrownController.IsHeld && !thrownStool.Spawned
                && thrownController.SearchableContents.Count == 1
                && thrownController.SearchableContents[0] == thrownStool);
            if (thrownController == null) throw new InvalidOperationException("Throw controller was not created.");
            Check("held object enters the normal targeter", Find.Targeter.IsTargeting
                && Find.Targeter.targetingSource == thrownController);
            Check("throw targeter accepts the clear destination and rejects a distant cell",
                thrownController.CanHitTarget(new LocalTargetInfo(throwDestination))
                && !thrownController.CanHitTarget(new LocalTargetInfo(origin + new IntVec3(40, 0, 3))));
            thrownController.OrderForceTarget(new LocalTargetInfo(throwDestination));
            Find.Targeter.StopTargeting();
            Check("ordering a throw starts flight without releasing the payload",
                !thrownController.IsHeld && !thrownStool.Spawned
                && thrownController.SearchableContents.Count == 1);
            throwStartTick = Find.TickManager.TicksGame;
            FieldInfo flightTicksField = AccessTools.Field(typeof(Thing_MugirlThrownObject), "flightTicks");
            if (flightTicksField == null) throw new InvalidOperationException("Throw flight duration field is missing.");
            throwFlightDurationTicks = (int)flightTicksField.GetValue(thrownController);
            FieldInfo spinField = AccessTools.Field(typeof(Thing_MugirlThrownObject), "SpinDegreesPerTick");
            if (spinField == null) throw new InvalidOperationException("Throw spin field is missing.");
            float degreesPerTick = (float)(spinField.IsLiteral
                ? spinField.GetRawConstantValue() : spinField.GetValue(null));
            Check("ordinary throw completes at least two airborne rotations at 42 degrees per tick",
                Mathf.Abs(degreesPerTick - 42f) < 0.001f
                && throwFlightDurationTicks * degreesPerTick >= 720f);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            CheckFlightRenderInterpolation();
        }

        private static void CheckFlightRenderInterpolation()
        {
            // 同一 Game Tick 内模拟多个 Unity 帧，验证视觉轨迹补帧而不推进实际投掷状态。
            FieldInfo ticksFlyingField = AccessTools.Field(typeof(Thing_MugirlThrownObject), "ticksFlying");
            FieldInfo visualTicksField = AccessTools.Field(typeof(Thing_MugirlThrownObject), "visualTicks");
            FieldInfo visualFrameField = AccessTools.Field(typeof(Thing_MugirlThrownObject), "visualFrame");
            if (ticksFlyingField == null || visualTicksField == null || visualFrameField == null)
            {
                throw new InvalidOperationException("Throw flight interpolation fields are missing.");
            }

            int originalTick = (int)ticksFlyingField.GetValue(thrownController);
            float originalVisualTick = (float)visualTicksField.GetValue(thrownController);
            int originalVisualFrame = (int)visualFrameField.GetValue(thrownController);
            int gameTick = Find.TickManager.TicksGame;
            try
            {
                ticksFlyingField.SetValue(thrownController, 1);
                visualTicksField.SetValue(thrownController, 0f);
                thrownController.UpdateFlightPresentation(0f, 1f, paused: false);
                float startTick = thrownController.FlyingRenderTick;
                Vector3 start = thrownController.DrawPos;

                thrownController.UpdateFlightPresentation(1f / 120f, 1f, paused: false);
                float halfTick = thrownController.FlyingRenderTick;
                Vector3 half = thrownController.DrawPos;

                thrownController.UpdateFlightPresentation(1f / 120f, 1f, paused: false);
                float cappedTick = thrownController.FlyingRenderTick;
                Vector3 capped = thrownController.DrawPos;

                Check("flight visual tick and position advance within one game tick",
                    halfTick > startTick + 0.2f && halfTick < cappedTick - 0.2f
                    && half.x > start.x + 0.01f && capped.x > half.x + 0.01f
                    && Find.TickManager.TicksGame == gameTick
                    && (int)ticksFlyingField.GetValue(thrownController) == 1);
                thrownController.UpdateFlightPresentation(1f, 3f, paused: false);
                Check("flight visual progress never overtakes the logical tick",
                    cappedTick <= 1.001f && thrownController.FlyingRenderTick <= 1.001f);

                visualTicksField.SetValue(thrownController, 0.25f);
                float pausedStartTick = thrownController.FlyingRenderTick;
                Vector3 pausedStart = thrownController.DrawPos;
                thrownController.UpdateFlightPresentation(1f, 3f, paused: true);
                float pausedEndTick = thrownController.FlyingRenderTick;
                Vector3 pausedEnd = thrownController.DrawPos;
                Check("paused flight visual remains frozen despite elapsed real time",
                    Mathf.Abs(pausedEndTick - pausedStartTick) < 0.001f
                    && (pausedEnd - pausedStart).sqrMagnitude < 0.000001f
                    && Find.TickManager.TicksGame == gameTick);
            }
            finally
            {
                ticksFlyingField.SetValue(thrownController, originalTick);
                visualTicksField.SetValue(thrownController, originalVisualTick);
                visualFrameField.SetValue(thrownController, originalVisualFrame);
            }
        }

        internal static void RecordThrowImpact(Thing_MugirlThrownObject controller)
        {
            if (controller == thrownController && observedThrowImpactTick < 0)
            {
                observedThrowImpactTick = Find.TickManager.TicksGame;
                FieldInfo ticksFlyingField = AccessTools.Field(typeof(Thing_MugirlThrownObject), "ticksFlying");
                impactProgressTicks = (int)ticksFlyingField.GetValue(controller);
            }
        }

        internal static void RecordThrowFlightTick(Thing_MugirlThrownObject controller, int delta)
        {
            if (controller != thrownController)
            {
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            if (firstThrowFlightTick < 0) firstThrowFlightTick = currentTick;
            FieldInfo ticksFlyingField = AccessTools.Field(typeof(Thing_MugirlThrownObject), "ticksFlying");
            int priorProgress = (int)ticksFlyingField.GetValue(controller);
            lastFlightAdvance = priorProgress + delta;
            if (expectedThrowImpactTick < 0 && lastFlightAdvance >= throwFlightDurationTicks)
            {
                expectedThrowImpactTick = currentTick;
            }
        }

        private static void TickThrow()
        {
            int elapsed = Find.TickManager.TicksGame - throwStartTick;
            if (elapsed < 150)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                nextAt = Time.realtimeSinceStartup + 0.05f;
                return;
            }

            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Check("flight resolves the original stool; impact may break it (destroyed="
                + thrownStool.Destroyed + ", spawned=" + thrownStool.Spawned + ")",
                thrownController.Destroyed && (thrownStool.Destroyed
                    || (thrownStool.Spawned && thrownStool.Map == Find.CurrentMap)));
            Check("any surviving stool lands near impact, away from pickup",
                thrownStool.Destroyed || (thrownStool.Spawned
                    && thrownStool.Position.DistanceTo(throwDestination) <= 12f
                    && thrownStool.Position != origin + IntVec3.East));
            Check("no held throw controller remains after impact", MugirlThrowUtility.FindHeldController(caster) == null);
            Check("flight visual interpolation preserves the exact logical impact tick (first="
                + firstThrowFlightTick + ", expected=" + expectedThrowImpactTick
                + ", observed=" + observedThrowImpactTick + ", advance=" + lastFlightAdvance
                + ", progress=" + impactProgressTicks + ")",
                observedThrowImpactTick == expectedThrowImpactTick
                && lastFlightAdvance >= throwFlightDurationTicks
                && impactProgressTicks >= throwFlightDurationTicks);
            phase = 3;
            nextAt = Time.realtimeSinceStartup + 0.25f;
        }

        private static void CheckDunkRenderInterpolation()
        {
            FieldInfo stateField = AccessTools.Field(typeof(Thing_MugirlDunkProp), "state");
            FieldInfo stateTicksField = AccessTools.Field(typeof(Thing_MugirlDunkProp), "stateTicks");
            FieldInfo visualTicksField = AccessTools.Field(typeof(Thing_MugirlDunkProp), "visualTicks");
            FieldInfo visualFrameField = AccessTools.Field(typeof(Thing_MugirlDunkProp), "visualFrame");
            FieldInfo slamStartField = AccessTools.Field(typeof(Thing_MugirlDunkProp), "slamStartDrawPos");
            FieldInfo slamAnimatingField = AccessTools.Field(typeof(Thing_MugirlDunkProp), "slamAnimating");
            if (stateField == null || stateTicksField == null || visualTicksField == null || visualFrameField == null
                || slamStartField == null || slamAnimatingField == null)
            {
                throw new InvalidOperationException("Dunk presentation fields are missing.");
            }

            object originalState = stateField.GetValue(dunkProp);
            int originalStateTicks = (int)stateTicksField.GetValue(dunkProp);
            float originalVisualTicks = (float)visualTicksField.GetValue(dunkProp);
            int originalVisualFrame = (int)visualFrameField.GetValue(dunkProp);
            Vector3 originalSlamStart = (Vector3)slamStartField.GetValue(dunkProp);
            bool originalSlamAnimating = (bool)slamAnimatingField.GetValue(dunkProp);
            try
            {
                CheckDunkRenderStage("Spin", stateField, stateTicksField, visualTicksField, visualFrameField);
                CheckDunkRenderStage("Flight", stateField, stateTicksField, visualTicksField, visualFrameField);
                slamStartField.SetValue(dunkProp, dunkProp.DrawPos);
                slamAnimatingField.SetValue(dunkProp, true);
                CheckDunkRenderStage("Slam", stateField, stateTicksField, visualTicksField, visualFrameField);
            }
            finally
            {
                stateField.SetValue(dunkProp, originalState);
                stateTicksField.SetValue(dunkProp, originalStateTicks);
                visualTicksField.SetValue(dunkProp, originalVisualTicks);
                visualFrameField.SetValue(dunkProp, originalVisualFrame);
                slamStartField.SetValue(dunkProp, originalSlamStart);
                slamAnimatingField.SetValue(dunkProp, originalSlamAnimating);
            }
        }

        private static void CheckDunkRenderStage(string stage, FieldInfo stateField,
            FieldInfo stateTicksField, FieldInfo visualTicksField, FieldInfo visualFrameField)
        {
            // 在同一 Game Tick 内人为推进两个渲染半帧，避免受 quicktest 实际 FPS 影响。
            stateField.SetValue(dunkProp, Enum.Parse(stateField.FieldType, stage));
            stateTicksField.SetValue(dunkProp, 1);
            visualTicksField.SetValue(dunkProp, 0f);
            visualFrameField.SetValue(dunkProp, -1);
            int gameTick = Find.TickManager.TicksGame;

            float startTick = dunkProp.RenderTick;
            Vector3 startPos = dunkProp.DrawPos;
            float startAngle = dunkProp.RenderSpinDegrees;
            dunkProp.UpdateDunkPresentation(1f / 120f, 1f, paused: false);
            float halfTick = dunkProp.RenderTick;
            Vector3 halfPos = dunkProp.DrawPos;
            float halfAngle = dunkProp.RenderSpinDegrees;
            dunkProp.UpdateDunkPresentation(1f / 120f, 1f, paused: false);
            float endTick = dunkProp.RenderTick;
            Vector3 endPos = dunkProp.DrawPos;
            float endAngle = dunkProp.RenderSpinDegrees;

            float firstTurn = Mathf.DeltaAngle(startAngle, halfAngle);
            float secondTurn = Mathf.DeltaAngle(halfAngle, endAngle);
            Check("dunk " + stage + " head moves and turns 46 degrees across one game tick",
                Mathf.Abs(startTick) < 0.001f
                && Mathf.Abs(halfTick - 0.5f) < 0.01f
                && Mathf.Abs(endTick - 1f) < 0.01f
                && (halfPos - startPos).sqrMagnitude > 0.000001f
                && (endPos - halfPos).sqrMagnitude > 0.000001f
                && Mathf.Abs(firstTurn - 23f) < 0.05f
                && Mathf.Abs(secondTurn - 23f) < 0.05f
                && Find.TickManager.TicksGame == gameTick
                && (int)stateTicksField.GetValue(dunkProp) == 1);

            dunkProp.UpdateDunkPresentation(1f, 3f, paused: false);
            Check("dunk " + stage + " visual progress cannot pass its logical tick",
                dunkProp.RenderTick <= 1.001f
                && (int)stateTicksField.GetValue(dunkProp) == 1
                && Find.TickManager.TicksGame == gameTick);

            visualTicksField.SetValue(dunkProp, 0.25f);
            float pausedTick = dunkProp.RenderTick;
            Vector3 pausedPos = dunkProp.DrawPos;
            float pausedAngle = dunkProp.RenderSpinDegrees;
            dunkProp.UpdateDunkPresentation(1f, 3f, paused: true);
            Check("paused dunk " + stage + " head position and angle remain frozen",
                Mathf.Abs(dunkProp.RenderTick - pausedTick) < 0.001f
                && (dunkProp.DrawPos - pausedPos).sqrMagnitude < 0.000001f
                && Mathf.Abs(Mathf.DeltaAngle(pausedAngle, dunkProp.RenderSpinDegrees)) < 0.001f
                && Find.TickManager.TicksGame == gameTick);
        }

        private static void CheckDunkSlamKickoff(Thing_MugirlDunkProp controller)
        {
            FieldInfo stateTicksField = AccessTools.Field(typeof(Thing_MugirlDunkProp), "stateTicks");
            FieldInfo visualTicksField = AccessTools.Field(typeof(Thing_MugirlDunkProp), "visualTicks");
            if (stateTicksField == null || visualTicksField == null)
            {
                Check("slam kickoff presentation fields exist", false);
                return;
            }

            int logicalTick = (int)stateTicksField.GetValue(controller);
            float originalVisualTick = (float)visualTicksField.GetValue(controller);
            int gameTick = Find.TickManager.TicksGame;
            Vector3 start = controller.DrawPos;
            Vector3 afterRunning = start;
            bool pausedAtStart = false;
            bool pausedAfterRunning = false;
            try
            {
                controller.UpdateDunkPresentation(1f / 60f, 1f, paused: true);
                pausedAtStart = Mathf.Abs(controller.RenderTick - originalVisualTick) < 0.001f
                    && (controller.DrawPos - start).sqrMagnitude < 0.000001f;
                controller.UpdateDunkPresentation(1f / 60f, 1f, paused: false);
                afterRunning = controller.DrawPos;
                float runningTick = controller.RenderTick;
                controller.UpdateDunkPresentation(1f / 60f, 1f, paused: true);
                pausedAfterRunning = Mathf.Abs(controller.RenderTick - runningTick) < 0.001f
                    && (controller.DrawPos - afterRunning).sqrMagnitude < 0.000001f;
            }
            finally
            {
                visualTicksField.SetValue(controller, originalVisualTick);
            }

            Check("slam head moves in its first render frame, stays airborne, and pauses cleanly "
                + "(logicalTick=" + logicalTick + ", start=" + start
                + ", after=" + afterRunning + ")",
                logicalTick == 0 && Mathf.Abs(originalVisualTick) < 0.001f
                && HorizontalDistance(afterRunning, start) > 0.01f
                && DunkDistanceToBasket(afterRunning) > 0.3f
                && pausedAtStart && pausedAfterRunning
                && Find.TickManager.TicksGame == gameTick);
        }

        private static void StartDunk()
        {
            Map map = Find.CurrentMap;
            victimOrigin = origin + new IntVec3(2, 0, 11);
            Faction enemy = Find.FactionManager.AllFactionsListForReading.First(f => !f.IsPlayer && f.HostileTo(Faction.OfPlayer));
            victim = MakePawn(PawnKindDefOf.Colonist, enemy, victimOrigin, map);
            victim.stances.stunner.StunFor(6000, null);
            if (caster.abilities.GetAbility(Mugirl_DefOf.Mugirl_Dunk) == null)
            {
                caster.abilities.GainAbility(Mugirl_DefOf.Mugirl_Dunk);
            }
            Ability ability = caster.abilities.GetAbility(Mugirl_DefOf.Mugirl_Dunk);
            dunkAbility = ability;
            CompAbilityEffect_Dunk comp = ability?.CompOfType<CompAbilityEffect_Dunk>();
            Check("the Mugirl has the live dunk ability and its effect comp", ability != null && comp != null);
            if (comp == null) throw new InvalidOperationException("Dunk ability comp was not created.");

            previousDevMode = Prefs.DevMode;
            try
            {
                Prefs.DevMode = false;
                BodyPartRecord head;
                string reason;
                Check("normal mode hides and disallows the dunk comp",
                    comp.ShouldHideGizmo && !comp.CanCast && !ability.GizmosVisible()
                    && !MugirlDunkUtility.CanDunk(caster, victim, out head, out reason));
                Check("normal mode exposes no dunk command",
                    !HasDunkGizmo(caster, Mugirl_DefOf.Mugirl_Dunk));
                Check("normal mode rejects the victim at the live effect validation entry",
                    !comp.Valid(new LocalTargetInfo(victim)));
                Prefs.DevMode = true;
                Check("developer mode reveals and enables the dunk comp",
                    !comp.ShouldHideGizmo && comp.CanCast && ability.GizmosVisible()
                    && MugirlDunkUtility.CanDunk(caster, victim, out victimHead, out reason));
                Check("developer mode exposes the dunk command",
                    HasDunkGizmo(caster, Mugirl_DefOf.Mugirl_Dunk));
                Check("developer mode accepts the victim at the live effect validation entry",
                    comp.Valid(new LocalTargetInfo(victim)));
                Check("first stage accepts human Pawn; second stage selects a location",
                    Mugirl_DefOf.Mugirl_Dunk.verbProperties.targetParams.canTargetPawns
                    && !Mugirl_DefOf.Mugirl_Dunk.verbProperties.targetParams.canTargetLocations
                    && comp.targetParams.canTargetLocations && !comp.targetParams.canTargetPawns);
                IntVec3 jobDestination = origin + new IntVec3(12, 0, 11);
                Job job = ability.GetJob(new LocalTargetInfo(victim), new LocalTargetInfo(jobDestination));
                Check("dunk ability builds the dedicated job with victim, destination and ability",
                    job != null && job.def == Mugirl_DefOf.Job_MugirlDunk
                    && job.GetTarget(TargetIndex.A).Thing == victim
                    && job.GetTarget(TargetIndex.B).Cell == jobDestination && job.ability == ability);
                CheckSealedDunkLanding(map);

                Mesh headMesh = HumanlikeMeshPoolUtility.GetHumanlikeHeadSetForPawn(victim).MeshAt(Rot4.South);
                Vector3 headBounds = headMesh.bounds.size;
                expectedHeadDrawSize = new Vector2(headBounds.x, headBounds.z);
                Mesh hairMesh = victim.story.hairDef == null || victim.story.hairDef.noGraphic
                    ? null : HumanlikeMeshPoolUtility.GetHumanlikeHairSetForPawn(victim).MeshAt(Rot4.South);
                Vector3 hairBounds = hairMesh != null ? hairMesh.bounds.size : Vector3.zero;
                expectedHairDrawSize = new Vector2(hairBounds.x, hairBounds.z);

                bloodStage = 1;
                bool created;
                try
                {
                    created = Thing_MugirlDunkProp.TryCreate(caster, victim, ability, out dunkProp);
                }
                finally
                {
                    bloodStage = 0;
                }
                headBall = dunkProp?.SearchableContents.Count > 0
                    ? dunkProp.SearchableContents[0] as Thing_MugirlDunkHead : null;
                Check("dunk removes the actual head and retains the body as a corpse at its origin",
                    created && victimHead != null && victim.Dead
                    && victim.health.hediffSet.PartIsMissing(victimHead)
                    && victim.Corpse != null && victim.Corpse.Spawned
                    && victim.Corpse.Position == victimOrigin);
                Check("the detached head is the controller's sole basketball payload",
                    dunkProp != null && dunkProp.HasPayload && headBall != null
                    && !headBall.Spawned && dunkProp.SearchableContents.Count == 1);
                Check("the held head uses the victim's original head mesh size (expected="
                    + expectedHeadDrawSize + ", actual=" + headBall?.HeadDrawSize + ")",
                    headBall != null && (headBall.HeadDrawSize - expectedHeadDrawSize).sqrMagnitude < 0.000001f);
                Check("the held hair uses the victim's original hair mesh size (expected="
                    + expectedHairDrawSize + ", actual=" + headBall?.HairDrawSize + ")",
                    headBall != null && (headBall.HairDrawSize - expectedHairDrawSize).sqrMagnitude < 0.000001f);
                Check("decapitation emits visible blood flecks near the victim", severBloodFlecks > 0);
                Check("a headless or dead victim cannot be selected again",
                    !MugirlDunkUtility.CanDunk(caster, victim, out head, out reason));
                if (dunkProp == null) throw new InvalidOperationException("Dunk controller was not created.");
                dribbleStart = dunkProp.DrawPos;
                dunkProp.NotifyRunning();
                bloodStage = 2;
                dribbleStartTick = Find.TickManager.TicksGame;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            }
            catch
            {
                Prefs.DevMode = previousDevMode;
                throw;
            }
        }

        private static void CheckSealedDunkLanding(Map map)
        {
            // 封住搜索半径内的每个候选落点，确认失败时不会退回施术者脚下原地起跳。
            IntVec3 center = origin + new IntVec3(18, 0, -3);
            List<Building> temporaryWalls = new List<Building>();
            int count = GenRadial.NumCellsInRadius(2.4f);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    IntVec3 cell = center + GenRadial.RadialPattern[i];
                    if (cell != center && cell.InBounds(map))
                    {
                        temporaryWalls.Add(SpawnWall(cell, map));
                    }
                }

                bool everyCandidateBlocked = true;
                for (int i = 0; i < count; i++)
                {
                    IntVec3 cell = center + GenRadial.RadialPattern[i];
                    if (cell != center && JumpUtility.ValidJumpTarget(caster, map, cell))
                    {
                        everyCandidateBlocked = false;
                        break;
                    }
                }

                bool found = Thing_MugirlDunkProp.TryFindLandingCell(caster, center, out IntVec3 landing);
                Check("a basket enclosed by temporary walls has no landing cell and never returns takeoff "
                    + "(walls=" + temporaryWalls.Count + ", landing=" + landing + ")",
                    temporaryWalls.Count == count - 1 && everyCandidateBlocked
                    && !found && landing == IntVec3.Invalid && landing != caster.Position);
            }
            finally
            {
                foreach (Building wall in temporaryWalls)
                {
                    if (wall != null && !wall.Destroyed)
                    {
                        wall.Destroy(DestroyMode.Vanish);
                    }
                }
                map.pathing.RecalculateAllPerceivedPathCosts();
            }
        }

        private static void TickDribble()
        {
            if (Find.TickManager.TicksGame - dribbleStartTick < 45)
            {
                // 连续跑动跨过完整拍球周期，避免随机起始 tick 错过触地帧。
                dunkProp.NotifyRunning();
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                nextAt = Time.realtimeSinceStartup;
                return;
            }

            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            bloodStage = 0;
            Check("head remains held while its actual draw position bounces during dribbling",
                dunkProp.HasPayload && !dunkProp.SequenceStarted
                && (dunkProp.DrawPos - dribbleStart).sqrMagnitude > 0.0001f);
            Check("running dribble emits visible blood flecks near the caster", dribbleBloodFlecks > 0);
            dunkDestination = origin + new IntVec3(12, 0, 11);
            checkedDunkImpact = false;
            dunkFlyerStartCell = caster.Position;
            dunkHandFollowSamples = 0;
            dunkFlightHandFollowSamples = 0;
            dunkMissingFlyerSamples = 0;
            dunkLargestHeadFlyerGap = 0f;
            dunkFlyerGroundTravel = 0f;
            dunkLastFlightTick = -1;
            dunkLastFlightHeadPos = Vector3.zero;
            dunkLastFlightStep = -1f;
            dunkFlightToSlamStep = -1f;
            dunkFlightToSlamZStep = float.NaN;
            dunkSlamFirstStep = -1f;
            dunkSlamSecondStep = -1f;
            dunkSlamFirstZStep = float.NaN;
            dunkSlamSecondZStep = float.NaN;
            dunkSlamStartTick = -1;
            dunkSlamImpactTick = -1;
            dunkLastHeldFlyerProgress = -1f;
            dunkSlamStartFlyerProgress = -1f;
            dunkSlamImpactFlyerProgress = -1f;
            dunkSlamSamples = 0;
            dunkSlamStartDistance = -1f;
            dunkSlamLastDistance = -1f;
            dunkSlamContactDistance = -1f;
            dunkSlamPathDistance = 0f;
            dunkSlamFlyerPathDistance = 0f;
            dunkSlamLastPos = Vector3.zero;
            dunkSlamLastFlyerPos = Vector3.zero;
            dunkSlamHeadIntact = true;
            dunkSlamCasterAirborne = true;
            dunkSlamFlyerPresent = true;
            dunkSlamNoEarlyBlood = true;
            dunkSlamImpactHeadPos = Vector3.zero;
            dunkSlamImpactFlyerPos = Vector3.zero;
            dunkSlamImpactForward = float.NaN;
            dunkSlamImpactScreenBelow = float.NaN;
            bool started = dunkProp.BeginDunk(dunkDestination);
            sawDunkFlyer = Find.CurrentMap.listerThings.ThingsOfDef(Mugirl_DefOf.Mugirl_DunkFlyer).Count > 0;
            Check("dunk begins its spin and launches the dedicated PawnFlyer",
                started && dunkProp.SequenceStarted && sawDunkFlyer && !caster.Spawned);
            if (!started) throw new InvalidOperationException("Dunk sequence did not start.");
            IntVec3 actualLanding = (IntVec3)AccessTools.Field(typeof(Thing_MugirlDunkProp), "landingCell")
                .GetValue(dunkProp);
            Check("dunk flyer lands north of the basket so the head hits in front of its south-facing body "
                + "(start=" + dunkFlyerStartCell + ", landing=" + actualLanding
                + ", basket=" + dunkDestination + ")",
                actualLanding != dunkFlyerStartCell
                && actualLanding.DistanceTo(dunkDestination) <= 2.5f
                && dunkDestination.z < actualLanding.z);
            CheckDunkRenderInterpolation();
            bloodStage = 3;
            dunkStartTick = Find.TickManager.TicksGame;
            phase = 5;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            nextAt = Time.realtimeSinceStartup + 0.05f;
        }

        private static void TickDunk()
        {
            int elapsed = Find.TickManager.TicksGame - dunkStartTick;
            sawDunkFlyer |= Find.CurrentMap.listerThings.ThingsOfDef(Mugirl_DefOf.Mugirl_DunkFlyer).Count > 0;
            // 长距离跃迁由 PawnFlyer 的真实飞行时长决定；头先触地，仍需等待施术者落地及 90 tick 清理。
            if (elapsed < 360 && !dunkProp.Destroyed)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                nextAt = Time.realtimeSinceStartup + 0.05f;
                return;
            }

            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Check("spin and flight keep the detached head close to the visible flyer's hand "
                + "(samples=" + dunkHandFollowSamples + ", flyerAbsent=" + dunkMissingFlyerSamples
                + ", largestGap=" + dunkLargestHeadFlyerGap.ToString("0.00") + " cells)",
                dunkHandFollowSamples >= 8 && dunkMissingFlyerSamples == 0
                && dunkLargestHeadFlyerGap <= 1.9f);
            Check("head-to-hand samples cover a moving flyer during flight "
                + "(flightSamples=" + dunkFlightHandFollowSamples
                + ", groundTravel=" + dunkFlyerGroundTravel.ToString("0.00") + " cells)",
                dunkFlightHandFollowSamples >= 8 && dunkFlyerGroundTravel >= 2f);
            Check("head starts its slam as the flyer crosses the apex "
                + "(lastHeldProgress=" + dunkLastHeldFlyerProgress.ToString("0.000")
                + ", startProgress=" + dunkSlamStartFlyerProgress.ToString("0.000")
                + ", impactProgress=" + dunkSlamImpactFlyerProgress.ToString("0.000") + ")",
                dunkLastHeldFlyerProgress >= 0f && dunkLastHeldFlyerProgress < 0.5f
                && dunkSlamStartFlyerProgress >= 0.5f && dunkSlamStartFlyerProgress <= 0.65f
                && dunkSlamStartFlyerProgress - dunkLastHeldFlyerProgress <= 0.15f
                && dunkSlamImpactFlyerProgress > dunkSlamStartFlyerProgress
                && dunkSlamImpactFlyerProgress < 1f);
            Check("flight-to-slam head motion is sampled across the state boundary "
                + "(lastFlightTick=" + dunkLastFlightTick
                + ", slamStartTick=" + dunkSlamStartTick
                + ", lastFlightStep=" + dunkLastFlightStep.ToString("0.000")
                + ", boundaryStep=" + dunkFlightToSlamStep.ToString("0.000")
                + ", boundaryZ=" + dunkFlightToSlamZStep.ToString("0.000")
                + ", firstStep=" + dunkSlamFirstStep.ToString("0.000")
                + ", firstZ=" + dunkSlamFirstZStep.ToString("0.000")
                + ", secondStep=" + dunkSlamSecondStep.ToString("0.000")
                + ", secondZ=" + dunkSlamSecondZStep.ToString("0.000") + ")",
                dunkLastFlightTick >= 0 && dunkSlamStartTick > dunkLastFlightTick
                && dunkSlamFirstStep >= 0f && dunkSlamSecondStep >= 0f);
            Check("descent dunk visibly drives the intact head down faster than the flyer "
                + "(samples=" + dunkSlamSamples + ", startGap=" + dunkSlamStartDistance.ToString("0.00")
                + ", lastGap=" + dunkSlamLastDistance.ToString("0.00")
                + ", headPath=" + dunkSlamPathDistance.ToString("0.00")
                + ", flyerPath=" + dunkSlamFlyerPathDistance.ToString("0.00")
                + ", contactGap=" + dunkSlamContactDistance.ToString("0.00") + " cells)",
                dunkSlamSamples >= 3 && dunkSlamPathDistance >= 0.45f
                && dunkSlamContactDistance <= 0.3f
                && dunkSlamLastDistance <= 1.5f
                && dunkSlamPathDistance > dunkSlamFlyerPathDistance * 1.25f
                && dunkSlamHeadIntact && dunkSlamCasterAirborne && dunkSlamFlyerPresent);
            Check("dunk head hits below the airborne caster's screen position "
                + "(head=" + dunkSlamImpactHeadPos
                + ", flyer=" + dunkSlamImpactFlyerPos
                + ", forward=" + dunkSlamImpactForward.ToString("0.000")
                + ", screenBelow=" + dunkSlamImpactScreenBelow.ToString("0.000") + ")",
                !float.IsNaN(dunkSlamImpactForward)
                && dunkSlamImpactScreenBelow > 0f);
            Check("dunk impact cues wait for the final contact tick "
                + "(slamStart=" + dunkSlamStartTick + ", impact=" + dunkSlamImpactTick + ")",
                dunkSlamStartTick >= 0 && dunkSlamImpactTick - dunkSlamStartTick >= 10
                && dunkSlamNoEarlyBlood);
            Check("dunk impact and cleanup finish before the runtime timeout",
                checkedDunkImpact && dunkProp.Destroyed);
            Check("dunk flyer lands its living caster", caster.Spawned && !caster.Dead);
            Check("dunk used the dedicated flyer and leaves no controller after its finish lifetime",
                sawDunkFlyer && dunkProp.Destroyed && MugirlThrowUtility.FindDunkController(caster) == null);
            phase = 6;
            nextAt = Time.realtimeSinceStartup + 0.1f;
        }

        internal static void RecordDunkImpact(Thing_MugirlDunkProp controller)
        {
            if (controller != dunkProp || checkedDunkImpact)
            {
                return;
            }

            checkedDunkImpact = true;
            bloodStage = 0;
            Check("dunk shatters the detached head in the impact tick",
                controller.Completed && !controller.HasPayload
                && headBall != null && headBall.Destroyed);
            Check("head hits and shatters while the caster is still descending",
                !caster.Spawned && ActiveDunkFlyer() != null);
            Check("dunk impact emits visible blood flecks at the basket",
                slamBloodFlecks > 0);
            Check("the decapitated corpse remains at the original cell after impact",
                victim.Corpse != null && !victim.Corpse.Destroyed
                && victim.Corpse.Spawned && victim.Corpse.Position == victimOrigin);
        }

        internal static void RecordDunkImpactBefore(Thing_MugirlDunkProp controller)
        {
            if (controller != dunkProp || checkedDunkImpact)
            {
                return;
            }

            dunkSlamImpactTick = Find.TickManager.TicksGame;
            dunkSlamHeadIntact &= controller.HasPayload && headBall != null && !headBall.Destroyed;
            PawnFlyer flyer = ActiveDunkFlyer();
            dunkSlamCasterAirborne &= !caster.Spawned;
            dunkSlamFlyerPresent &= flyer != null;
            if (flyer is PawnFlyer_MugirlDunk impactFlyer)
            {
                dunkSlamImpactFlyerProgress = impactFlyer.AdjustedFlightProgress;
            }
            dunkSlamNoEarlyBlood &= slamBloodFlecks == 0;
            dunkSlamContactDistance = DunkDistanceToBasket(controller);
            Vector3 contactPos = DunkPositionAtLogicalTick(controller);
            dunkSlamImpactHeadPos = contactPos;
            dunkSlamPathDistance += HorizontalDistance(contactPos, dunkSlamLastPos);
            if (flyer != null)
            {
                Vector3 flyerPos = flyer.DrawPos;
                dunkSlamImpactFlyerPos = flyerPos;
                dunkSlamFlyerPathDistance += HorizontalDistance(flyerPos, dunkSlamLastFlyerPos);
                Vector3 travel = (dunkDestination - dunkFlyerStartCell).ToVector3();
                float length = Mathf.Sqrt(travel.x * travel.x + travel.z * travel.z);
                if (length > 0.001f)
                {
                    dunkSlamImpactForward = ((contactPos.x - flyerPos.x) * travel.x
                        + (contactPos.z - flyerPos.z) * travel.z) / length;
                }
                // 游戏地面坐标 z 与跳跃绘制高度共用屏幕纵向；正值表示头在角色下方。
                dunkSlamImpactScreenBelow = flyerPos.z - contactPos.z;
            }
        }

        internal static void RecordDunkSlam(Thing_MugirlDunkProp controller)
        {
            if (controller != dunkProp || checkedDunkImpact || controller.Destroyed)
            {
                return;
            }

            FieldInfo stateField = AccessTools.Field(typeof(Thing_MugirlDunkProp), "state");
            if (stateField?.GetValue(controller)?.ToString() != "Slam")
            {
                return;
            }

            int tick = Find.TickManager.TicksGame;
            PawnFlyer flyer = ActiveDunkFlyer();
            if (dunkSlamStartTick < 0)
            {
                dunkSlamStartTick = tick;
                if (flyer is PawnFlyer_MugirlDunk startFlyer && startFlyer.IsDescending)
                {
                    dunkSlamStartFlyerProgress = startFlyer.AdjustedFlightProgress;
                }
                CheckDunkSlamKickoff(controller);
            }

            Vector3 pos = DunkPositionAtLogicalTick(controller);
            float distance = DunkDistanceToBasket(pos);
            if (dunkSlamSamples == 0)
            {
                dunkSlamStartDistance = distance;
                if (dunkLastFlightTick >= 0)
                {
                    dunkFlightToSlamStep = HorizontalDistance(pos, dunkLastFlightHeadPos);
                    dunkFlightToSlamZStep = pos.z - dunkLastFlightHeadPos.z;
                }
            }
            else
            {
                float step = HorizontalDistance(pos, dunkSlamLastPos);
                if (dunkSlamSamples == 1)
                {
                    dunkSlamFirstStep = step;
                    dunkSlamFirstZStep = pos.z - dunkSlamLastPos.z;
                }
                else if (dunkSlamSamples == 2)
                {
                    dunkSlamSecondStep = step;
                    dunkSlamSecondZStep = pos.z - dunkSlamLastPos.z;
                }
                dunkSlamPathDistance += step;
                if (flyer != null)
                {
                    dunkSlamFlyerPathDistance += HorizontalDistance(flyer.DrawPos, dunkSlamLastFlyerPos);
                }
            }
            dunkSlamLastPos = pos;
            if (flyer != null)
            {
                dunkSlamLastFlyerPos = flyer.DrawPos;
            }
            dunkSlamLastDistance = distance;
            dunkSlamSamples++;
            dunkSlamHeadIntact &= controller.HasPayload && headBall != null && !headBall.Destroyed;
            dunkSlamCasterAirborne &= !caster.Spawned;
            dunkSlamFlyerPresent &= flyer != null;
            dunkSlamNoEarlyBlood &= slamBloodFlecks == 0;
        }

        private static PawnFlyer ActiveDunkFlyer()
        {
            return Find.CurrentMap?.listerThings.ThingsOfDef(Mugirl_DefOf.Mugirl_DunkFlyer)
                .OfType<PawnFlyer>().FirstOrDefault(candidate => candidate.FlyingPawn == caster);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static float DunkDistanceToBasket(Thing_MugirlDunkProp controller)
        {
            return DunkDistanceToBasket(DunkPositionAtLogicalTick(controller));
        }

        private static float DunkDistanceToBasket(Vector3 pos)
        {
            Vector3 basket = dunkDestination.ToVector3Shifted();
            float dx = pos.x - basket.x;
            float dz = pos.z - basket.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static Vector3 DunkPositionAtLogicalTick(Thing_MugirlDunkProp controller)
        {
            FieldInfo stateTicksField = AccessTools.Field(typeof(Thing_MugirlDunkProp), "stateTicks");
            FieldInfo visualTicksField = AccessTools.Field(typeof(Thing_MugirlDunkProp), "visualTicks");
            if (stateTicksField == null || visualTicksField == null)
            {
                return new Vector3(float.PositiveInfinity, 0f, float.PositiveInfinity);
            }

            float originalVisualTicks = (float)visualTicksField.GetValue(controller);
            Vector3 pos;
            try
            {
                visualTicksField.SetValue(controller, (float)(int)stateTicksField.GetValue(controller));
                pos = controller.DrawPos;
            }
            finally
            {
                visualTicksField.SetValue(controller, originalVisualTicks);
            }

            return pos;
        }

        internal static void RecordDunkHandFollow(Thing_MugirlDunkProp controller)
        {
            if (controller != dunkProp || checkedDunkImpact || controller.Destroyed || !controller.HasPayload)
            {
                return;
            }

            FieldInfo stateField = AccessTools.Field(typeof(Thing_MugirlDunkProp), "state");
            string stage = stateField?.GetValue(controller)?.ToString();
            if (stage != "Spin" && stage != "Flight")
            {
                return;
            }

            PawnFlyer flyer = Find.CurrentMap?.listerThings.ThingsOfDef(Mugirl_DefOf.Mugirl_DunkFlyer)
                .OfType<PawnFlyer>().FirstOrDefault(candidate => candidate.FlyingPawn == caster);
            if (flyer == null)
            {
                dunkMissingFlyerSamples++;
                return;
            }

            // 直接比较当前游戏 tick 的画面坐标，避免无头窗口的 FPS 改变采样结果。
            // 头部显示进度仍由渲染循环维护；读完当 tick 的端点后立即还原。
            FieldInfo stateTicksField = AccessTools.Field(typeof(Thing_MugirlDunkProp), "stateTicks");
            FieldInfo visualTicksField = AccessTools.Field(typeof(Thing_MugirlDunkProp), "visualTicks");
            if (stateTicksField == null || visualTicksField == null)
            {
                dunkMissingFlyerSamples++;
                return;
            }

            float originalVisualTicks = (float)visualTicksField.GetValue(controller);
            Vector3 headPos;
            try
            {
                visualTicksField.SetValue(controller, (float)(int)stateTicksField.GetValue(controller));
                headPos = controller.DrawPos;
            }
            finally
            {
                visualTicksField.SetValue(controller, originalVisualTicks);
            }

            Vector3 flyerPos = flyer.DrawPos;
            float dx = headPos.x - flyerPos.x;
            float dz = headPos.z - flyerPos.z;
            dunkLargestHeadFlyerGap = Mathf.Max(dunkLargestHeadFlyerGap, Mathf.Sqrt(dx * dx + dz * dz));
            dunkHandFollowSamples++;
            if (stage == "Flight")
            {
                if (dunkLastFlightTick >= 0)
                {
                    dunkLastFlightStep = HorizontalDistance(headPos, dunkLastFlightHeadPos);
                }
                dunkLastFlightTick = Find.TickManager.TicksGame;
                dunkLastFlightHeadPos = headPos;
                dunkFlightHandFollowSamples++;
                if (flyer is PawnFlyer_MugirlDunk dunkFlyer)
                {
                    dunkLastHeldFlyerProgress = dunkFlyer.AdjustedFlightProgress;
                }
                // Flyer 的 Thing.Position 一开始就是目标格；用实际绘制的 x 位移证明它已飞动。
                dunkFlyerGroundTravel = Mathf.Max(dunkFlyerGroundTravel,
                    Mathf.Abs(flyerPos.x - dunkFlyerStartCell.ToVector3Shifted().x));
            }
        }

        private static void StartJobDunk()
        {
            Map map = Find.CurrentMap;
            // 第二轮目标须远离第一轮篮筐；爆炸最大半径 7.2 格，避免残余场地干扰任务。
            jobVictimOrigin = origin + new IntVec3(22, 0, 11);
            IntVec3 destination = origin + new IntVec3(31, 0, 11);
            Faction enemy = Find.FactionManager.AllFactionsListForReading.First(f => !f.IsPlayer && f.HostileTo(Faction.OfPlayer));
            jobVictim = MakePawn(PawnKindDefOf.Colonist, enemy, jobVictimOrigin, map);
            jobVictim.stances.stunner.StunFor(6000, null);
            caster.jobs.StopAll();
            caster.jobs.ClearQueuedJobs();
            Job job = dunkAbility.GetJob(new LocalTargetInfo(jobVictim), new LocalTargetInfo(destination));
            bool ordered = caster.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            Check("ability-generated dunk JobDriver accepts the live victim and destination (ordered="
                + ordered + ", current=" + (caster.CurJob?.def?.defName ?? "null")
                + ", victimDead=" + jobVictim.Dead + ")",
                ordered && caster.CurJob?.def == Mugirl_DefOf.Job_MugirlDunk
                && caster.CurJob.GetTarget(TargetIndex.A).Thing == jobVictim
                && caster.CurJob.GetTarget(TargetIndex.B).Cell == destination);
            jobStartTick = Find.TickManager.TicksGame;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
        }

        private static void TickJobDunk()
        {
            Map map = Find.CurrentMap;
            Thing_MugirlDunkProp active = MugirlThrowUtility.FindDunkController(caster);
            if (active != null)
            {
                jobProp = active;
                if (active.HasPayload && jobVictim.Dead && jobVictim.Corpse?.Spawned == true)
                {
                    jobHead = active.SearchableContents[0] as Thing_MugirlDunkHead;
                    sawJobHeadPickup |= jobHead != null;
                }
                if (!checkedJobRunUp && active.HasPayload && !active.SequenceStarted)
                {
                    checkedJobRunUp = true;
                    Check("JobDriver keeps the victim in A and stores its run-up cell in C",
                        caster.CurJob?.def == Mugirl_DefOf.Job_MugirlDunk
                        && caster.CurJob.GetTarget(TargetIndex.A).Thing == jobVictim
                        && caster.CurJob.GetTarget(TargetIndex.C).Cell.IsValid);
                }
                sawJobSequence |= active.SequenceStarted;
            }
            sawJobFlyer |= map.listerThings.ThingsOfDef(Mugirl_DefOf.Mugirl_DunkFlyer).Count > 0;
            int elapsed = Find.TickManager.TicksGame - jobStartTick;
            if (elapsed < 650 && !(sawJobSequence && jobProp?.Destroyed == true && caster.Spawned))
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                nextAt = Time.realtimeSinceStartup + 0.05f;
                return;
            }

            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Check("real JobDriver picks up a detached head and preserves the victim corpse",
                sawJobHeadPickup && jobVictim.Dead && jobVictim.Corpse != null
                && jobVictim.Corpse.Spawned && jobVictim.Corpse.Position == jobVictimOrigin);
            Check("real JobDriver runs up, dunks, and lands through its dedicated flyer",
                checkedJobRunUp && sawJobSequence && sawJobFlyer && caster.Spawned && !caster.Dead);
            Check("real JobDriver shatters the head and clears its controller",
                jobHead?.Destroyed == true && jobProp?.Destroyed == true
                && MugirlThrowUtility.FindDunkController(caster) == null);
            phase = 8;
            nextAt = Time.realtimeSinceStartup + 0.1f;
        }

        private static Pawn MakePawn(PawnKindDef kind, Faction faction, IntVec3 cell, Map map)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer,
                map.Tile, forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                canGeneratePawnRelations: false, allowPregnant: false, forceNoIdeo: true,
                developmentalStages: DevelopmentalStage.Adult));
            GenSpawn.Spawn(pawn, cell, map);
            return pawn;
        }

        private static Thing SpawnStack(ThingDef def, int count, IntVec3 cell, Map map)
        {
            Thing stack = ThingMaker.MakeThing(def);
            stack.stackCount = count;
            return GenSpawn.Spawn(stack, cell, map, WipeMode.Vanish);
        }

        private static Building SpawnWall(IntVec3 cell, Map map)
        {
            Thing wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Plasteel);
            return (Building)GenSpawn.Spawn(wall, cell, map, WipeMode.Vanish);
        }

        private static Building SpawnStool(IntVec3 cell, Map map)
        {
            Thing stool = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Stool"), ThingDefOf.WoodLog);
            return (Building)GenSpawn.Spawn(stool, cell, map, WipeMode.Vanish);
        }

        internal static void RecordDunkBlood(FleckManager manager, FleckCreationData data)
        {
            if (bloodStage == 0 || dunkBloodFleck == null || data.def != dunkBloodFleck
                || Find.CurrentMap == null || manager != Find.CurrentMap.flecks)
            {
                return;
            }

            IntVec3 center = bloodStage == 1 ? victimOrigin
                : bloodStage == 2 ? caster.Position : dunkDestination;
            Vector3 at = center.ToVector3Shifted();
            float dx = data.spawnPosition.x - at.x;
            float dz = data.spawnPosition.z - at.z;
            if (dx * dx + dz * dz > 36f)
            {
                return;
            }

            if (bloodStage == 1) severBloodFlecks++;
            else if (bloodStage == 2) dribbleBloodFlecks++;
            else if (bloodStage == 3) slamBloodFlecks++;
        }

        private static bool HasDunkGizmo(Pawn pawn, AbilityDef def)
        {
            return pawn.GetGizmos().OfType<Command_Ability>()
                .Any(command => command.Ability?.def == def);
        }

        private static void Check(string label, bool passed)
        {
            if (!passed) failures++;
            string line = (passed ? "PASS " : "FAIL ") + label;
            File.AppendAllText(Path.Combine(outputRoot, "throw-checks.txt"), line + Environment.NewLine);
            Log.Message("[ThrowValidation] " + line);
        }

        private static void Finish()
        {
            if (phase == 9) return;
            phase = 9;
            bloodStage = 0;
            Prefs.DevMode = previousDevMode;
            File.WriteAllText(Path.Combine(outputRoot, "throw-complete.txt"),
                (failures == 0 ? "PASS" : "FAIL") + " failures=" + failures
                + " " + DateTime.UtcNow.ToString("O"));
            Application.Quit();
        }
    }

    [HarmonyPatch(typeof(Thing_MugirlThrownObject), "ResolveImpact")]
    internal static class ThrowImpactTimingProbe
    {
        private static void Prefix(Thing_MugirlThrownObject __instance)
        {
            ThrowRuntimeValidation.RecordThrowImpact(__instance);
        }
    }

    [HarmonyPatch(typeof(Thing_MugirlThrownObject), "TickFlying")]
    internal static class ThrowFlightTickProbe
    {
        private static void Prefix(Thing_MugirlThrownObject __instance, int delta)
        {
            ThrowRuntimeValidation.RecordThrowFlightTick(__instance, delta);
        }
    }

    [HarmonyPatch(typeof(FleckManager), nameof(FleckManager.CreateFleck))]
    internal static class DunkBloodFleckProbe
    {
        private static void Prefix(FleckManager __instance, FleckCreationData fleckData)
        {
            ThrowRuntimeValidation.RecordDunkBlood(__instance, fleckData);
        }
    }

    [HarmonyPatch(typeof(Thing_MugirlDunkProp), "ResolveSmash")]
    internal static class DunkImpactProbe
    {
        private static void Prefix(Thing_MugirlDunkProp __instance)
        {
            ThrowRuntimeValidation.RecordDunkImpactBefore(__instance);
        }

        private static void Postfix(Thing_MugirlDunkProp __instance)
        {
            ThrowRuntimeValidation.RecordDunkImpact(__instance);
        }
    }

    [HarmonyPatch(typeof(Thing_MugirlDunkProp), "TickInterval")]
    internal static class DunkHandFollowProbe
    {
        private static void Postfix(Thing_MugirlDunkProp __instance)
        {
            ThrowRuntimeValidation.RecordDunkHandFollow(__instance);
            ThrowRuntimeValidation.RecordDunkSlam(__instance);
        }
    }
}
