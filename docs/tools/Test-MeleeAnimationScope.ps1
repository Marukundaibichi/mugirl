param([string]$CscPath, [string]$HarmonyPath)

$ErrorActionPreference = 'Stop'
$modRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $CscPath) {
    $msbuildCommand = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
    if ($msbuildCommand) {
        $CscPath = Join-Path (Split-Path $msbuildCommand.Source -Parent) 'Roslyn\csc.exe'
    }
}
if (-not $CscPath -or -not (Test-Path -LiteralPath $CscPath)) {
    throw 'Pass -CscPath pointing to the Visual Studio Roslyn csc.exe (C# 7.2 or newer).'
}
if (-not $HarmonyPath) {
    $projectPath = Join-Path $modRoot '1.6\Source\MugirlRace.csproj'
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $harmonyReference = @($project.Project.ItemGroup.Reference) | Where-Object { $_.Include -eq '0Harmony' } | Select-Object -First 1
    if (-not $harmonyReference) { throw 'The project does not declare a Harmony reference.' }
    $HarmonyPath = [IO.Path]::GetFullPath((Join-Path (Split-Path $projectPath -Parent) ([string]$harmonyReference.HintPath)))
}
if (-not (Test-Path -LiteralPath $HarmonyPath)) { throw 'Harmony DLL missing; pass -HarmonyPath or restore the project reference.' }

