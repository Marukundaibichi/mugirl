param()

$ErrorActionPreference = 'Stop'
$sourcePath = Join-Path $PSScriptRoot '../../1.6/Source/Features/Roping/MapRopingIndex.cs'
$source = Get-Content -LiteralPath $sourcePath -Raw
$servicePath = Join-Path $PSScriptRoot '../../1.6/Source/Features/Roping/RopingService.cs'
$serviceSource = Get-Content -LiteralPath $servicePath -Raw
$selectionMethod = [regex]::Match($serviceSource, '(?s)        public static Pawn FirstMugirlFollowing\(Pawn roper\).*?(?=        public static int CountMugirlFollowers)').Value
if ([string]::IsNullOrWhiteSpace($selectionMethod)) { throw 'Could not find production selection method.' }
$selectionSource = @'
namespace Mugirl
{
    public static class RopingService
    {
        private static bool IsMugirlRopee(Verse.Pawn pawn) { return pawn != null && pawn.IsMugirl; }
        private static bool IsFollowingRoper(Verse.Pawn pawn) { return pawn != null && pawn.Follows; }
'@ + [Environment]::NewLine + $selectionMethod + [Environment]::NewLine + '    } }'

# Compile the production index itself against small tracker/map fixtures. This verifies cache
# behavior and scheduling without starting RimWorld; it does not simulate rope jobs or saving.
$fixture = @'
namespace Verse
{
    public class Map
    {
        public int uniqueID;
        public MapPawns mapPawns = new MapPawns();
    }
    public class MapPawns
    {
        public System.Collections.Generic.List<Pawn> AllPawnsSpawned = new System.Collections.Generic.List<Pawn>();
    }
    public class Pawn
    {
        public bool Destroyed;
        public bool Spawned = true;
        public Map Map;
        public RimWorld.Pawn_RopeTracker roping = new RimWorld.Pawn_RopeTracker();
        public bool IsMugirl = true;
        public bool Follows = true;
        public TestJob CurJob = new TestJob();
    }
    public class TestJob { public TestTarget targetA = new TestTarget(); }
    public class TestTarget { public Pawn Thing; }
    public class MapComponent
    {
        protected Map map;
        public MapComponent(Map map) { this.map = map; }
        public virtual void MapComponentTick() { }
        public virtual void FinalizeInit() { }
    }
}
namespace RimWorld
{
    public class Pawn_RopeTracker
    {
        public static int TargetReads;
        public Verse.Pawn Target;
        public bool Spot;
        public System.Collections.Generic.List<Verse.Pawn> Ropees = new System.Collections.Generic.List<Verse.Pawn>();
        public Verse.Pawn RopedByPawn { get { TargetReads++; return Target; } }
        public bool IsRopedToSpot { get { return Target == null && Spot; } }
    }
}
namespace MugirlIndexTests
{
    using Mugirl;
    using Verse;
    using RimWorld;
    using System;
    using System.Collections.Generic;

    public static class Fixture
    {
        private static int assertions;
        private static void Require(bool result, string message)
        {
            assertions++;
            if (!result) throw new Exception(message);
        }
        private static Pawn Add(Map map)
        {
            Pawn pawn = new Pawn { Map = map };
            map.mapPawns.AllPawnsSpawned.Add(pawn);
            return pawn;
        }
        private static void Tick(MapRopingIndex index, int ticks = 250)
        {
            for (int i = 0; i < ticks; i++) index.MapComponentTick();
        }
        private static void ExpectRope(MapRopingIndex index, Pawn ropee, Pawn roper)
        {
            Require(index.RoperFor(ropee) == roper, "Wrong rope owner after reconciliation");
            if (roper == null) return;
            List<Pawn> list = index.RopeesFor(roper);
            Require(list != null && list.Contains(ropee), "Reverse index missing ropee");
            Require(list.IndexOf(ropee) == list.LastIndexOf(ropee), "Duplicate reverse entry");
        }

