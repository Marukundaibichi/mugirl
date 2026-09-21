// Optional EnableStylingValidation cold-start probe; no world or player save is created.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AlienRace;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    [StaticConstructorOnStartup]
    internal static class StylingStartupValidation
    {
        private static string outputRoot;
        private static int failures;

        static StylingStartupValidation()
        {
            if (!GenCommandLine.CommandLineArgPassed("mugirlStylingStartupChecks")) return;
            // Prepatcher 从内存加载程序集，Assembly.Location 可能为空；使用实际 Mod 根目录。
            if (string.IsNullOrEmpty(MugirlMod.ContentRoot)) return;
            string temporaryRoot = Path.GetFullPath(Path.Combine(MugirlMod.ContentRoot, "TMP"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string actualRoot = Path.GetFullPath(GenFilePaths.SaveDataFolderPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!actualRoot.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)) return;
            outputRoot = actualRoot;
            // Do not touch StylingStation fields on the startup worker. The bootstrap's
            // deferred patch registration was queued before this startup attribute runs.
            LongEventHandler.ExecuteWhenFinished(Run);
        }

        private static void Run()
        {
            try
            {
                Directory.CreateDirectory(outputRoot);
                Check("startup checks execute on the Unity main thread", UnityData.IsInMainThread);
                bool withYaOpt = ModsConfig.IsActive("sz.yaopt");
                bool expectedYaOpt = GenCommandLine.CommandLineArgPassed("mugirlStylingWithYaOpt");
                Check("YaOpt activation matches the requested fixture (actual=" + withYaOpt
                    + ", expected=" + expectedYaOpt + ")", withYaOpt == expectedYaOpt);
                if (expectedYaOpt)
                    Check("YaOpt fixture includes Prepatcher for lazy texture loading", ModsConfig.IsActive("zetrith.prepatcher"));
                CheckTexture("ChainTex", "AlienRace/UI/LinkChain");
                CheckTexture("ClearTex", "AlienRace/UI/ClearButton");
                CheckTexture("ChainVanillaTex", "AlienRace/UI/LinkVanilla");
                CheckPatches("initial startup");
                CheckNewApparelGraphics();
                CheckLongCascadeBackHair();
                CheckHornHidingHelmetTags();
                MugirlBootstrap.Initialize();
                // Observe again after any callbacks from the repeated initialization.
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    try
                    {
                        Check("repeat initialization check runs on the Unity main thread", UnityData.IsInMainThread);
                        CheckPatches("repeated initialization");
                    }
                    catch (Exception exception) { Check("repeat initialization exception: " + exception, false); }
                    finally { Finish(); }
                });
            }
            catch (Exception exception)
            {
                try { Check("startup exception: " + exception, false); }
                finally { Finish(); }
            }
        }

        private static void CheckNewApparelGraphics()
        {
            string[] names =
            {
                "CorporateBaseballCap", "CorporateSunHat", "HighCutSweater", "SisterMask", "BrandBag", "LeatherHeels",
                "PoliceShirt", "PoliceShorts", "PoliceThong", "PoliceZipperBra", "PoliceBra", "PoliceCrossStickers",
                "PoliceBoots", "PoliceCap", "NunDressClassic", "NunDressSimple", "NunVeil", "NunBlindfold",
                "KnightMountPlateWhite", "KnightMountCoat", "KnightMountHelmet",
                "NeuralCombatArmor", "NeuralAmplifierHelmet"
            };
            foreach (string name in names)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamed("Mugirl_" + name);
                Check(def.defName + " is adult-only", def.apparel.developmentalStageFilter == DevelopmentalStage.Adult);
                if (name == "SisterMask" || name == "NunBlindfold")
                {
                    Check(def.defName + " uses the head apparel layer and mesh without extra transforms",
                        def.apparel.LastLayer == (name == "SisterMask" ? ApparelLayerDefOf.Overhead : ApparelLayerDefOf.EyeCover)
                        && def.apparel.parentTagDef == PawnRenderNodeTagDefOf.ApparelHead
                        && def.apparel.drawData == null
                        && def.apparel.wornGraphicData == null
                        && def.graphicData.drawSize == Vector2.one);
                }
                else if (name == "NunVeil")
                {
                    PawnRenderNodeProperties rearLayer = def.apparel.RenderNodeProperties
                        .FirstOrDefault(props => props.nodeClass == typeof(Features.Appearance.PawnRenderNode_NunVeilBack));
                    Check("Mugirl_NunVeil has one rear head layer", rearLayer != null
                        && rearLayer.parentTagDef == PawnRenderNodeTagDefOf.Head
                        && rearLayer.baseLayer < 0f);
                    Texture2D rearTexture = rearLayer == null
                        ? null
                        : ContentFinder<Texture2D>.Get(rearLayer.texPath, false);
                    Check("Mugirl_NunVeil rear layer texture is loaded", rearTexture != null && rearTexture != BaseContent.BadTex);
                }
                // 启动界面尚无游戏或物品 ID 管理器，只构造贴图解析所需的服装对象。
                Apparel apparel = (Apparel)Activator.CreateInstance(def.thingClass);
                apparel.def = def;
                apparel.SetStuffDirect(GenStuff.DefaultStuffFor(def));
                foreach (BodyTypeDef bodyType in new[] { BodyTypeDefOf.Female, BodyTypeDefOf.Child })
                {
                    if (bodyType == BodyTypeDefOf.Child
                        && (!ModsConfig.BiotechActive || !def.apparel.developmentalStageFilter.HasFlag(DevelopmentalStage.Child))) continue;
                    string label = def.defName + "/" + bodyType.defName;
                    bool found = ApparelGraphicRecordGetter.TryGetGraphicApparel(apparel, bodyType, false, out ApparelGraphicRecord record);
                    Check(label + " resolves a worn graphic", found && record.graphic != null);
                    if (!found || record.graphic == null) continue;
                    foreach (Rot4 rotation in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
                    {
                        Texture texture = record.graphic.MatAt(rotation).mainTexture;
                        Check(label + "/" + rotation + " has a loaded worn texture", texture != null && texture != BaseContent.BadTex);
                    }
                }
            }
        }

        private static void CheckHornHidingHelmetTags()
        {
            string[] expectedDefs =
            {
                "Mugirl_Combatant_Helmet",
                "Mugirl_KnightMountHelmet",
                "Mugirl_PowerArmorHelmet"
            };
            foreach (string defName in expectedDefs)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamed(defName);
                Check(defName + " hides Mugirl horns", def.apparel?.tags?.Contains("Mugirl_HideHorns") == true);
            }

            string[] actualDefs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def.apparel?.tags?.Contains("Mugirl_HideHorns") == true)
                .Select(def => def.defName)
                .OrderBy(defName => defName)
                .ToArray();
            Check("only the three requested helmets hide Mugirl horns",
                actualDefs.SequenceEqual(expectedDefs.OrderBy(defName => defName)));
        }

        private static void CheckLongCascadeBackHair()
        {
            HairDef hairDef = DefDatabase<HairDef>.GetNamed("Mugirl_LongCascade");
            Check("Mugirl_LongCascade DefOf resolves to the registered hair",
                Mugirl_DefOf.Mugirl_LongCascade == hairDef);

            ThingDef raceDef = DefDatabase<ThingDef>.GetNamed("Mugirl");
            Check("Mugirl race installs the LongCascade rear-layer comp",
                raceDef.comps?.Any(props => props is Features.Appearance.CompProperties_LongCascadeBackHair) == true);

            PawnRenderNodeProperties nodeProps = Features.Appearance.Comp_LongCascadeBackHair.CreateNodeProperties();
            Check("LongCascade rear layer follows Head and stays behind it",
                nodeProps.parentTagDef == PawnRenderNodeTagDefOf.Head
                && nodeProps.baseLayer < 0f
                && nodeProps.drawSize == Vector2.one);
            Check("LongCascade rear layer is limited to south/east/west and honors hidden hair",
                nodeProps.visibleFacing != null
                && nodeProps.visibleFacing.Count == 3
                && nodeProps.visibleFacing.Contains(Rot4.South)
                && nodeProps.visibleFacing.Contains(Rot4.East)
                && nodeProps.visibleFacing.Contains(Rot4.West)
                && !nodeProps.visibleFacing.Contains(Rot4.North)
                && nodeProps.skipFlag == RenderSkipFlagDefOf.Hair);

            CheckTextureDimensions("LongCascade rear south", "Mugirl/Hair/Mugirl_LongCascadeBack_south", 512, 512);
            CheckTextureDimensions("LongCascade rear east/west", "Mugirl/Hair/Mugirl_LongCascadeBack_east", 512, 512);
        }

        private static void CheckTextureDimensions(string label, string contentPath, int width, int height)
        {
            Texture2D texture = ContentFinder<Texture2D>.Get(contentPath, false);
            Check(label + " texture is loaded at " + width + "x" + height,
                texture != null
                && texture != BaseContent.BadTex
                && texture.width == width
                && texture.height == height);
        }

        private static void CheckTexture(string fieldName, string contentPath)
        {
            FieldInfo field = typeof(StylingStation).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
            Check(fieldName + " is the expected private readonly Texture2D field",
                field != null && field.IsPrivate && field.IsInitOnly && field.FieldType == typeof(Texture2D));
            Texture2D actual = field?.GetValue(null) as Texture2D;
            Check(fieldName + " holds a valid loaded texture instead of BadTex",
                actual != null && actual != BaseContent.BadTex && actual.width > 0 && actual.height > 0);
            Texture2D expected = ContentFinder<Texture2D>.Get(contentPath, true);
            Check(fieldName + " retains the actual HAR texture at " + contentPath,
                expected != null && expected != BaseContent.BadTex && ReferenceEquals(actual, expected));
        }

        private static void CheckPatches(string stage)
        {
            MethodInfo method = AccessTools.Method(typeof(StylingStation), nameof(StylingStation.DoRaceTabs));
            Patches info = method == null ? null : HarmonyLib.Harmony.GetPatchInfo(method);
            Type patchClass = typeof(Harmony_StylingStationRefresh);
            int prefixes = info == null ? 0 : info.Prefixes.Count(p => p.owner == MugirlBootstrap.HarmonyId
                && p.PatchMethod?.DeclaringType == patchClass);
            int postfixes = info == null ? 0 : info.Postfixes.Count(p => p.owner == MugirlBootstrap.HarmonyId
                && p.PatchMethod?.DeclaringType == patchClass);
            Check(stage + ": exactly one Mugirl styling Prefix (actual=" + prefixes + ")", prefixes == 1);
            Check(stage + ": exactly one Mugirl styling Postfix (actual=" + postfixes + ")", postfixes == 1);
            int registered = MugirlBootstrap.PatchedClassNames.Count(name => name == patchClass.FullName);
            Check(stage + ": bootstrap records this patch class exactly once (actual=" + registered + ")", registered == 1);
        }

        private static void Check(string label, bool passed)
        {
            if (!passed) failures++;
            string line = (passed ? "PASS " : "FAIL ") + label;
            File.AppendAllText(Path.Combine(outputRoot, "styling-startup-checks.txt"), line + Environment.NewLine);
            Log.Message("[StylingStartupValidation] " + line);
        }

        private static void Finish()
        {
            try
            {
                File.WriteAllText(Path.Combine(outputRoot, "styling-startup-complete.txt"),
                    (failures == 0 ? "PASS" : "FAIL") + " failures=" + failures + " " + DateTime.UtcNow.ToString("O"));
            }
            finally { Application.Quit(); }
        }
    }
}
