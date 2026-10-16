using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mugirl.Features.Roping;
using Verse;

namespace Mugirl.Tests.SaveUpgrade.Modules.Restraints;

[CollectionDefinition("Pending hitch", DisableParallelization = true)]
public sealed class PendingHitchCollection;

[Collection("Pending hitch")]
public sealed class PendingHitchRegistryTests
{
    [Fact]
    public void RemovingOneMapEntryDoesNotClearAnotherEntryForTheSamePawn()
    {
        PendingHitchRegistry.Reset();
        try
        {
            Pawn pawn = new() { def = new ThingDef { defName = "Mugirl_TestPawn" }, thingIDNumber = 1 };
            Map oldMap = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
            Map newMap = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
            var pending = (ConditionalWeakTable<Map, HashSet<Pawn>>)typeof(PendingHitchRegistry).GetField("Pending", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            pending.GetOrCreateValue(oldMap).Add(pawn);
            pending.GetOrCreateValue(newMap).Add(pawn);
            typeof(PendingHitchRegistry).GetField("totalPending", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, 2);
            PendingHitchRegistry.Clear(pawn, newMap);
            Assert.Contains(pawn, pending.GetOrCreateValue(oldMap));
            Assert.DoesNotContain(pawn, pending.GetOrCreateValue(newMap));
            Assert.True(PendingHitchRegistry.AnyPending);
            PendingHitchRegistry.Clear(pawn, oldMap);
            Assert.False(PendingHitchRegistry.AnyPending);
        }
        finally
        {
            PendingHitchRegistry.Reset();
        }
    }

    [Fact]
    public void UnspawnedPawnCannotBeMarkedAndResetDropsEveryMap()
    {
        PendingHitchRegistry.Reset();
        Pawn pawn = new();
        Map map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        PendingHitchRegistry.Mark(pawn, map);
        Assert.False(PendingHitchRegistry.AnyPending);
        Assert.False(PendingHitchRegistry.Contains(pawn, map));
        Assert.False(RopingService.IsPendingSpotRope(null));
    }
}