        public static string Run()
        {
            foreach (int count in new[] { 1, 2, 24, 25, 26, 99, 250, 1000 })
            {
                Map map = new Map();
                for (int i = 0; i < count; i++) Add(map);
                MapRopingIndex index = new MapRopingIndex(map);
                Pawn_RopeTracker.TargetReads = 0;
                int maxReads = 0;
                for (int i = 0; i < 250; i++)
                {
                    int before = Pawn_RopeTracker.TargetReads;
                    index.MapComponentTick();
                    maxReads = Math.Max(maxReads, Pawn_RopeTracker.TargetReads - before);
                }
                Require(Pawn_RopeTracker.TargetReads == count, "Idle maps must receive one scan per 250 ticks");
                Require(maxReads <= (count + 24) / 25, "Idle map scan exceeded its per-batch budget");
            }

            Map first = new Map { uniqueID = 7 };
            Map second = new Map { uniqueID = 2 };
            Pawn a = Add(first), b = Add(first), x = Add(first), y = Add(first), pending = Add(first);
            x.roping.Target = a;
            y.roping.Target = a;
            MapRopingIndex firstIndex = new MapRopingIndex(first);
            firstIndex.FinalizeInit();
            ExpectRope(firstIndex, x, a);
            List<Pawn> shared = firstIndex.RopeesFor(a);
            List<Pawn>.Enumerator stableEnumerator = shared.GetEnumerator();
            Require(stableEnumerator.MoveNext() && stableEnumerator.Current == x, "Unexpected initial list order");
            for (int i = 0; i < 100; i++) firstIndex.RegisterPawnRope(a, x);
            firstIndex.RebuildFromMap();
            Tick(firstIndex);
            Require(object.ReferenceEquals(shared, firstIndex.RopeesFor(a)), "Unchanged ropes replaced shared list");
            Require(shared.Count == 2 && shared[0] == x && shared[1] == y, "Repeated registrations changed list order/count");
            Require(stableEnumerator.MoveNext() && stableEnumerator.Current == y, "Unchanged maintenance invalidated shared enumerator");

            // The map has x before y, while the real rope order is y before x. Building commands
            // must retain the latter after initial/load reconciliation and ignore stale tracker entries.
            a.roping.Ropees.Add(y);
            a.roping.Ropees.Add(x);
            x.CurJob.targetA.Thing = y.CurJob.targetA.Thing = a;
            Require(RopingService.FirstMugirlFollowing(a) == y, "Building selection followed map/index order");
            y.roping.Target = b;
            Require(RopingService.FirstMugirlFollowing(a) == x, "Building selection accepted stale owner");
            y.roping.Target = a;
            y.IsMugirl = false;
            Require(RopingService.FirstMugirlFollowing(a) == x, "Building selection accepted non-Mugirl");
            y.IsMugirl = true;
            y.Follows = false;
            Require(RopingService.FirstMugirlFollowing(a) == x, "Building selection accepted non-following pawn");
            y.Follows = true;
            y.CurJob.targetA.Thing = b;
            Require(RopingService.FirstMugirlFollowing(a) == x, "Building selection accepted another job target");
            y.CurJob.targetA.Thing = a;
            a.roping.Ropees.Insert(0, null);
            Require(RopingService.FirstMugirlFollowing(a) == y, "Null ropee broke ordered selection");
            Require(RopingService.FirstMugirlFollowing(null) == null, "Null roper selected a pawn");
            Require(shared[0] == x && shared[1] == y && a.roping.Ropees[1] == y && a.roping.Ropees[2] == x,
                "Building selection mutated a rope list");

            firstIndex.MarkPendingSpotRope(pending);
            Tick(firstIndex);
            Require(firstIndex.IsPendingSpotRope(pending), "Reconciliation discarded a pending spot job");
            pending.roping.Spot = true;
            Tick(firstIndex);
            Require(firstIndex.IsRopedToSpot(pending) && !firstIndex.IsPendingSpotRope(pending), "Pending spot did not complete");
            pending.roping.Spot = false;
            Tick(firstIndex);
            Require(!firstIndex.IsRopedToSpot(pending), "External unrope left stale spot entry");

            // Change the tracker without any notifications, including a relationship absent from
            // the former roper's list. The target tracker remains the authoritative state.
            x.roping.Target = b;
            Tick(firstIndex);
            ExpectRope(firstIndex, x, b);
            Require(!shared.Contains(x) && shared.Contains(y), "Reassignment damaged remaining ropees");
            y.roping.Target = null;
            y.roping.Spot = true;
            Tick(firstIndex);
            Require(firstIndex.RoperFor(y) == null && firstIndex.IsRopedToSpot(y), "Pawn-to-spot transition failed");
            Require(shared.Count == 0, "Old shared list retained detached pawn");

            // Moving both ends without callbacks must expire the old map cache and populate the new one.
            first.mapPawns.AllPawnsSpawned.Remove(b);
            first.mapPawns.AllPawnsSpawned.Remove(x);
            b.Map = second;
            x.Map = second;
            second.mapPawns.AllPawnsSpawned.Add(b);
            second.mapPawns.AllPawnsSpawned.Add(x);
            MapRopingIndex secondIndex = new MapRopingIndex(second);
            Tick(firstIndex);
            Tick(secondIndex);
            ExpectRope(firstIndex, x, null);
            ExpectRope(secondIndex, x, b);
            secondIndex.MarkPendingSpotRope(b);
            b.Destroyed = x.Destroyed = true;
            b.Spawned = x.Spawned = false;
            second.mapPawns.AllPawnsSpawned.Clear();
            Tick(secondIndex);
            ExpectRope(secondIndex, x, null);
            Require(secondIndex.RopeesFor(b) == null && !secondIndex.IsPendingSpotRope(b), "Empty map retained invalid objects");
            Pawn freshRoper = Add(second), freshRopee = Add(second);
            freshRopee.roping.Target = freshRoper;
            Tick(secondIndex);
            ExpectRope(secondIndex, freshRopee, freshRoper);

            // Deterministic mixed mutations verify both directions of the index after each sweep.
            Map changing = new Map { uniqueID = 19 };
            List<Pawn> all = changing.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < 100; i++) Add(changing);
            MapRopingIndex changingIndex = new MapRopingIndex(changing);
            Random random = new Random(12009);
            for (int round = 0; round < 40; round++)
            {
                for (int i = 10; i < all.Count; i++)
                {
                    int target = random.Next(12);
                    all[i].roping.Target = target < 10 ? all[target] : null;
                    all[i].roping.Spot = target == 10;
                    if ((i + round) % 3 == 0 && target < 10)
                        changingIndex.RegisterPawnRope(all[target], all[i]);
                }
                Tick(changingIndex);
                for (int i = 10; i < all.Count; i++)
                {
                    ExpectRope(changingIndex, all[i], all[i].roping.Target);
                    Require(changingIndex.IsRopedToSpot(all[i]) == all[i].roping.Spot, "Spot state drift");
                }
                for (int i = 0; i < 10; i++)
                {
                    List<Pawn> indexed = changingIndex.RopeesFor(all[i]);
                    if (indexed == null) continue;
                    foreach (Pawn ropee in indexed)
                        Require(ropee.roping.Target == all[i], "Reverse index retained obsolete owner");
                }
            }
            MapRopingIndex loadedIndex = new MapRopingIndex(changing);
            loadedIndex.FinalizeInit();
            for (int i = 10; i < all.Count; i++) ExpectRope(loadedIndex, all[i], all[i].roping.Target);
            return "PASS: " + assertions + " assertions; scan budgets, immediate initialization, list identity, ordered building selection, notifications, external mutations, spot/pending transitions, cross-map and empty-map cleanup.";
        }
    }
}
'@

Add-Type -TypeDefinition ($source + [Environment]::NewLine + $selectionSource + [Environment]::NewLine + $fixture)
[MugirlIndexTests.Fixture]::Run()
