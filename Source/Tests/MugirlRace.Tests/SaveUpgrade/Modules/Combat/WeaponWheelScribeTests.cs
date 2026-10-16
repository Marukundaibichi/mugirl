using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Mugirl.Features.WeaponWheel;
using Verse;

namespace Mugirl.Tests.SaveUpgrade.Modules.Combat;

[Collection("Reference aliases")]
public sealed class WeaponWheelScribeTests
{
    [Fact]
    public void AssemblyScopeIsolatesAndRestoresAnEarlierNegativeTypeLookup()
    {
        var cache = (IDictionary)typeof(GenTypes).GetField("typeCache", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        object? key = null;
        using (new ModAssemblyScope())
        {
            foreach (DictionaryEntry entry in cache)
            {
                if (Equals(entry.Value, typeof(WeaponWheelSlot))) { key = entry.Key; break; }
            }
        }
        Assert.NotNull(key);
        bool existed = cache.Contains(key);
        object? previous = cache[key];
        try
        {
            cache[key] = null; // The engine caches failed lookups, even after GenTypes.ClearCache.
            GenTypes.ClearCache();
            Assert.Null(GenTypes.GetTypeInAnyAssembly(typeof(WeaponWheelSlot).FullName));
            using (new ModAssemblyScope())
            {
                Assert.Same(typeof(WeaponWheelSlot), GenTypes.GetTypeInAnyAssembly(typeof(WeaponWheelSlot).FullName));
            }
            Assert.True(cache.Contains(key));
            Assert.Null(cache[key]);
        }
        finally
        {
            if (existed) { cache[key] = previous; } else { cache.Remove(key); }
            GenTypes.ClearCache();
        }
    }

    [Fact]
    public void ActualComponentResolvesSlotsIntoItsReserveOwnerAndSecondSaveIsIdentical()
    {
        using var scope = new ScribeScope();
        var first = new Snapshot();
        var weapon = new Weapon();
        Assert.True(first.Wheel.ReserveWeapons.TryAdd(weapon, false));
        first.Wheel.storage.slots[2].Weapon = weapon;
        first.Wheel.storage.slots[3].Unlocked = true;
        first.Wheel.swordDance.swordDanceFailureStreak = 2;
        string saved = Scribe.saver.DebugOutputFor(first);
        Snapshot second = scope.Read(saved);
        Weapon restored = Assert.IsType<Weapon>(Assert.Single(second.Wheel.ReserveWeapons));
        Assert.Same(restored, second.Wheel.storage.slots[2].Weapon);
        Assert.Same(second.Wheel, restored.ParentHolder);
        Assert.Same(second.Wheel, second.Wheel.ReserveWeapons.Owner);
        Assert.Same(second.Pawn, ((IThingHolder)second.Wheel).ParentHolder);
        Assert.Equal(6, second.Wheel.storage.slots.Count);
        Assert.True(second.Wheel.storage.slots[3].Unlocked);
        Assert.False(second.Wheel.storage.slots[4].Unlocked);
        Assert.Equal(2, second.Wheel.swordDance.swordDanceFailureStreak);
        Assert.Equal(saved, Scribe.saver.DebugOutputFor(second));
        Assert.Equal(saved, Scribe.saver.DebugOutputFor(scope.Read(saved)));
    }

    [Fact]
    public void NewlyAddedComponentGetsDistinctDefaultSlotsWithoutInventingAWeapon()
    {
        using var scope = new ScribeScope();
        Snapshot loaded = scope.Read("<snapshot />");
        Assert.Empty(loaded.Wheel.ReserveWeapons);
        Assert.Equal(6, loaded.Wheel.storage.slots.Count);
        for (int i = 0; i < 6; i++)
        {
            Assert.Null(loaded.Wheel.storage.slots[i].Weapon);
            Assert.Equal(i < 3, loaded.Wheel.storage.slots[i].Unlocked);
            for (int j = i + 1; j < 6; j++)
            {
                Assert.NotSame(loaded.Wheel.storage.slots[i], loaded.Wheel.storage.slots[j]);
            }
        }
        string saved = Scribe.saver.DebugOutputFor(loaded);
        Assert.Equal(saved, Scribe.saver.DebugOutputFor(scope.Read(saved)));
    }

    private static ThingDef Definition(string name)
    {
        var definition = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        definition.defName = name;
        definition.stackLimit = 1;
        return definition;
    }

    private sealed class Weapon : ThingWithComps
    {
        public Weapon()
        {
            def = Definition("WheelScribeWeapon");
            thingIDNumber = 17;
            stackCount = 1;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref thingIDNumber, "number", 0);
            Scribe_Values.Look(ref stackCount, "stackCount", 1);
        }
    }

