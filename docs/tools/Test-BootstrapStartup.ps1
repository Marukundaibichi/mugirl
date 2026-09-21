param([string]$CscPath)
$ErrorActionPreference = 'Stop'
$modRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $CscPath) {
    $CscPath = Join-Path (Split-Path (Get-Command MSBuild.exe).Source -Parent) 'Roslyn\csc.exe'
}
# 执行真实启动与日志源码，注入补丁失败和未初始化语言，验证异常隔离。
$harness = @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mugirl;
using Verse;
namespace HarmonyLib {
    public class HarmonyPatch : Attribute {}
    public class HarmonyMethod { public HarmonyMethod(MethodInfo method) {} }
    public class Harmony {
        public static bool FailManual;
        public Harmony(string id) {}
        public Processor CreateClassProcessor(Type type) { return new Processor(type); }
        public void Patch(MethodBase target, HarmonyMethod prefix = null, HarmonyMethod postfix = null) {
            if (FailManual) throw new InvalidOperationException("manual wrapper", new ArgumentException("manual root cause"));
        }
    }
    public class Processor {
        private Type type;
        public Processor(Type type) { this.type = type; }
        public void Patch() {
            if (type == typeof(FailingPatch))
                throw new InvalidOperationException("patch wrapper", new ArgumentException("original patch cause"));
        }
    }
    public static class AccessTools {
        public static MethodInfo Method(Type type, string name, Type[] args = null) {
            return args == null ? type.GetMethod(name) : type.GetMethod(name, args);
        }
    }
}
namespace Verse {
    public static class LanguageDatabase { public static object activeLanguage; }
    public static class Translator {
        public static int Calls;
        public static string Translate(this string key, params object[] args) {
            Calls++;
            if (LanguageDatabase.activeLanguage == null) throw new Exception("No active language!");
            return key + ": " + string.Join(" / ", args);
        }
    }
    public static class Prefs { public static bool DevMode; }
    public static class Log {
        public static readonly List<string> Warnings = new List<string>();
        public static void Message(string text) {}
        public static void Warning(string text) { Warnings.Add(text); }
    }
    public static class LongEventHandler {
        public static readonly List<Action> Pending = new List<Action>();
        public static void ExecuteWhenFinished(Action action) { Pending.Add(action); }
    }
    public struct PawnGenerationRequest {}
    public static class PawnGenerator { public static void GeneratePawn(PawnGenerationRequest request) {} }
}
namespace Mugirl {
    [HarmonyPatch] internal class FailingPatch {}
    [HarmonyPatch] internal class HealthyPatch {}
    [HarmonyPatch] internal class Harmony_StylingStationRefresh {}
    internal static class PawnGenerator_GeneratePawn_Patch { public static void Postfix() {} }
    internal static class MugirlPatchCatalog {
        public static void LogDevSummary(IReadOnlyList<string> classes, IReadOnlyList<string> manual) {}
    }
}
class Tests {
    static int assertions;
    static void Check(bool passed, string label) {
        if (!passed) throw new Exception(label);
        assertions++;
        Console.WriteLine("PASS " + label);
    }
    static object Call(Type type, string method, params object[] args) {
        return type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
    }
    static void Main() {
        MugirlBootstrap.Initialize();
        Check(Translator.Calls == 0, "startup failure never invokes translation before language initialization");
        Check(Log.Warnings.Count == 1 && Log.Warnings[0].Contains("Mugirl.FailingPatch")
            && Log.Warnings[0].Contains("original patch cause"), "failed class and inner exception are immediately logged");
        Check(MugirlBootstrap.PatchedClassNames.Contains(typeof(HealthyPatch).FullName)
            && !MugirlBootstrap.PatchedClassNames.Contains(typeof(FailingPatch).FullName),
            "failed patch is excluded while other patches continue");
        Check(MugirlPatchRegistry.ManualPatchNames.Count == 1, "manual registration continues after an attribute patch fails");
        Check(!MugirlBootstrap.PatchedClassNames.Contains(typeof(Harmony_StylingStationRefresh).FullName),
            "styling patch remains deferred");
        Call(typeof(MugirlBootstrap), "TryPatchClass", MugirlBootstrap.Harmony, typeof(FailingPatch));
        Check(Log.Warnings.Count == 1, "repeated patch failure logs only once");

        MugirlPatchRegistry.RegisterManualPatches(null);
        Call(typeof(MugirlPatchRegistry), "TryPatch", MugirlBootstrap.Harmony, "missing-target", null, null, null);
        MethodInfo target = typeof(PawnGenerator).GetMethod("GeneratePawn");
        Call(typeof(MugirlPatchRegistry), "TryPatch", MugirlBootstrap.Harmony, "missing-method", target, null, null);
        Harmony.FailManual = true;
        MugirlPatchRegistry.RegisterManualPatches(MugirlBootstrap.Harmony);
        Check(Translator.Calls == 0 && Log.Warnings.Count == 5,
            "all manual registration error paths are safe before language initialization");
        Check(Log.Warnings[4].Contains("manual root cause"), "manual failure preserves the inner exception");
        Check(MugirlPatchRegistry.ManualPatchNames.Count == 0, "failed manual patch is not recorded as successful");

        LanguageDatabase.activeLanguage = new object();
        foreach (Action callback in LongEventHandler.Pending.ToArray()) callback();
        Check(MugirlBootstrap.PatchedClassNames.Contains(typeof(Harmony_StylingStationRefresh).FullName),
            "deferred patch still registers after loading");
        MugirlLog.ResetOnceWarnings();
        Call(typeof(MugirlBootstrap), "TryPatchClass", MugirlBootstrap.Harmony, typeof(FailingPatch));
        Check(Translator.Calls == 1 && Log.Warnings.Last().Contains("Mugirl.Bootstrap.HarmonyPatchFailed"),
            "language-ready diagnostics use the existing localization key");
        MugirlPatchRegistry.RegisterManualPatches(MugirlBootstrap.Harmony);
        Check(Translator.Calls == 3 && Log.Warnings.Last().Contains("Mugirl.PatchRegistry.PatchFailed"),
            "language-ready manual diagnostics translate both name and message");
        int registered = MugirlBootstrap.PatchedClassNames.Count;
        MugirlBootstrap.Initialize();
        Check(MugirlBootstrap.PatchedClassNames.Count == registered, "repeat initialization does not register twice");
        Console.WriteLine(assertions + " bootstrap startup assertions passed.");
    }
}
'@
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('mugirl-bootstrap-tests-' + [Guid]::NewGuid().ToString('N'))
$harnessPath = Join-Path $testRoot 'Harness.cs'
$testExe = Join-Path $testRoot 'BootstrapTests.exe'
try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    [IO.File]::WriteAllText($harnessPath, $harness, [Text.UTF8Encoding]::new($false))
    $sources = @('MugirlBootstrap.cs', 'MugirlLog.cs', 'MugirlPatchRegistry.cs') | ForEach-Object {
        Join-Path $modRoot ('1.6\Source\Core\' + $_)
    }
    & $CscPath /nologo /langversion:7.2 /target:exe "/out:$testExe" $harnessPath @sources
    if ($LASTEXITCODE -ne 0) { throw 'Bootstrap tests compilation failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Bootstrap tests failed.' }
}
finally {
    foreach ($testFile in @($harnessPath, $testExe)) {
        if (Test-Path -LiteralPath $testFile) { Remove-Item -LiteralPath $testFile }
    }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot }
}
