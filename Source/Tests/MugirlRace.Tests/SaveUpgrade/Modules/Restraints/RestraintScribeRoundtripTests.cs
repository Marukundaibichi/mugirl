using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Mugirl.Features.Restraints;
using RimWorld;
using Verse;

namespace Mugirl.Tests.SaveUpgrade.Modules.Restraints;

[CollectionDefinition("Restraint Scribe", DisableParallelization = true)]
public sealed class RestraintScribeCollection;

[Collection("Restraint Scribe")]
public sealed class RestraintScribeRoundtripTests : IDisposable
{
    private readonly ModContentPack mod;
    private readonly List<ThingDef> definitions = [];

    public RestraintScribeRoundtripTests()
    {
        mod = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
        mod.assemblies = new ModAssemblyHandler(mod);
        mod.assemblies.loadedAssemblies.Add(typeof(RestraintApparel).Assembly);
        LoadedModManager.RunningModsListForReading.Add(mod);
        GenTypes.ClearCache();
    }

    public void Dispose()
    {
        foreach (ThingDef definition in definitions)
        {
            typeof(DefDatabase<ThingDef>).GetMethod("Remove", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [definition]);
        }

        DefDatabase<ThingDef>.ClearCachedData();
        LoadedModManager.RunningModsListForReading.Remove(mod);
        GenTypes.ClearCache();
    }

    [Fact]
    public void EveryEquipmentTimerRetainsItsExactValueThroughRealScribe()
    {
        ThingComp[] first =
        [
            new CompBrainwashHelmet { props = new CompProperties_BrainwashHelmet() },
            new CompMagneticShackles { props = new CompProperties_MagneticShackles() },
            new CompShockCollar { props = new CompProperties_ShockCollar() },
        ];
        const string Xml = "<snapshot><mugirlHelmetNonBlockedTicks>314</mugirlHelmetNonBlockedTicks><mugirlHelmetTicksToChange>47319</mugirlHelmetTicksToChange><mugirlHelmetLastManualUseTick>-999999</mugirlHelmetLastManualUseTick><mugirlShacklesActive>True</mugirlShacklesActive><mugirlShacklesCycleInterval>23119</mugirlShacklesCycleInterval><mugirlShacklesBindDurationTicks>11917</mugirlShacklesBindDurationTicks><mugirlShacklesNextStateTick>1234567</mugirlShacklesNextStateTick><mugirlShacklesLastManualUseTick>771</mugirlShacklesLastManualUseTick><mugirlShockCheckTicks>17</mugirlShockCheckTicks><mugirlShockLastManualUseTick>-99999</mugirlShockLastManualUseTick></snapshot>";
        foreach (ThingComp component in first)
        {
            var exposure = new Exposure(component.PostExposeData);
            Read(Xml, exposure);
            string once = Write(exposure);
            Read(once, exposure);
            Assert.Equal(once, Write(exposure));
        }

        Assert.Equal(314, Field<int>(first[0], "mugirlHelmetNonBlockedTicks"));
        Assert.Equal(47319, Field<int>(first[0], "mugirlHelmetTicksToChange"));
        Assert.Equal(23119, Field<int>(first[1], "mugirlShacklesCycleInterval"));
        Assert.Equal(11917, Field<int>(first[1], "mugirlShacklesBindDurationTicks"));
        Assert.Equal(1234567, Field<int>(first[1], "mugirlShacklesNextStateTick"));
        Assert.Equal(17, Field<int>(first[2], "mugirlShockCheckTicks"));
        Assert.Equal(-99999, Field<int>(first[2], "mugirlShockLastManualUseTick"));
    }

    [Fact]
    public void DelayedTriggerAndNestedPerformanceKeepSeparateAges()
    {
        var trigger = new HediffComp_AddThought();
        var performance = new HediffComp_BrainwashPerformance();
        var exposure = new Exposure(() => { trigger.CompExposeData(); performance.CompExposeData(); });
        const string Xml = "<snapshot><mugirlAddThoughtAge>11</mugirlAddThoughtAge><mugirlAddThoughtTriggered>True</mugirlAddThoughtTriggered><mugirlBrainwashPerformance><age>123</age><active>True</active><stunTickTarget>19</stunTickTarget><stunEndTick>321</stunEndTick><nextTextShowTicks><li>-1</li><li>777</li></nextTextShowTicks><nextSoundTicks><li>456</li></nextSoundTicks><nextFleckTicks><li>654</li></nextFleckTicks></mugirlBrainwashPerformance></snapshot>";
        Read(Xml, exposure);
        string once = Write(exposure);
        Read(once, exposure);
        Assert.Equal(once, Write(exposure));
        Assert.Equal(11, Field<int>(trigger, "age"));
        Assert.True(Field<bool>(trigger, "triggered"));
        BrainwashPerformancePlayer player = Field<BrainwashPerformancePlayer>(performance, "mugirlBrainwashPerformance");
        Assert.Equal(123, Field<int>(player, "age"));
        Assert.Equal(new[] { -1, 777 }, Field<List<int>>(player, "nextTextShowTicks"));
        Assert.Equal(new[] { 456 }, Field<List<int>>(player, "nextSoundTicks"));
        Assert.True(player.Active);
    }

