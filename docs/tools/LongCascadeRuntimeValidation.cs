// 仅以 EnableStylingValidation 构建；检查实际渲染节点的方向网格，并输出四向地图图与近景。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Mugirl.Features.Appearance;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(Game), nameof(Game.UpdatePlay))]
    internal static class LongCascadeRuntimeValidation
    {
        // StaticCacheLifecycle: 仅供一次性独立验证进程使用，进程退出即释放，不写用户存档。
        private static Pawn pawn;
        private static string outputRoot;
        private static int phase;
        private static int failures;
        private static float nextAt;
        private static string pendingShot;

        private static void Postfix()
        {
            if (!GenCommandLine.CommandLineArgPassed("mugirlLongCascadeChecks")
                || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null || phase < 0) return;
            outputRoot = Path.GetFullPath(GenFilePaths.SaveDataFolderPath);
            string allowedRoot = Path.GetFullPath(Path.Combine(MugirlMod.ContentRoot, "TMP")) + Path.DirectorySeparatorChar;
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
                else if (phase < 32)
                {
                    int frame = phase / 2;
                    Rot4 facing = new[] { Rot4.South, Rot4.East, Rot4.North, Rot4.West }[frame % 4];
                    pawn.apparel.DestroyAll();
                    Wear("Mugirl_NunDressSimple");
                    if (frame % 8 >= 4) Wear("Mugirl_NunVeil");
                    if (frame >= 8) Wear("Mugirl_NunBlindfold");
                    pawn.Rotation = facing;
                    pawn.Drawer.renderer.SetAllGraphicsDirty();
                    PawnRenderTree tree = pawn.Drawer.renderer.renderTree;
                    tree.EnsureInitialized(PawnRenderFlags.None);
                    List<PawnRenderNode> nodes = Descendants(tree.rootNode).ToList();
                    PawnRenderNode_LongCascadeBackHair back = nodes.OfType<PawnRenderNode_LongCascadeBackHair>().Single();
                    PawnRenderNode_Hair front = nodes.OfType<PawnRenderNode_Hair>().Single();
                    PawnDrawParms parms = new PawnDrawParms { pawn = pawn, facing = facing, rotDrawMode = RotDrawMode.Fresh };
                    string label = "frame=" + frame + "/" + facing;
                    Check(label + " rear follows Head behind the main hair", back.parent?.Props.tagDef == PawnRenderNodeTagDefOf.Head
                        && back.Props.baseLayer < front.Props.baseLayer);
                    Mesh backMesh = back.GetMesh(parms);
                    Mesh frontMesh = front.GetMesh(parms);
                    Check(label + " front/rear UV orientation matches", backMesh.uv.SequenceEqual(frontMesh.uv));
                    Check(label + " front/rear canvas sizes match", backMesh.bounds.size == frontMesh.bounds.size);
                    Check(label + " rear visibility matches direction", back.Worker.CanDrawNow(back, parms) == (facing != Rot4.North));
                    Check(label + " rear directional texture loads", back.GraphicForFacing(pawn, facing).MatAt(facing).mainTexture != BaseContent.BadTex);
                    if (frame >= 8)
                    {
                        PawnRenderNode_Apparel blindfold = nodes.OfType<PawnRenderNode_Apparel>()
                            .Single(node => node.apparel.def.defName == "Mugirl_NunBlindfold");
                        PawnRenderNode head = nodes.Single(node => node.Props.tagDef == PawnRenderNodeTagDefOf.Head);
                        float layer = blindfold.Worker.LayerFor(blindfold, parms);
                        Check(label + " blindfold is above face and below hair", layer > head.Worker.LayerFor(head, parms)
                            && layer < front.Worker.LayerFor(front, parms));
                        Check(label + " blindfold retains head mesh and canvas", blindfold.useHeadMesh
                            && blindfold.parent.Props.tagDef == PawnRenderNodeTagDefOf.ApparelHead
                            && StylingStartupValidation.HasOnlyLayerOverride(blindfold.Props.drawData, 61f));
                    }
                    // 改发型和头盔隐藏时，后发不能残留；随后还原以便拍摄正常效果。
                    HairDef originalHair = pawn.story.hairDef;
                    pawn.story.hairDef = DefDatabase<HairDef>.GetNamed("Shaved");
                    Check(label + " changing hairstyle hides rear", !back.Worker.CanDrawNow(back, parms));
                    pawn.story.hairDef = originalHair;
                    parms.skipFlags = RenderSkipFlagDefOf.Hair;
                    Check(label + " hidden hair also hides rear", !back.Worker.CanDrawNow(back, parms));
                    Find.Selector.ClearSelection();
                    Find.CameraDriver.SetRootPosAndSize(pawn.DrawPos, 4f);
                    pendingShot = "hair-" + frame + "-" + facing + ".png";
                    SavePortrait(frame, facing);
                }
                else { Finish(); return; }
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
            pawn.story.hairDef = Mugirl_DefOf.Mugirl_LongCascade;
            GenSpawn.Spawn(pawn, cell, map);
        }

        private static void Wear(string defName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamed(defName);
            pawn.apparel.Wear((Apparel)ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def)), false);
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
            File.AppendAllText(Path.Combine(outputRoot, "long-cascade-checks.txt"),
                (passed ? "PASS " : "FAIL ") + label + Environment.NewLine);
        }

        private static void Finish()
        {
            phase = -1;
            File.WriteAllText(Path.Combine(outputRoot, "long-cascade-complete.txt"),
                (failures == 0 ? "PASS" : "FAIL") + " failures=" + failures + " " + DateTime.UtcNow.ToString("O"));
            Application.Quit();
        }
    }
}
