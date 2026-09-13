param([string]$CscPath)

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

# Compile the actual production source with controlled clock, pawn and renderer stubs.
# This checks scheduling and invalidation counts; it does not render real game graphics.
$harnessSource = @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string name) { } }
    public static class AccessTools { public static FieldInfo Field(Type type, string name) { return type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); } }
}
namespace RimWorld
{
    public class BodyTypeDef { }
    public static class BodyTypeDefOf
    {
        public static readonly BodyTypeDef Baby = new BodyTypeDef();
        public static readonly BodyTypeDef Child = new BodyTypeDef();
        public static readonly BodyTypeDef Female = new BodyTypeDef();
    }
    public static class ModsConfig { public static bool BiotechActive = true; }
    public static class PortraitsCache { public static int Count; public static void SetDirty(Verse.Pawn pawn) { Count++; } }
}
namespace Verse
{
    public class Game { }
    public class GameComponent { public virtual void GameComponentTick() { } }
    public class HediffDef { public string defName; }
    public class Hediff { public HediffDef def; public Pawn pawn; public void PostAdd() { } }
    public class LifeStage { public string defName; }
    public class Pawn_AgeTracker { public Pawn pawn; public LifeStage CurLifeStage = new LifeStage(); }
    public class Story { public RimWorld.BodyTypeDef bodyType; }
    public class Renderer { public int Count; public Action OnDirty; public void SetAllGraphicsDirty() { Count++; Action callback = OnDirty; OnDirty = null; if (callback != null) callback(); } }
    public class Drawer { public Renderer renderer = new Renderer(); }
    public struct Stage
    {
        public int Value;
        public bool Newborn() { return Value == 0; }
        public bool Baby() { return Value == 1; }
        public bool Child() { return Value == 2; }
        public bool Adult() { return Value == 3; }
    }
    public class Pawn
    {
        public bool Destroyed;
        public bool IsMugirl = true;
        public Story story = new Story();
        public Drawer Drawer = new Drawer();
        public Pawn_AgeTracker ageTracker = new Pawn_AgeTracker();
        public Stage DevelopmentalStage = new Stage { Value = 3 };
    }
    public static class PawnsFinder { public static List<Pawn> AllMapsWorldAndTemporary_Alive = new List<Pawn>(); }
}
namespace Mugirl
{
    public static class MountedPawnUtility { public static bool IsMugirl(Verse.Pawn pawn) { return pawn != null && pawn.IsMugirl; } }
    public static class MugirlIdentity { public static bool HasMugirlBody(Verse.Pawn pawn) { return MountedPawnUtility.IsMugirl(pawn); } }
    public static class MugirlGameUtility { public static bool Playing = true; public static bool IsPlaying() { return Playing; } }
    public static class MugirlTickUtility { public static int Tick; public static bool Available = true; public static bool TryGetCurrentGameTick(out int tick) { tick = Tick; return Available; } }