    private sealed class Snapshot : IExposable
    {
        internal Pawn Pawn { get; } = new() { def = Definition("WheelScribePawn"), thingIDNumber = 18 };
        internal CompWeaponWheel Wheel { get; }

        internal Snapshot()
        {
            Wheel = new CompWeaponWheel { parent = Pawn };
            Wheel.Initialize(new CompProperties_WeaponWheel());
        }

        public void ExposeData() => Wheel.PostExposeData();
    }

    private sealed class ScribeScope : IDisposable
    {
        private readonly ScribeLoader loader = Scribe.loader;
        private readonly ScribeSaver saver = Scribe.saver;
        private readonly LoadSaveMode mode = Scribe.mode;
        private readonly Game previousGame = Current.Game;
        private readonly ModAssemblyScope assemblies = new();

        internal ScribeScope()
        {
            var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            typeof(Game).GetField("maps", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(game, new List<Map>());
            Current.Game = game;
            Scribe.loader = new ScribeLoader();
            Scribe.saver = new ScribeSaver();
            Scribe.mode = LoadSaveMode.Inactive;
        }

        internal Snapshot Read(string xml)
        {
            using var errors = new ScribeErrorCapture();
            var document = new XmlDocument { XmlResolver = null };
            document.LoadXml(xml);
            var snapshot = new Snapshot();
            Scribe.loader = new ScribeLoader { curXmlParent = document.DocumentElement, curParent = snapshot };
            Scribe.mode = LoadSaveMode.LoadingVars;
            snapshot.ExposeData();
            Scribe.loader.crossRefs.RegisterForCrossRefResolve(snapshot);
            Scribe.loader.initer.RegisterForPostLoadInit(snapshot);
            Scribe.loader.curXmlParent = null;
            Scribe.loader.crossRefs.ResolveAllCrossReferences();
            Scribe.loader.initer.DoAllPostLoadInits();
            Scribe.mode = LoadSaveMode.Inactive;
            Assert.True(errors.Messages.Count == 0, string.Join("\n", errors.Messages));
            return snapshot;
        }

        public void Dispose()
        {
            assemblies.Dispose();
            Current.Game = previousGame;
            Scribe.loader = loader;
            Scribe.saver = saver;
            Scribe.mode = mode;
        }
    }

    // Both tests use this isolated registration. GenTypes.ClearCache does not clear typeCache (4979
    // 06000597/0600059B), including null results from earlier unregistered lookup attempts.
    internal sealed class ModAssemblyScope : IDisposable
    {
        private readonly ModContentPack mod;
        private readonly IDictionary typeCache;
        private readonly List<DictionaryEntry> previousTypes = [];
        private bool disposed;

        internal ModAssemblyScope()
        {
            typeCache = (IDictionary)typeof(GenTypes).GetField("typeCache", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            foreach (DictionaryEntry entry in typeCache) { previousTypes.Add(entry); }
            mod = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
            mod.assemblies = new ModAssemblyHandler(mod);
            mod.assemblies.loadedAssemblies.Add(typeof(CompWeaponWheel).Assembly);
            mod.assemblies.loadedAssemblies.Add(typeof(WeaponWheelScribeTests).Assembly);
            LoadedModManager.RunningModsListForReading.Add(mod);
            GenTypes.ClearCache();
            typeCache.Clear();
            try
            {
                Assert.Same(typeof(WeaponWheelSlot), GenTypes.GetTypeInAnyAssembly(typeof(WeaponWheelSlot).FullName));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            LoadedModManager.RunningModsListForReading.Remove(mod);
            GenTypes.ClearCache();
            typeCache.Clear();
            foreach (DictionaryEntry entry in previousTypes) { typeCache.Add(entry.Key, entry.Value); }
        }
    }
}