# Use the actual MeleeAnimationCompat.cs and project-referenced Harmony DLL.
# A .NET Framework executable matches this Harmony build; PowerShell 7's runtime does not.
# Pawn/drawing targets and Melee Animation settings are minimal stubs, not game rendering.
$harnessSource = @'
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace AM.AMSettings { public class Settings { public bool AnimateAtIdle = true; } }
namespace AM { public static class Core { public static AMSettings.Settings Settings = new AMSettings.Settings(); } }
namespace AM.Idle { public class IdleControllerComp : Verse.ThingComp { public void ClearAnimation() { } } }
namespace RimWorld { public class Placeholder { } }
namespace Verse
{
    public class ThingDef { public bool IsMeleeWeapon = true; }
    public class ThingComp { }
    public class ThingWithComps { public ThingDef def = new ThingDef(); }
    public class Equipment { public ThingWithComps Primary = new ThingWithComps(); }
    public class Pawn { public Equipment equipment = new Equipment(); public List<ThingComp> AllComps = new List<ThingComp>(); }
    public class Pawn_DrawTracker { private Pawn pawn = new Pawn(); public void Notify_MeleeAttackOn() { GC.KeepAlive(pawn); } }
    public static class ModLister { public static object GetActiveModWithIdentifier(string id) { return new object(); } }
}
namespace Mugirl.Features.WeaponWheel
{
    public class Comp_WeaponWheel
    {
        public Verse.Pawn Pawn = new Verse.Pawn();
        public bool ShouldSuppressExternalMeleeAttackAnimation() { return true; }
        public bool MoveActiveWeaponTo() { return true; }
        public bool NormalizeToPrimarySlot() { return true; }
    }
    public static class WeaponWheelHarmonyUtility { public static Comp_WeaponWheel CompFor(Verse.Pawn pawn) { return null; } }
}
namespace Mugirl
{
    public class Comp_MugirlMount
    {
        public Verse.Pawn MountedPawn = new Verse.Pawn();
        public int Nested;
        public bool Throw;
        public Action DuringRender;
    }
    public static class MountedPawnMeleeSupport
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void DrawWeapon(Comp_MugirlMount comp)
        {
            PerfMeleeTests.Assert(!AM.Core.Settings.AnimateAtIdle, "Setting suspended inside actual patched method");
            if (comp.Nested > 0)
            {
                comp.Nested--;
                DrawWeapon(comp);
                PerfMeleeTests.Assert(!AM.Core.Settings.AnimateAtIdle, "Nested return retains outer suspension");
            }
            if (comp.DuringRender != null) comp.DuringRender();
            if (comp.Throw) throw new InvalidOperationException("expected rendering failure");
        }
    }
    public static class PerfMeleeTests
    {
        public static void Main() { Console.WriteLine(Run()); }
        private static int assertions;
        public static void Assert(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
        public static void LatePostfix()
        {
            Assert(AM.Core.Settings.AnimateAtIdle, "Main postfix already restored before later postfix");
            AM.Core.Settings.AnimateAtIdle = false;
        }
        public static string Run()
        {
            var harmony = new Harmony("mugirl.perf.state");
            harmony.CreateClassProcessor(typeof(Harmony_MeleeAnimation_MountedWeaponDraw)).Patch();
            try
            {
                MountedPawnMeleeSupport.DrawWeapon(new Comp_MugirlMount());
                Assert(AM.Core.Settings.AnimateAtIdle, "Normal postfix restores setting");
                MountedPawnMeleeSupport.DrawWeapon(new Comp_MugirlMount { Nested = 3 });
                Assert(AM.Core.Settings.AnimateAtIdle, "Nested postfix restores outer state");
                try { MountedPawnMeleeSupport.DrawWeapon(new Comp_MugirlMount { Nested = 2, Throw = true }); Assert(false, "Expected exception"); }
                catch (InvalidOperationException ex) { Assert(ex.Message == "expected rendering failure", "Finalizer preserves original exception"); }
                Assert(AM.Core.Settings.AnimateAtIdle, "Finalizer restores after nested exception");

                AM.Core.Settings.AnimateAtIdle = false;
                MountedPawnMeleeSupport.DrawWeapon(new Comp_MugirlMount());
                Assert(!AM.Core.Settings.AnimateAtIdle, "Originally disabled setting stays disabled");
                AM.Core.Settings.AnimateAtIdle = true;

                var originalSettings = AM.Core.Settings;
                var replacementSettings = new AM.AMSettings.Settings { AnimateAtIdle = false };
                MountedPawnMeleeSupport.DrawWeapon(new Comp_MugirlMount { DuringRender = () => AM.Core.Settings = replacementSettings });
                Assert(originalSettings.AnimateAtIdle && !replacementSettings.AnimateAtIdle, "Restore original instance after settings replacement");
                AM.Core.Settings = originalSettings;

                MethodInfo target = typeof(MountedPawnMeleeSupport).GetMethod("DrawWeapon");
                var later = new Harmony("mugirl.perf.state.later");
                later.Patch(target, postfix: new HarmonyMethod(typeof(PerfMeleeTests).GetMethod("LatePostfix")) { after = new[] { "mugirl.perf.state" } });
                MountedPawnMeleeSupport.DrawWeapon(new Comp_MugirlMount());
                Assert(!AM.Core.Settings.AnimateAtIdle, "Finalizer does not restore twice after postfix has consumed ref state");
                later.UnpatchAll("mugirl.perf.state.later");
                AM.Core.Settings.AnimateAtIdle = true;
                return assertions + " assertions passed with actual Harmony patch generation and actual MeleeAnimationCompat.cs.";
            }
            finally { harmony.UnpatchAll("mugirl.perf.state"); }
        }
    }
}

'@

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('mugirl-melee-tests-' + [Guid]::NewGuid().ToString('N'))
$harnessPath = Join-Path $testRoot 'Harness.cs'
$testExe = Join-Path $testRoot 'MeleeScopeTests.exe'
$localHarmony = Join-Path $testRoot '0Harmony.dll'
try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    [IO.File]::WriteAllText($harnessPath, $harnessSource, [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath $HarmonyPath -Destination $localHarmony
    $sourcePath = Join-Path $modRoot '1.6\Source\Compatibility\MeleeAnimation\MeleeAnimationCompat.cs'
    & $CscPath /nologo /langversion:7.2 /target:exe "/out:$testExe" "/reference:$localHarmony" $sourcePath $harnessPath
    if ($LASTEXITCODE -ne 0) { throw 'Melee Animation scope test compilation failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Melee Animation scope tests failed.' }
}
finally {
    foreach ($testFile in @($harnessPath, $testExe, $localHarmony)) {
        if (Test-Path -LiteralPath $testFile) { Remove-Item -LiteralPath $testFile }
    }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot }
}
