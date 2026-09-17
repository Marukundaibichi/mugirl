$ErrorActionPreference = 'Stop'

# Exercise the production facing method with lightweight game-state stubs.
# This checks direction and scope; it does not replace an in-game rendering check.
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$source = Get-Content -LiteralPath (Join-Path $repoRoot '1.6\Source\Features\Milk\MugirlMilkingAnimation.cs') -Raw
$method = [regex]::Match($source, '(?s)        internal static void AdjustDrawFacing\(.*?(?=\r?\n        private static void FaceEachOther)').Value
$roles = [regex]::Match($source, '(?s)    public enum MugirlMilkingVisualRole.*?\n    }').Value
if (!$method -or !$roles) { throw 'Source extraction failed; review method boundaries.' }

$harness = @'
using System;
using System.Collections.Generic;
namespace MilkingFacingChecks {
[Flags] public enum PawnRenderFlags { None = 0, Portrait = 1, Cache = 2, Statue = 4 }
public static class FlagExtensions {
    public static bool FlagSet(this PawnRenderFlags value, PawnRenderFlags flag) { return (value & flag) != 0; }
}
public enum Rot4 { North, East, South, West }
public struct Vector3 {
    public float x, y, z;
    public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    public float sqrMagnitude { get { return x*x + y*y + z*z; } }
    public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x, a.y-b.y, a.z-b.z); }
    public Vector3 ToVector3() { return this; }
    public float AngleFlat() { return (float)((Math.Atan2(x, z) * 180 / Math.PI + 360) % 360); }
}
public static class Pawn_RotationTracker {
    // Thresholds verified against the installed game's Pawn_RotationTracker.
    public static Rot4 RotFromAngleBiased(float angle) {
        if (angle < 30) return Rot4.North;
        if (angle < 150) return Rot4.East;
        if (angle < 210) return Rot4.South;
        if (angle < 330) return Rot4.West;
        return Rot4.North;
    }
}
public class Pawn {
    public int thingIDNumber;
    public object Map;
    public Vector3 DrawPos, Position;
    public Rot4 Rotation = Rot4.South;
    public bool Spawned = true, Destroyed;
}
public static class MugirlTickUtility {
    public static int Now = 100;
    public static bool Available = true;
    public static bool TryGetCurrentGameTick(out int now) { now = Now; return Available; }
}
__ROLES__
public static class Checks {
    private const int StaleAfterTicks = 6;
    private class MilkingVisualState {
        public Pawn pawn, partner;
        public MugirlMilkingVisualRole role;
        public int lastTick = 100;
    }
    private static readonly Dictionary<int, MilkingVisualState> states = new Dictionary<int, MilkingVisualState>();
    private static bool Valid(Pawn pawn) { return pawn != null && !pawn.Destroyed && pawn.Spawned && pawn.Map != null; }
__METHOD__
    private static int assertions;
    private static void Expect(Pawn pawn, Rot4 expected, string name, PawnRenderFlags flags = PawnRenderFlags.None) {
        Rot4 facing = Rot4.South;
        AdjustDrawFacing(pawn, flags, ref facing);
        assertions++;
        if (facing != expected) throw new Exception(name + ": " + facing + " != " + expected);
        if (pawn != null && pawn.Rotation != Rot4.South) throw new Exception("Rendering changed simulation rotation");
    }
    public static string Run() {
        object map = new object();
        Pawn a = new Pawn { thingIDNumber = 1, Map = map };
        Pawn b = new Pawn { thingIDNumber = 2, Map = map };
        var sa = new MilkingVisualState { pawn = a, partner = b, role = MugirlMilkingVisualRole.Helper };
        var sb = new MilkingVisualState { pawn = b, partner = a, role = MugirlMilkingVisualRole.AssistedTarget };
        states[1] = sa; states[2] = sb;
        int[,] offsets = { {0,1}, {1,0}, {0,-1}, {-1,0}, {1,1}, {1,-1}, {-1,-1}, {-1,1} };
        Rot4[] expected = { Rot4.North, Rot4.East, Rot4.South, Rot4.West, Rot4.East, Rot4.East, Rot4.West, Rot4.West };
        for (int i = 0; i < 8; i++) {
            b.DrawPos = b.Position = new Vector3(offsets[i,0], 0, offsets[i,1]);
            Expect(a, expected[i], "helper direction " + i);
            Expect(b, (Rot4)(((int)expected[i] + 2) % 4), "target direction " + i);
        }
        b.DrawPos = new Vector3(1, 20, 0);
        Expect(a, Rot4.East, "ignore altitude");
        b.DrawPos = a.DrawPos; b.Position = new Vector3(1, 0, 0);
        Expect(a, Rot4.East, "overlapping draw positions use cells");
        b.Position = a.Position;
        Expect(a, Rot4.South, "coincident positions keep facing");
        b.DrawPos = new Vector3(1, 0, 0);
        foreach (PawnRenderFlags flag in new[] { PawnRenderFlags.Portrait, PawnRenderFlags.Cache, PawnRenderFlags.Statue })
            Expect(a, Rot4.South, "preview excluded", flag);
        foreach (MugirlMilkingVisualRole role in Enum.GetValues(typeof(MugirlMilkingVisualRole))) {
            sa.role = role;
            Expect(a, role == MugirlMilkingVisualRole.Helper || role == MugirlMilkingVisualRole.AssistedTarget ? Rot4.East : Rot4.South, "role " + role);
        }
        sa.role = MugirlMilkingVisualRole.Helper;
        MugirlTickUtility.Now = 106; Expect(a, Rot4.East, "active boundary");
        MugirlTickUtility.Now = 107; Expect(a, Rot4.South, "stale animation");
        MugirlTickUtility.Now = 100;
        MugirlTickUtility.Available = false; Expect(a, Rot4.South, "no game"); MugirlTickUtility.Available = true;
        b.Spawned = false; Expect(a, Rot4.South, "partner despawned"); b.Spawned = true;
        b.Destroyed = true; Expect(a, Rot4.South, "partner destroyed"); b.Destroyed = false;
        b.Map = new object(); Expect(a, Rot4.South, "different maps"); b.Map = map;
        sa.partner = null; Expect(a, Rot4.South, "missing partner"); sa.partner = b;
        Expect(new Pawn { thingIDNumber = 1, Map = map }, Rot4.South, "reused pawn ID");
        a.Spawned = false; Expect(a, Rot4.South, "pawn despawned"); a.Spawned = true;
        Expect(null, Rot4.South, "null pawn");
        states.Clear(); Expect(a, Rot4.South, "animation ended");
        return assertions + " milking facing checks passed.";
    }
}
}
'@
Add-Type -TypeDefinition ($harness.Replace('__ROLES__', $roles).Replace('__METHOD__', $method))
[MilkingFacingChecks.Checks]::Run()
