param([string]$CscPath)
$ErrorActionPreference = 'Stop'
$modRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $CscPath) {
    $CscPath = Join-Path (Split-Path (Get-Command MSBuild.exe).Source -Parent) 'Roslyn\csc.exe'
}
# Execute the production patch with controlled HAR input and rendering stubs.
# These tests check invalidation and selection preservation, not Unity rendering.
$harness = @'
using System;
using System.Collections.Generic;
using Mugirl;
using AlienRace;
using UnityEngine;
using Verse;
namespace HarmonyLib {
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type t, string n) {} }
}
namespace UnityEngine {
    public enum EventType { Layout, Repaint, MouseDown, Used }
    public class Event { public static Event current; public EventType type; }
    public struct Color {
        public int value;
        public static bool operator ==(Color a, Color b) { return a.value == b.value; }
        public static bool operator !=(Color a, Color b) { return !(a == b); }
        public override bool Equals(object o) { return o is Color && this == (Color)o; }
        public override int GetHashCode() { return value; }
    }
}
namespace Verse {
    public class Pawn { public bool mugirl = true; public Drawer Drawer = new Drawer(); }
    public class Drawer { public Renderer renderer = new Renderer(); }
    public class Renderer { public int dirty; public void SetAllGraphicsDirty() { dirty++; } }
}
namespace RimWorld {
    public static class PortraitsCache { public static int dirty; public static void SetDirty(Pawn p) { dirty++; } }
}
namespace AlienRace {
    public static class StylingStation { public static void DoRaceTabs() {} }
    public class AlienPartGenerator {
        public class Pair<T> { public T first; public T second; }
        public class AlienComp {
            public List<int> addonVariants = new List<int> { 0, 0 };
            public List<Pair<Color?>> addonColors = new List<Pair<Color?>> { new Pair<Color?>() };
            public Dictionary<string, Pair<Color>> ColorChannels = new Dictionary<string, Pair<Color>> {
                { "skin", new Pair<Color>() }
            };
        }
    }
}
namespace Mugirl {
    public static class MugirlIdentity { public static bool IsMugirlPawn(Pawn p) { return p != null && p.mugirl; } }
}
class Tests {
    static int assertions;
    static void Check(bool ok, string message) { assertions++; if (!ok) throw new Exception(message); }
    static void Input(Pawn pawn, AlienPartGenerator.AlienComp comp, Action change) {
        Event.current = new Event { type = EventType.MouseDown };
        Harmony_StylingStationRefresh.AppearanceSnapshot state;
        Harmony_StylingStationRefresh.Prefix(pawn, comp, out state);
        change();
        Event.current.type = EventType.Used;
        Harmony_StylingStationRefresh.Postfix(pawn, comp, state);
    }
    static void Main() {
        var pawn = new Pawn(); var comp = new AlienPartGenerator.AlienComp();
        Input(pawn, comp, () => { comp.addonVariants[0] = 2; comp.addonVariants[1] = 2; });
        Check(pawn.Drawer.renderer.dirty == 1 && RimWorld.PortraitsCache.dirty == 1, "First click refreshes pawn and portrait once");
        Check(comp.addonVariants[0] == 2 && comp.addonVariants[1] == 2, "Linked selection is preserved");
        Input(pawn, comp, () => {});
        Check(pawn.Drawer.renderer.dirty == 1, "Unchanged selection does not rebuild graphics");
        Input(pawn, comp, () => comp.addonColors[0].first = new Color { value = 3 });
        Check(pawn.Drawer.renderer.dirty == 2, "In-place addon color changes refresh immediately");
        Input(pawn, comp, () => comp.addonColors[0].first = null);
        Check(pawn.Drawer.renderer.dirty == 3, "Clearing a color override refreshes immediately");
        Input(pawn, comp, () => comp.ColorChannels["skin"].second = new Color { value = 5 });
        Check(pawn.Drawer.renderer.dirty == 4, "In-place channel changes refresh immediately");
        foreach (var kind in new[] { EventType.Layout, EventType.Repaint }) {
            Event.current = new Event { type = kind };
            Harmony_StylingStationRefresh.AppearanceSnapshot state;
            Harmony_StylingStationRefresh.Prefix(pawn, comp, out state);
            Check(state == null, "Drawing passes allocate no snapshot");
        }
        pawn.mugirl = false;
        Input(pawn, comp, () => comp.addonVariants[0] = 1);
        Check(pawn.Drawer.renderer.dirty == 4, "Other races retain original behavior");
        pawn.mugirl = true;
        Input(pawn, comp, () => comp.addonVariants[0] = 0);
        Check(comp.addonVariants[0] == 0 && pawn.Drawer.renderer.dirty == 5, "Returning to the initial option does not replay an earlier selection");
        Console.WriteLine(assertions + " styling refresh assertions passed.");
    }
}
'@
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('mugirl-styling-tests-' + [Guid]::NewGuid().ToString('N'))
$harnessPath = Join-Path $testRoot 'Harness.cs'
$testExe = Join-Path $testRoot 'StylingTests.exe'
try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    [IO.File]::WriteAllText($harnessPath, $harness, [Text.UTF8Encoding]::new($false))
    & $CscPath /nologo /langversion:7.2 /target:exe "/out:$testExe" $harnessPath (Join-Path $modRoot '1.6\Source\Compatibility\OtherMods\Harmony_StylingStationRefresh.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Styling tests compilation failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Styling tests failed.' }
}
finally {
    foreach ($testFile in @($harnessPath, $testExe)) {
        if (Test-Path -LiteralPath $testFile) { Remove-Item -LiteralPath $testFile }
    }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot }
}
