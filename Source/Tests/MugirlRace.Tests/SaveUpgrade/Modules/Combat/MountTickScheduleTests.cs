using System.Reflection;
using Mugirl.Features.Mounting;
using Verse;

namespace Mugirl.Tests.SaveUpgrade.Modules.Combat;

[Collection("Mount Scribe")]
public sealed class MountTickScheduleTests
{
    [Fact]
    public void AccumulatorsPreserveDisabledWorkDebtNegativeValuesAndUncheckedOverflow()
    {
        var schedule = new MountTickSchedule { physiology = int.MaxValue - 7, safety = -7, turret = 59, melee = 999 };
        schedule.Add(8);
        Assert.Equal(unchecked(int.MaxValue + 1), schedule.physiology);
        Assert.Equal(1, schedule.safety);
        Assert.Equal(67, schedule.turret);
        Assert.Equal(1007, schedule.melee);
        Assert.False(schedule.ConsumePhysiology(150, out int physiologicalDelta));
        Assert.Equal(0, physiologicalDelta);
        Assert.True(schedule.ConsumeTurret(60, out int turretDelta));
        Assert.Equal(67, turretDelta);
        Assert.True(schedule.ConsumeMelee(60, out int meleeDelta));
        Assert.Equal(1007, meleeDelta);
        schedule.Add(250);
        Assert.True(schedule.ConsumeSafety(250, out int safetyDelta));
        Assert.Equal(251, safetyDelta);
    }

    [Fact]
    public void AllFourCountersMatchTheOriginalAccumulationContractAcrossVariableDeltas()
    {
        var random = new Random(831);
        var actual = new MountTickSchedule();
        int[] expected = [0, 0, 0, 0];
        for (int i = 0; i < 10000; i++)
        {
            int delta = i % 173 == 0 ? int.MaxValue : random.Next(-100, 700);
            for (int j = 0; j < expected.Length; j++) { expected[j] = unchecked(expected[j] + delta); }
            actual.Add(delta);
            int interval = random.Next(1, 500);
            for (int stage = 0; stage < 4; stage++)
            {
                if (random.Next(3) == 0) { continue; }
                bool ready = expected[stage] >= interval;
                int elapsed = ready ? expected[stage] : 0;
                if (ready) { expected[stage] = 0; }
                int observed;
                bool triggered = stage switch
                {
                    0 => actual.ConsumePhysiology(interval, out observed),
                    1 => actual.ConsumeSafety(interval, out observed),
                    2 => actual.ConsumeTurret(interval, out observed),
                    _ => actual.ConsumeMelee(interval, out observed),
                };
                Assert.Equal(ready, triggered);
                Assert.Equal(elapsed, observed);
            }

            Assert.Equal(expected, new[] { actual.physiology, actual.safety, actual.turret, actual.melee });
        }
    }

    [Theory]
    [InlineData(17, 0, 16)]
    [InlineData(17, -10, 16)]
    [InlineData(17, 10, 7)]
    [InlineData(17, 250, 0)]
    public void AimUsesTheSameMinimumOneDeltaAndKeepsTotalDuration(int remaining, int delta, int expected)
    {
        var turret = new MountTurretState { aimTicksLeft = remaining, aimTicksTotal = 93 };
        turret.AdvanceAim(delta);
        Assert.Equal(expected, turret.aimTicksLeft);
        Assert.Equal(93, turret.aimTicksTotal);
    }

    [Fact]
    public void ClearAimPreservesIndependentCooldownAndLastAttackFacts()
    {
        var turret = new MountTurretState
        {
            aimTarget = new LocalTargetInfo(new IntVec3(1, 0, 2)), aimTicksLeft = 17, aimTicksTotal = 93,
            castStartTick = 4001, burstCooldownTicksLeft = 27,
            lastAttackedTarget = new LocalTargetInfo(new IntVec3(3, 0, 4)), lastAttackTargetTick = 3987,
        };
        turret.ClearAim();
        Assert.False(turret.aimTarget.IsValid);
        Assert.Equal(0, turret.aimTicksLeft);
        Assert.Equal(0, turret.aimTicksTotal);
        Assert.Equal(-1, turret.castStartTick);
        Assert.Equal(27, turret.burstCooldownTicksLeft);
        Assert.Equal(new IntVec3(3, 0, 4), turret.lastAttackedTarget.Cell);
        Assert.Equal(3987, turret.lastAttackTargetTick);
    }

    [Theory]
    [InlineData(5, -4, 9)]
    [InlineData(5, 0, 5)]
    [InlineData(5, 7, 0)]
    [InlineData(int.MaxValue, -1, 0)]
    public void CooldownUsesActualDeltaWithoutNormalizingIt(int remaining, int delta, int expected)
    {
        var turret = new MountTurretState { burstCooldownTicksLeft = remaining };
        turret.AdvanceCooldown(delta);
        Assert.Equal(expected, turret.burstCooldownTicksLeft);
    }

    [Fact]
    public void MountedCasterAndWarmupRestoreAfterAnException()
    {
        var verb = new Verb_Shoot();
        var rider = new Thing();
        var carrier = new Thing();
        verb.caster = rider;
        Action fail = () =>
        {
            using var caster = new MountedVerbScope(verb, carrier);
            using var warmup = new MountedWarmupOverride(verb, 0f);
            Assert.Same(carrier, verb.caster);
            Assert.True(MountedWarmupOverride.TryGet(verb, out _));
            throw new InvalidOperationException("control");
        };
        Assert.Throws<InvalidOperationException>(fail);
        Assert.Same(rider, verb.caster);
        Assert.False(MountedWarmupOverride.TryGet(verb, out _));
    }

    [Fact]
    public void StateAdvancementDoesNotAllocatePerTick()
    {
        var schedule = new MountTickSchedule();
        var turret = new MountTurretState();
        _ = GC.GetAllocatedBytesForCurrentThread();
        for (int warmup = 0; warmup < 3; warmup++)
        {
            AdvanceState(schedule, turret, 100000);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        AdvanceState(schedule, turret, 100000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(schedule);
        GC.KeepAlive(turret);
        Assert.Equal(0, allocated);
    }

    private static void AdvanceState(MountTickSchedule schedule, MountTurretState turret, int count)
    {
        for (int i = 0; i < count; i++)
        {
            schedule.Add(1);
            schedule.ConsumePhysiology(150, out _);
            schedule.ConsumeSafety(250, out _);
            schedule.ConsumeTurret(60, out _);
            schedule.ConsumeMelee(60, out _);
            turret.AdvanceAim(1);
            turret.AdvanceCooldown(1);
        }
    }

    [Fact]
    public void FrozenCarrierIdentityApiIsPublicStaticAndNotInlined()
    {
        MethodInfo method = typeof(global::Mugirl.MountedPawnUtility).GetMethod("IsMugirl", [typeof(Pawn)])!;
        Assert.True(method.IsPublic && method.IsStatic);
        Assert.True((method.GetMethodImplementationFlags() & MethodImplAttributes.NoInlining) != 0);
        Assert.Equal("pawn", method.GetParameters()[0].Name);
    }
}
