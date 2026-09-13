$ErrorActionPreference = 'Stop'

# Compile the actual edited methods in isolation. The stubs below model only the
# list and save lifecycle used here; this is not a RimWorld runtime integration test.
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$source = Get-Content -LiteralPath (Join-Path $repoRoot '1.6\Source\Features\Milk\CompMooMilkable.cs') -Raw
$ensure = [regex]::Match($source, '(?s)        private void EnsureLactationHediff\(\).*?(?=\r?\n        public override void PostExposeData)').Value
$expose = [regex]::Match($source, '(?s)        public override void PostExposeData\(\).*?(?=\r?\n        //)').Value
$cacheField = [regex]::Match($source, '        private int cachedLactationHediffIndex = -1;').Value
if (!$ensure -or !$expose -or !$cacheField) { throw 'Source extraction failed; review method boundaries.' }

$prefix = @'
using System;
using System.Collections.Generic;
namespace MilkCacheBehaviorCheck {
public sealed class HediffDef {
    public static int Comparisons;
    public static bool operator ==(HediffDef left, HediffDef right) { Comparisons++; return ReferenceEquals(left, right); }
    public static bool operator !=(HediffDef left, HediffDef right) { return !(left == right); }
    public override bool Equals(object other) { return ReferenceEquals(this, other); }
    public override int GetHashCode() { return base.GetHashCode(); }
}
public sealed class Hediff { public HediffDef def; public Hediff(HediffDef value) { def = value; } }
public sealed class HediffSet { public List<Hediff> hediffs = new List<Hediff>(); }
public sealed class Health {
    public HediffSet hediffSet = new HediffSet();
    public int AddCalls;
    public Action<HediffDef> AddAction;
    public void AddHediff(HediffDef def) {
        AddCalls++;
        if (AddAction != null) AddAction(def); else hediffSet.hediffs.Add(new Hediff(def));
    }
}
public sealed class Pawn { public Health health = new Health(); }
public static class MugirlRequiredDefs { public static class Hediffs { public static HediffDef MugirlLactation = new HediffDef(); } }
public enum LoadSaveMode { Inactive, PostLoadInit }
public static class Scribe { public static LoadSaveMode mode; }
public class ProbeBase { public virtual void PostExposeData() {} }
public sealed class Probe : ProbeBase {
    public Pawn pawn = new Pawn();
    public bool active = true;
    public int ActiveReads;
    private object cachedMilkingDevice;
    private Pawn MooPawn { get { return pawn; } }
    private bool Active { get { ActiveReads++; return active; } }
    public int CachedIndex { get { return cachedLactationHediffIndex; } }
    public object CachedDevice { get { return cachedMilkingDevice; } }
    public void Tick() { EnsureLactationHediff(); }
'@

$suffix = @'
}
public static class Checks {
    private static int assertions;
    private static HediffDef Target { get { return MugirlRequiredDefs.Hediffs.MugirlLactation; } }
    private static Hediff Other() { return new Hediff(new HediffDef()); }
    private static int TargetCount(Probe p) {
        int count = 0;
        foreach (Hediff h in p.pawn.health.hediffSet.hediffs) if (ReferenceEquals(h.def, Target)) count++;
        return count;
    }
    private static void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception(name); }
    public static string Run() {
        Probe p = new Probe();
        var list = p.pawn.health.hediffSet.hediffs;
        for (int i = 0; i < 100; i++) list.Add(Other());
        p.Tick();
        Check(TargetCount(p) == 1 && p.pawn.health.AddCalls == 1, "Initial active tick adds target");
        p.Tick();
        Check(p.CachedIndex == 100, "Next tick locates actual list position");
        HediffDef.Comparisons = 0;
        int activeReads = p.ActiveReads;
        p.Tick();
        Check(HediffDef.Comparisons <= 2 && p.ActiveReads == activeReads, "Stable tick checks one list slot without another Active evaluation");

        list.RemoveAt(p.CachedIndex);
        list.Add(Other());
        p.Tick();
        Check(TargetCount(p) == 1 && p.pawn.health.AddCalls == 2, "Same-count removal and replacement restores on first tick");
        p.Tick();
        list.RemoveAt(0);
        p.Tick();
        Check(TargetCount(p) == 1 && p.pawn.health.AddCalls == 2 && p.CachedIndex == list.Count - 1, "Removal before cached position relocates without duplicate");
        list.Insert(0, Other());
        p.Tick();
        Check(TargetCount(p) == 1 && p.pawn.health.AddCalls == 2 && p.CachedIndex == list.Count - 1, "Insertion before cached position relocates");
        list.Reverse();
        p.Tick();
        Check(p.CachedIndex == 0 && p.pawn.health.AddCalls == 2, "Reorder relocates");

        p.pawn.health.hediffSet = new HediffSet();
        list = p.pawn.health.hediffSet.hediffs;
        list.Add(Other());
        p.Tick();
        Check(TargetCount(p) == 1 && p.pawn.health.AddCalls == 3, "Replacing whole HediffSet with absent target restores");
        p.Tick();
        p.pawn.health.hediffSet = new HediffSet();
        list = p.pawn.health.hediffSet.hediffs;
        list.Add(Other());
        list.Add(new Hediff(Target));
        p.Tick();
        Check(TargetCount(p) == 1 && p.pawn.health.AddCalls == 3, "Replacement target in same slot is accepted");

        Scribe.mode = LoadSaveMode.PostLoadInit;
        p.PostExposeData();
        Scribe.mode = LoadSaveMode.Inactive;
        Check(p.CachedIndex == -1 && p.CachedDevice == null, "PostLoadInit clears transient caches");
        p.Tick();
        Check(p.CachedIndex == 1 && p.pawn.health.AddCalls == 3, "Read-loaded target rediscovered without duplicate");

        p.active = false;
        list.Clear();
        p.Tick();
        Check(TargetCount(p) == 0 && p.pawn.health.AddCalls == 3, "Inactive source does not add");
        p.active = true;
        p.Tick();
        Check(TargetCount(p) == 1 && p.pawn.health.AddCalls == 4, "Reactivation adds on first tick");

        Probe blocked = new Probe();
        blocked.pawn.health.AddAction = def => {};
        blocked.Tick(); blocked.Tick();
        Check(blocked.pawn.health.AddCalls == 2 && TargetCount(blocked) == 0, "Blocked AddHediff remains retryable each tick");
        blocked.pawn.health.AddAction = def => blocked.pawn.health.hediffSet.hediffs.Insert(0, new Hediff(def));
        blocked.Tick(); blocked.Tick();
        Check(blocked.pawn.health.AddCalls == 3 && blocked.CachedIndex == 0 && TargetCount(blocked) == 1, "Add callback insertion uses actual list state");

        blocked.pawn.health.hediffSet.hediffs.Add(new Hediff(Target));
        blocked.pawn.health.hediffSet.hediffs.RemoveAt(0);
        blocked.Tick();
        Check(TargetCount(blocked) == 1 && blocked.pawn.health.AddCalls == 3, "Removing one duplicate leaves existing target without new addition");
        return "PASS: " + assertions + " assertions against extracted production methods; isolated stubs, no game assembly rebuilt.";
    }
}
}
'@

Add-Type -TypeDefinition ($prefix + $cacheField + "`n" + $ensure + "`n" + $expose + $suffix)
[MilkCacheBehaviorCheck.Checks]::Run()
