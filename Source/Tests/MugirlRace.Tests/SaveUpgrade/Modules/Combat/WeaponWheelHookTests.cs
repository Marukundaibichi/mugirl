using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.IO;
using System.Runtime.CompilerServices;
using System.Linq;
using HarmonyLib;
using Mugirl.Features.WeaponWheel;
using Mugirl.Features.Lances;
using Verse;

namespace Mugirl.Tests.SaveUpgrade.Modules.Combat;

[CollectionDefinition("Combat hooks", DisableParallelization = true)]
public sealed class CombatHookCollection;

[Collection("Combat hooks")]
public sealed class WeaponWheelHookTests
{
    [Fact]
    public void QueriesKeepRegistrationOrderShortCircuitAndIgnoreDuplicateRegistration()
    {
        var hooks = new WeaponWheelHookSet();
        List<string> calls = [];
        hooks.AddEligibilityVeto("first", _ => { calls.Add("first"); return false; });
        hooks.AddEligibilityVeto("second", _ => { calls.Add("second"); return true; });
        hooks.AddEligibilityVeto("third", _ => { calls.Add("third"); return true; });
        hooks.AddEligibilityVeto("first", _ => throw new InvalidOperationException("duplicate"));
        Assert.True(hooks.IsEligibilityVetoed(null));
        Assert.Equal(new[] { "first", "second" }, calls);
        Assert.True(hooks.IsRegistered("first"));
        Assert.False(hooks.IsRegistered("absent"));
    }

    [Fact]
    public void SwapRestoresBeforeNotificationAndOnlyOnceOnFailureOrSuccess()
    {
        List<string> calls = [];
        var wheel = new CompWeaponWheel();
        var guard = new Guard(calls);
        var scope = new WeaponWheelHookSet.PrimarySwapScope(wheel, [guard], []);
        scope.PrepareTokens();
        scope.SetToken(0, new object());
        calls.Add("body");
        scope.Restore();
        scope.NotifySucceeded(true);
        scope.Restore();
        Assert.Equal(new[] { "body", "restore" }, calls);
    }