    public static class PerfRenderingTests
    {
        public static void Main() { Console.WriteLine(Run()); }
        private static int assertions;
        private static FieldInfo PendingField = typeof(PawnRenderingRefreshUtility).GetField("pendingRefreshes", BindingFlags.Static | BindingFlags.NonPublic);
        private static void Assert(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
        private static int Count(Verse.Pawn pawn) { return pawn.Drawer.renderer.Count; }
        private static int PendingCount() { return ((IDictionary)PendingField.GetValue(null)).Count; }
        private static void Reset(int tick = 100)
        {
            PawnRenderingRefreshUtility.ClearPendingRefreshes();
            MugirlTickUtility.Tick = tick;
            MugirlTickUtility.Available = true;
            MugirlGameUtility.Playing = true;
            RimWorld.PortraitsCache.Count = 0;
        }
        private static void Tick(int tick) { MugirlTickUtility.Tick = tick; PawnRenderingRefreshUtility.TickPendingRefreshes(); }

        public static string Run()
        {
            Reset();
            var pawn = new Verse.Pawn();
            PawnRenderingRefreshUtility.NotifyPawnChanged(pawn);
            Assert(Count(pawn) == 1, "Immediate refresh");
            Tick(129); Assert(Count(pawn) == 1, "No early refresh before 30 ticks");
            Tick(130); Assert(Count(pawn) == 2, "Early refresh at 30 ticks");
            Tick(399); Assert(Count(pawn) == 2, "No intervening repeated refresh");
            Tick(400); Assert(Count(pawn) == 3 && PendingCount() == 0, "Final refresh at 300 ticks and removal");
            Tick(1000); Assert(Count(pawn) == 3, "Final refresh runs only once");

            Reset(); pawn = new Verse.Pawn();
            PawnRenderingRefreshUtility.NotifyPawnChanged(pawn);
            MugirlTickUtility.Tick = 115;
            PawnRenderingRefreshUtility.NotifyPawnChanged(pawn);
            Tick(130); Assert(Count(pawn) == 2, "New notification resets old early deadline");
            Tick(145); Assert(Count(pawn) == 3, "Replacement early deadline");
            Tick(400); Assert(Count(pawn) == 3, "Old final deadline does not refresh");
            Tick(415); Assert(Count(pawn) == 4 && PendingCount() == 0, "Replacement final deadline");

            Reset(); pawn = new Verse.Pawn();
            PawnRenderingRefreshUtility.NotifyPawnChanged(pawn);
            Tick(700); Assert(Count(pawn) == 2 && PendingCount() == 0, "Overdue work coalesces into one final refresh");

            Reset(); pawn = new Verse.Pawn();
            PawnRenderingRefreshUtility.NotifyPawnChanged(pawn); pawn.Destroyed = true;
            Tick(130); Assert(Count(pawn) == 1 && PendingCount() == 0, "Destroyed pawn cleanup");

            Reset(); pawn = new Verse.Pawn();
            PawnRenderingRefreshUtility.NotifyPawnChanged(pawn);
            new GameComponent_GhoulRenderingRefresh(new Verse.Game());
            Tick(400); Assert(Count(pawn) == 1 && PendingCount() == 0, "New game construction clears pending pawn references");
            Assert((int)typeof(PawnRenderingRefreshUtility).GetField("nextPendingRefreshTick", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == int.MaxValue, "Clear resets timing sentinel");
            Assert(((ICollection)typeof(PawnRenderingRefreshUtility).GetField("tmpPawnsToProcess", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).Count == 0, "Clear resets scratch pawn references");

            Reset(); pawn = new Verse.Pawn(); var second = new Verse.Pawn();
            PawnRenderingRefreshUtility.NotifyPawnChanged(pawn);
            pawn.Drawer.renderer.OnDirty = () => PawnRenderingRefreshUtility.NotifyPawnChanged(second);
            Tick(130); Assert(Count(pawn) == 2 && Count(second) == 1, "Reentrant notification does not invalidate dictionary enumeration");
            Tick(160); Assert(Count(second) == 2, "Reentrant entry retains early schedule");
            Tick(400); Assert(Count(pawn) == 3 && Count(second) == 2, "Independent final schedule");
            Tick(430); Assert(Count(second) == 3 && PendingCount() == 0, "Reentrant entry retains final schedule");

            Reset(); pawn = new Verse.Pawn(); MugirlTickUtility.Available = false;
            PawnRenderingRefreshUtility.NotifyPawnChanged(pawn);
            Assert(Count(pawn) == 1 && PendingCount() == 0, "No clock still refreshes immediately without retaining pawn");

            Reset(); pawn = new Verse.Pawn(); pawn.story.bodyType = RimWorld.BodyTypeDefOf.Female;
            Assert(!LifeStageVisualService.NormalizeBodyType(pawn, true) && Count(pawn) == 0 && PendingCount() == 0, "Correct body type does not dirty or enqueue");
            pawn.story.bodyType = RimWorld.BodyTypeDefOf.Child;
            Assert(LifeStageVisualService.NormalizeBodyType(pawn, true) && Count(pawn) == 1 && RimWorld.PortraitsCache.Count == 1, "Body correction triggers exactly one immediate render refresh");
            Tick(130); Tick(400);
            Assert(Count(pawn) == 3 && pawn.story.bodyType == RimWorld.BodyTypeDefOf.Female, "Changed body retains early and final refresh");

            Reset(); pawn = new Verse.Pawn(); pawn.IsMugirl = false;
            LifeStageVisualService.RefreshVisuals(pawn, true);
            Assert(Count(pawn) == 1 && PendingCount() == 0, "Non-Mugirl public refresh retains immediate fallback");
            return assertions + " assertions passed against the actual two source files.";
        }
    }
}

'@

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('mugirl-rendering-tests-' + [Guid]::NewGuid().ToString('N'))
$harnessPath = Join-Path $testRoot 'Harness.cs'
$testExe = Join-Path $testRoot 'RenderingTests.exe'
try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    [IO.File]::WriteAllText($harnessPath, $harnessSource, [Text.UTF8Encoding]::new($false))
    $sources = @(
        (Join-Path $modRoot '1.6\Source\Features\Misc\Harmony_GhoulRenderingRefresh.cs'),
        (Join-Path $modRoot '1.6\Source\Features\Newborn\LifeStageVisualService.cs'),
        $harnessPath
    )
    & $CscPath /nologo /langversion:7.2 /target:exe "/out:$testExe" @sources
    if ($LASTEXITCODE -ne 0) { throw 'Rendering test compilation failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Rendering tests failed.' }
}
finally {
    foreach ($testFile in @($harnessPath, $testExe)) {
        if (Test-Path -LiteralPath $testFile) { Remove-Item -LiteralPath $testFile }
    }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot }
}
