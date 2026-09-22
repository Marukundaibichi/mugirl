// 仅以 EnableStylingValidation 构建；使用独立 quicktest 地图检查真实头饰节点和四向穿戴效果。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(Game), nameof(Game.UpdatePlay))]
    internal static class FaceAccessoryRuntimeValidation
    {
        // StaticCacheLifecycle: 仅供单次独立验证进程使用，退出时释放，不写入用户存档。
        private static Pawn pawn;
        private static string outputRoot;
        private static int phase;
        private static int failures;
        private static float nextAt;
        private static string pendingShot;

        private static void Postfix()
        {
            if (!GenCommandLine.CommandLineArgPassed("mugirlFaceAccessoryChecks")
                || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null || phase < 0) return;
            string allowedRoot = Path.GetFullPath(Path.Combine(MugirlMod.ContentRoot, "TMP")) + Path.DirectorySeparatorChar;
            outputRoot = Path.GetFullPath(GenFilePaths.SaveDataFolderPath);
            if (!outputRoot.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase)
                || Time.realtimeSinceStartup < nextAt) return;
            try
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                if (phase == 0) CreatePawn();
                if (pendingShot != null)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(outputRoot, pendingShot));
                    pendingShot = null;
                }
                else if (phase <= 18)
                {
                    int frame = phase / 2;
                    string defName = frame < 4 ? "Mugirl_SisterMask" : "Mugirl_NunBlindfold";
                    Rot4 facing = new[] { Rot4.South, Rot4.East, Rot4.North, Rot4.West }[frame % 4];
                    pawn.apparel.DestroyAll();
                    Wear("Mugirl_NunDressSimple");
                    Apparel accessory = Wear(defName);
                    // 最后两帧检查眼罩与头纱组合，以及卸下后节点是否移除。
                    if (frame == 8) Wear("Mugirl_NunVeil");
                    if (frame == 9) pawn.apparel.Remove(accessory);
                    pawn.Rotation = facing;
                    pawn.Drawer.renderer.SetAllGraphicsDirty();
                    pawn.Drawer.renderer.renderTree.EnsureInitialized(PawnRenderFlags.None);
                    List<PawnRenderNode> nodes = Descendants(pawn.Drawer.renderer.renderTree.rootNode).ToList();
                    List<PawnRenderNode_Apparel> accessories = nodes.OfType<PawnRenderNode_Apparel>()
                        .Where(node => node.apparel == accessory).ToList();
                    string label = defName + "/" + facing + "/" + frame;
                    if (frame == 9) Check(label + " removed apparel leaves no render node", accessories.Count == 0);
                    else
                    {
                        Check(label + " has exactly one worn node", accessories.Count == 1);
                        PawnRenderNode_Apparel node = accessories.Single();
                        Check(label + " follows ApparelHead with the head worker and head mesh",
                            node.parent?.Props.tagDef == PawnRenderNodeTagDefOf.ApparelHead
                            && node.Worker is PawnRenderNodeWorker_Apparel_Head && node.useHeadMesh);
                        Mesh mesh = node.MeshSetFor(pawn).MeshAt(facing);
                        Mesh headMesh = HumanlikeMeshPoolUtility.GetHumanlikeHeadSetForPawn(pawn).MeshAt(facing);
                        Check(label + " matches the head canvas size", mesh.bounds.size == headMesh.bounds.size);
                        Check(label + " has no added offset or scale",
                            (defName == "Mugirl_NunBlindfold"
                                ? StylingStartupValidation.HasOnlyLayerOverride(node.Props.drawData, 61f)
                                : node.Props.drawData == null) && node.Props.drawSize == Vector2.one
                            && !node.Props.overrideMeshSize.HasValue && accessory.def.apparel.wornGraphicData == null);
                        bool found = ApparelGraphicRecordGetter.TryGetGraphicApparel(accessory, pawn.story.bodyType, false, out ApparelGraphicRecord graphic);
                        Check(label + " resolves the unsuffixed head texture", found && graphic.graphic.path == accessory.def.apparel.wornGraphicPath
                            && graphic.graphic.MatAt(facing).mainTexture != BaseContent.BadTex);
                        if (frame == 8) Check("blindfold and veil remain equipped together", pawn.apparel.WornApparel.Contains(accessory)
                            && pawn.apparel.WornApparel.Any(ap => ap.def.defName == "Mugirl_NunVeil"));
                    }
                    Find.Selector.ClearSelection();
                    Find.CameraDriver.SetRootPosAndSize(pawn.DrawPos, 4f);
                    pendingShot = "face-" + frame + "-" + facing + ".png";
                    SavePortrait(frame, facing);
                }
                else
                {
                    Finish();
                    return;
                }
                phase++;
                nextAt = Time.realtimeSinceStartup + 1.5f;
            }
            catch (Exception exception)
            {
                Check("runtime exception: " + exception, false);
                Finish();
            }
        }

        private static void CreatePawn()
        {
            Map map = Find.CurrentMap;
            IntVec3 cell = CellFinder.StandableCellNear(map.Center, map, 20);
            foreach (IntVec3 nearby in GenRadial.RadialCellsAround(cell, 3f, true))
            {
                if (!nearby.InBounds(map)) continue;
                foreach (Thing thing in nearby.GetThingList(map).ToList())
                    if (thing is Building || thing is Plant) thing.Destroy();
                map.fogGrid.Unfog(nearby);
            }
            pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("Mugirl_Colony"),
                Faction.OfPlayer, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true,
                allowDead: false, allowDowned: false, canGeneratePawnRelations: false, allowPregnant: false,
                forceNoIdeo: true, developmentalStages: DevelopmentalStage.Adult));
            pawn.equipment.DestroyAllEquipment();
            pawn.story.hairDef = DefDatabase<HairDef>.GetNamed("Shaved");
            GenSpawn.Spawn(pawn, cell, map);
        }

        private static Apparel Wear(string defName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamed(defName);
            Apparel apparel = (Apparel)ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
            pawn.apparel.Wear(apparel, false);
            return apparel;
        }

        private static void SavePortrait(int frame, Rot4 facing)
        {
            RenderTexture portrait = PortraitsCache.Get(pawn, new Vector2(512, 768), facing,
                supersample: false, compensateForUIScale: false, renderHeadgear: true, renderClothes: true);
            RenderTexture previous = RenderTexture.active;
            Texture2D image = new Texture2D(portrait.width, portrait.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = portrait;
                image.ReadPixels(new Rect(0, 0, portrait.width, portrait.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(outputRoot, "portrait-" + frame + "-" + facing + ".png"), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.Destroy(image);
            }
        }

        private static IEnumerable<PawnRenderNode> Descendants(PawnRenderNode node)
        {
            yield return node;
            if (node.children == null) yield break;
            foreach (PawnRenderNode child in node.children)
                foreach (PawnRenderNode descendant in Descendants(child)) yield return descendant;
        }

        private static void Check(string label, bool passed)
        {
            if (!passed) failures++;
            File.AppendAllText(Path.Combine(outputRoot, "face-accessory-checks.txt"),
                (passed ? "PASS " : "FAIL ") + label + Environment.NewLine);
        }

        private static void Finish()
        {
            phase = -1;
            File.WriteAllText(Path.Combine(outputRoot, "face-accessory-complete.txt"),
                (failures == 0 ? "PASS" : "FAIL") + " failures=" + failures + " " + DateTime.UtcNow.ToString("O"));
            Application.Quit();
        }
    }
}