    [Theory]
    [InlineData(0, "detach,capture,body,restore,notify")]
    [InlineData(1, "detach,capture,body,restore")]
    [InlineData(2, "detach,capture,body,restore")]
    [InlineData(3, "detach,capture")]
    public void ActualHarmonyPriorityAndShortCircuitContractMatchesTheHookBoundary(int mode, string expected)
    {
        LegacyProbe.Calls.Clear();
        var harmony = new Harmony("Mugirl.Tests.CombatHookOrder");
        try
        {
            harmony.CreateClassProcessor(typeof(OldGuardPatch)).Patch();
            harmony.CreateClassProcessor(typeof(OldNotificationPatch)).Patch();
            if (mode == 2)
            {
                Assert.Throws<InvalidOperationException>(() => LegacyProbe.Invoke(mode));
            }
            else
            {
                Assert.Equal(mode == 0, LegacyProbe.Invoke(mode));
            }
            Assert.Equal(expected, string.Join(',', LegacyProbe.Calls));
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [Fact]
    public void TickAndGeneratedCallbacksKeepInputAndRegistrationOrder()
    {
        var hooks = new WeaponWheelHookSet();
        var wheel = new CompWeaponWheel();
        List<string> calls = [];
        hooks.AddTickCompleted("one", value => { Assert.Same(wheel, value); calls.Add("one"); });
        hooks.AddTickCompleted("two", _ => calls.Add("two"));
        hooks.AddGeneratedWeaponInitializer("ammo", (pawn, weapon) => { Assert.Null(pawn); Assert.Null(weapon); calls.Add("ammo"); });
        hooks.TickCompleted(wheel);
        hooks.InitializeGeneratedWeapon(null, null);
        Assert.Equal(new[] { "one", "two", "ammo" }, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualComponentRunsTickCallbackAfterEarlyReturnButNotAfterAnException(bool fail)
    {
        var harmony = new Harmony("Mugirl.Tests.CombatTickBoundary");
        TickProbe.Wheels.Clear();
        var wheel = new CompWeaponWheel { props = fail ? new CompProperties() : new CompProperties_WeaponWheel() };
        try
        {
            harmony.CreateClassProcessor(typeof(TickProbe)).Patch();
            if (fail)
            {
                Assert.Throws<InvalidCastException>(wheel.CompTick);
                Assert.Empty(TickProbe.Wheels);
            }
            else
            {
                wheel.CompTick(); // No parent Pawn: the real TickCore returns before maintenance.
                Assert.Same(wheel, Assert.Single(TickProbe.Wheels));
            }
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            TickProbe.Wheels.Clear();
        }
    }

    [Fact]
    public void DrawOwnershipContractHasExactlyTheOriginalLanceFlyerImplementer()
    {
        // Inspect our InterfaceImpl rows without loading unrelated optional HAR base classes.
        using FileStream stream = File.OpenRead(typeof(CompWeaponWheel).Assembly.Location);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        TypeDefinitionHandle contract = metadata.TypeDefinitions.Single(handle =>
        {
            TypeDefinition type = metadata.GetTypeDefinition(handle);
            return metadata.GetString(type.Namespace) == typeof(ICustomEquippedWeaponDrawOwner).Namespace
                && metadata.GetString(type.Name) == nameof(ICustomEquippedWeaponDrawOwner);
        });
        string[] owners = metadata.TypeDefinitions.Where(handle => metadata.GetTypeDefinition(handle)
                .GetInterfaceImplementations().Any(implementation => metadata.GetInterfaceImplementation(implementation).Interface == contract))
            .Select(handle =>
            {
                TypeDefinition type = metadata.GetTypeDefinition(handle);
                return metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
            }).ToArray();
        Assert.Equal(new[] { typeof(PawnFlyer_LanceCharge).FullName }, owners);
        Assert.True(typeof(PawnFlyer_LanceCharge).IsSealed);
    }

    private sealed class Guard(List<string> calls) : IWeaponWheelPrimarySwapGuard
    {
        public bool TryBeginPrimarySwap(CompWeaponWheel wheel, out object? token)
        {
            calls.Add("detach");
            token = new object();
            return true;
        }
        public void EndPrimarySwap(CompWeaponWheel wheel, object token) => calls.Add("restore");
    }

    [HarmonyPatch(typeof(WeaponWheelHookSet), nameof(WeaponWheelHookSet.TickCompleted))]
    private static class TickProbe
    {
        internal static readonly List<CompWeaponWheel> Wheels = [];
        private static bool Prefix(CompWeaponWheel wheel)
        {
            Wheels.Add(wheel);
            return false;
        }
    }

    private static class LegacyProbe
    {
        internal static readonly List<string> Calls = [];
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Invoke(int mode)
        {
            Calls.Add("body");
            return mode == 2 ? throw new InvalidOperationException("probe") : mode == 0;
        }
    }

    private sealed class TransferState
    {
        private bool restored;
        internal void Restore()
        {
            if (!restored)
            {
                restored = true;
                LegacyProbe.Calls.Add("restore");
            }
        }
    }

    [HarmonyPatch(typeof(LegacyProbe), nameof(LegacyProbe.Invoke))]
    private static class OldGuardPatch
    {
        [HarmonyPriority(Priority.Normal)]
        private static bool Prefix(int mode, ref bool __result, out TransferState? __state)
        {
            LegacyProbe.Calls.Add("detach");
            __state = mode == 3 ? null : new TransferState();
            if (mode == 3)
            {
                __result = false;
                return false;
            }
            return true;
        }
        [HarmonyPriority(Priority.Normal)]
        private static void Postfix(TransferState? __state) => __state?.Restore();
        private static Exception? Finalizer(Exception? __exception, TransferState? __state)
        {
            __state?.Restore();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(LegacyProbe), nameof(LegacyProbe.Invoke))]
    private static class OldNotificationPatch
    {
        [HarmonyPriority(Priority.Normal - 1)]
        private static void Prefix(out bool __state)
        {
            LegacyProbe.Calls.Add("capture");
            __state = true;
        }
        [HarmonyPriority(Priority.Normal - 1)]
        private static void Postfix(bool __state, bool __result)
        {
            if (__state && __result)
            {
                LegacyProbe.Calls.Add("notify");
            }
        }
    }
}
