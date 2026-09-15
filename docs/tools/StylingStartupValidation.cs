// Optional EnableStylingValidation cold-start probe; no world or player save is created.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AlienRace;
using HarmonyLib;
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