    [Theory]
    [InlineData(4, false, false, 4, false)]
    [InlineData(0, false, false, 0, false)]
    [InlineData(0, true, false, 7, true)]
    [InlineData(-3, true, false, 7, true)]
    [InlineData(4, true, true, 0, false)]
    public void ApparelKeepsTheOriginalPostLoadLockInvariants(int count, bool locked, bool cracked, int expectedCount, bool expectedLocked)
    {
        var definition = new SlaveApparelDef
        {
            defName = "Mugirl_TestScribeRestraint",
            thingClass = typeof(AdvancedRestraintApparel),
            category = ThingCategory.Item,
            apparel = new ApparelProperties(),
            needkeynumber = 7,
        };
        definition.ResolveReferences();
        DefDatabase<ThingDef>.Add(definition);
        definitions.Add(definition);
        var apparel = new AdvancedRestraintApparel { def = definition, thingIDNumber = 11 };
        Read("<snapshot><def>Mugirl_TestScribeRestraint</def><id>Mugirl_TestScribeRestraint11</id><mugirlRestraintLockCount>" + count + "</mugirlRestraintLockCount><mugirlRestraintLocked>" + locked + "</mugirlRestraintLocked><mugirlRestraintCracked>" + cracked + "</mugirlRestraintCracked></snapshot>", apparel);
        Assert.Equal(expectedCount, apparel.LockCount);
        Assert.Equal(expectedLocked, apparel.IsLocked);
        Assert.Equal(cracked, apparel.IsCracked());
        string once = Write(apparel);
        Read(once, apparel);
        Assert.Equal(once, Write(apparel));
    }

    private sealed class Exposure(Action action) : IExposable
    {
        public void ExposeData() => action();
    }

    private static void Read(string xml, IExposable value)
    {
        var document = new XmlDocument { XmlResolver = null };
        document.LoadXml(xml);
        ScribeLoader previousLoader = Scribe.loader;
        LoadSaveMode previousMode = Scribe.mode;
        try
        {
            Scribe.loader = new ScribeLoader { curXmlParent = document.DocumentElement, curParent = value };
            Scribe.mode = LoadSaveMode.LoadingVars;
            Scribe.loader.initer.RegisterForPostLoadInit(value);
            Scribe.loader.crossRefs.RegisterForCrossRefResolve(value);
            value.ExposeData();
            Scribe.loader.crossRefs.ResolveAllCrossReferences();
            Scribe.loader.initer.DoAllPostLoadInits();
        }
        finally
        {
            Scribe.loader = previousLoader;
            Scribe.mode = previousMode;
        }
    }

    private static string Write(IExposable value)
    {
        string path = Path.Combine(Path.GetTempPath(), "mugirl-restraint-roundtrip-" + Guid.NewGuid().ToString("N") + ".xml");
        ScribeSaver previousSaver = Scribe.saver;
        LoadSaveMode previousMode = Scribe.mode;
        try
        {
            Scribe.saver = new ScribeSaver();
            Scribe.mode = LoadSaveMode.Inactive;
            Scribe.saver.InitSaving(path, "snapshot");
            value.ExposeData();
            Scribe.saver.FinalizeSaving();
            var document = new XmlDocument { XmlResolver = null };
            document.Load(path);
            return document.DocumentElement!.OuterXml;
        }
        finally
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Scribe.saver.ForceStop();
            }

            Scribe.saver = previousSaver;
            Scribe.mode = previousMode;
            File.Delete(path);
        }
    }

    private static T Field<T>(object value, string name)
    {
        for (Type? type = value.GetType(); type is not null; type = type.BaseType)
        {
            if (type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is { } field)
            {
                return (T)field.GetValue(value)!;
            }
        }

        throw new MissingFieldException(value.GetType().FullName, name);
    }
}
