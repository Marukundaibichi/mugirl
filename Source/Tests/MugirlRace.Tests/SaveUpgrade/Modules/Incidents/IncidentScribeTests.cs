using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Mugirl.Core;
using Mugirl.Features.Incidents;
using Mugirl.SaveUpgrade;
using Verse;

namespace Mugirl.Tests.SaveUpgrade.Modules.Incidents;

[Collection("Incidents Scribe")]
public sealed class IncidentScribeTests
{
    [Fact]
    public void MaterializedEmptyLootMatchesTheOriginalPostLoadDefaultsAndRandomState()
    {
        using var scope = new ScribeScope();
        Map map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        const string Prefix = "<saveable Class=\"Mugirl.Features.Incidents.MapComponent_Punisher\"><activePunishers />";
        MapComponent_Punisher missing;
        int expectedRandom;
        using (Rand.Block(3871))
        {
            missing = scope.Read<MapComponent_Punisher>(Prefix + "</saveable>", [map]);
            Assert.Null(missing.GetDirectlyHeldThings()); // LoadingVars overwrites the constructor's container with the missing Deep value.
            scope.PostLoad(missing);
            expectedRandom = Rand.Int;
        }
        string canonical = Scribe.saver.DebugOutputFor(missing);
        XmlDocument saved = new(); saved.LoadXml(canonical);
        Assert.Equal("<heldLoot><innerList /></heldLoot>", saved.DocumentElement!["heldLoot"]!.OuterXml);
        Assert.Equal("<lootBursts />", saved.DocumentElement!["lootBursts"]!.OuterXml);

        MapComponent_Punisher materialized;
        ThingOwner holder;
        using (Rand.Block(3871))
        {
            materialized = scope.Read<MapComponent_Punisher>(Prefix + "<heldLoot><innerList /></heldLoot><lootBursts /></saveable>", [map]);
            holder = materialized.GetDirectlyHeldThings();
            Assert.IsType<ThingOwner<Thing>>(holder);
            Assert.Same(materialized, holder.Owner);
            scope.PostLoad(materialized);
            Assert.Equal(expectedRandom, Rand.Int);
        }
        Assert.Same(holder, materialized.GetDirectlyHeldThings());
        Assert.Same(map, materialized.ParentHolder);
        Assert.Equal(canonical, Scribe.saver.DebugOutputFor(materialized));
        MapComponent_Punisher reloaded = scope.Read<MapComponent_Punisher>(canonical, [map]);
        scope.PostLoad(reloaded);
        Assert.Equal(canonical, Scribe.saver.DebugOutputFor(reloaded));
        Assert.Same(reloaded, reloaded.GetDirectlyHeldThings().Owner);
    }

    [Fact]
    public void StorylineRealScribeKeepsBothFlagsReferencesAndCountdownOnSecondSave()
    {
        using var scope = new ScribeScope();
        Pawn pawn = MakePawn(123);
        const string xml = """
            <saveable Class="Mugirl.Features.Incidents.GameComponent_Storyline"><opening><started>True</started><checkRemaining>-1</checkRemaining><pawns><li>Thing_Mugirl123</li><li>null</li></pawns></opening>
            <courier><triggered>False</triggered><questStarted>True</questStarted><delayRemaining>299640</delayRemaining></courier>
            <investment><accepted>False</accepted><pending>True</pending><amount>1000</amount><resultRemaining>321</resultRemaining><nextOfferTick>125</nextOfferTick><investorActive>True</investorActive><investor>Thing_Mugirl123</investor></investment></saveable>
            """;
        GameComponent_Storyline first = scope.Read<GameComponent_Storyline>(xml, [], pawn);
        Assert.Same(pawn, first.OpeningCrash.pawns[0]);
        Assert.Null(first.OpeningCrash.pawns[1]);
        Assert.Same(pawn, first.Investment.investor);
        Assert.False(first.CourierTriggered);
        Assert.True(first.CourierQuestStarted);
        Assert.True(first.InvestmentPending);
        Assert.False(first.InvestmentAccepted);
        Assert.Equal(299640, first.Courier.delayRemaining);
        Assert.Equal(321, first.Investment.resultRemaining);
        string saved = Scribe.saver.DebugOutputFor(first);
        GameComponent_Storyline second = scope.Read<GameComponent_Storyline>(saved, [], pawn);
        Assert.Equal(saved, Scribe.saver.DebugOutputFor(second));
        Assert.Equal(-1, second.Clock.lastTick);
        Assert.True(second.Clock.TryBegin(900000, second.Investment.investorActive, out int elapsed, out bool stale));
        Assert.Equal(1, elapsed);
        Assert.True(stale);
    }

    [Fact]
    public void MissingStoryGroupsKeepThePriorFieldDefaults()
    {
        using var scope = new ScribeScope();
        var component = scope.Read<GameComponent_Storyline>("<saveable Class=\"Mugirl.Features.Incidents.GameComponent_Storyline\"><opening/><courier/><investment/></saveable>", []);
        Assert.Equal(360, component.OpeningCrash.checkRemaining);
        Assert.Equal(60000, component.Courier.delayRemaining);
        Assert.Equal(0, component.Investment.resultRemaining);
        Assert.False(component.CourierTriggered);
        Assert.False(component.CourierQuestStarted);
    }

    [Fact]
    public void MigrationCompletionSurvivesPreInitSaveAndConsumesExactlyTheOriginalRandomDraws()
    {
        using var scope = new ScribeScope();
        Map map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        Pawn pawn = MakePawn(123);
        const string xml = """
            <saveable Class="Mugirl.Features.Incidents.MapComponent_Migration"><migration><members><li><pawn>Thing_Mugirl123</pawn><grazeAtTick>371</grazeAtTick></li><li><pawn>null</pawn></li><li><pawn>null</pawn></li></members><departureRemaining>7</departureRemaining><killWindowRemaining>9</killWindowRemaining></migration>
            <initialization Class="Mugirl.SaveUpgrade.MigrationInitialization"><firstUnscheduledIndex>1</firstUnscheduledIndex></initialization></saveable>
            """;
        MapComponent_Migration first = scope.Read<MapComponent_Migration>(xml, [map], pawn);
        Assert.Same(pawn, first.State.members[0].pawn);
        string saved = Scribe.saver.DebugOutputFor(first);
        MapComponent_Migration second = scope.Read<MapComponent_Migration>(saved, [map], pawn);
        Assert.IsType<MigrationInitialization>(second.initialization);
        Assert.Equal(saved, Scribe.saver.DebugOutputFor(second));
        int expectedA, expectedB, expectedNext;
        using (Rand.Block(731))
        {
            expectedA = TickUtility.CurrentGameTickOrFallback(0) + Rand.RangeInclusive(30, 300);
            expectedB = TickUtility.CurrentGameTickOrFallback(0) + Rand.RangeInclusive(30, 300);
            expectedNext = Rand.Int;
        }

        using (Rand.Block(731))
        {
            scope.PostLoad(second);
            Assert.Equal(expectedNext, Rand.Int);
        }

        Assert.Null(second.initialization);
        Assert.Equal(3, second.State.members.Count);
        Assert.Equal(371, second.State.members[0].grazeAtTick);
        Assert.Equal(expectedA, second.State.members[1].grazeAtTick);
        Assert.Equal(expectedB, second.State.members[2].grazeAtTick);
        Assert.Equal(7, second.State.departureRemaining);
        Assert.Equal(9, second.State.killWindowRemaining);
        string complete = Scribe.saver.DebugOutputFor(second);
        MapComponent_Migration canonical = scope.Read<MapComponent_Migration>(complete, [map], pawn);
        Assert.Null(canonical.initialization);
        Assert.Equal(complete, Scribe.saver.DebugOutputFor(canonical));
    }

    [Fact]
    public void PunisherRealScribeKeepsLeavingStateAndOriginalAssetOwner()
    {
        using var scope = new ScribeScope();
        Map map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        Pawn pawn = MakePawn(123);
        const string xml = """
            <saveable Class="Mugirl.Features.Incidents.MapComponent_Punisher"><activePunishers><li><pawn>Thing_Mugirl123</pawn><spawnTick>999999</spawnTick><leaving>True</leaving></li><li><pawn>null</pawn></li></activePunishers><heldLoot><innerList/></heldLoot><lootBursts/>
            <initialization Class="Mugirl.SaveUpgrade.PunisherInitialization"><firstUnscheduledIndex>1</firstUnscheduledIndex></initialization></saveable>
            """;
        MapComponent_Punisher first = scope.Read<MapComponent_Punisher>(xml, [map], pawn);
        Assert.Same(pawn, first.ActivePunishers[0].pawn);
        Assert.Same(first, first.GetDirectlyHeldThings().Owner);
        Assert.Same(map, first.ParentHolder);
        string saved = Scribe.saver.DebugOutputFor(first);
        MapComponent_Punisher second = scope.Read<MapComponent_Punisher>(saved, [map], pawn);
        scope.PostLoad(second);
        Assert.True(second.ActivePunishers[0].leaving);
        Assert.Equal(999999, second.ActivePunishers[0].spawnTick);
        Assert.Equal(TickUtility.CurrentGameTickOrFallback(0), second.ActivePunishers[1].spawnTick);
        Assert.Null(second.initialization);
        Assert.Same(second, second.GetDirectlyHeldThings().Owner);
    }

    [Fact]
    public void PartialScheduleCompletionPersistsItsCursorBeforeRetrying()
    {
        using var scope = new ScribeScope();
        var state = new MigrationState { members = [new MigrationMember(), new MigrationMember(), new MigrationMember()] };
        var pending = new MigrationInitialization { firstUnscheduledIndex = 0 };
        int calls = 0;
        Assert.Throws<InvalidOperationException>(() => pending.FillMissingSchedules(state, 1000, () => ++calls == 2 ? throw new InvalidOperationException("control") : 31));
        Assert.Equal(1, pending.firstUnscheduledIndex);
        Assert.Equal(1031, state.members[0].grazeAtTick);
        string saved = Scribe.saver.DebugOutputFor(pending);
        MigrationInitialization reloaded = scope.Read<MigrationInitialization>(saved, []);
        reloaded.FillMissingSchedules(state, 1001, static () => 40);
        Assert.Equal(1031, state.members[0].grazeAtTick);
        Assert.Equal(1041, state.members[1].grazeAtTick);
        Assert.Equal(1041, state.members[2].grazeAtTick);
        Assert.Equal(-1, reloaded.firstUnscheduledIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitializationIsClearedOnlyAfterSuccessfulCompletion(bool throws)
    {
        using var scope = new ScribeScope();
        var component = new MapComponent_Migration((Map)RuntimeHelpers.GetUninitializedObject(typeof(Map)));
        var initialization = new ControlInitialization(throws);
        component.initialization = initialization;
        if (throws)
        {
            Assert.Throws<InvalidOperationException>(() => scope.PostLoad(component));
            Assert.Same(initialization, component.initialization);
        }
        else
        {
            scope.PostLoad(component);
            scope.PostLoad(component);
            Assert.Null(component.initialization);
        }

        Assert.Equal(1, initialization.Count);
    }

    private sealed class ControlInitialization(bool throws) : IMigrationInitialization
    {
        internal int Count { get; private set; }
        public void Complete(MigrationState state)
        {
            Count++;
            Assert.NotNull(state.members);
            Assert.NotNull(state.playerKillIds);
            if (throws)
            {
                throw new InvalidOperationException("control");
            }
        }

        public void ExposeData()
        {
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("<opening />")]
    [InlineData("<courier />")]
    [InlineData("<investment />")]
    public void MissingStorylineGroupsHaveNoActualScribeErrors(string group)
    {
        using var scope = new ScribeScope();
        GameComponent_Storyline story = scope.Read<GameComponent_Storyline>("<saveable Class=\"Mugirl.Features.Incidents.GameComponent_Storyline\">" + group + "</saveable>", []);
        scope.PostLoad(story);
        string saved = Scribe.saver.DebugOutputFor(story);
        GameComponent_Storyline second = scope.Read<GameComponent_Storyline>(saved, []);
        scope.PostLoad(second);
        Assert.Equal(saved, Scribe.saver.DebugOutputFor(second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<migration />")]
    public void MissingMigrationGroupHasNoActualScribeErrors(string group)
    {
        using var scope = new ScribeScope();
        Map map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        MapComponent_Migration migration = scope.Read<MapComponent_Migration>("<saveable Class=\"Mugirl.Features.Incidents.MapComponent_Migration\">" + group + "</saveable>", [map]);
        scope.PostLoad(migration);
        string saved = Scribe.saver.DebugOutputFor(migration);
        MapComponent_Migration second = scope.Read<MapComponent_Migration>(saved, [map]);
        scope.PostLoad(second);
        Assert.Equal(saved, Scribe.saver.DebugOutputFor(second));
    }

    private static Pawn MakePawn(int id)
    {
        var definition = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        definition.defName = "Mugirl";
        return new Pawn { def = definition, thingIDNumber = id };
    }

    private sealed class ScribeScope : IDisposable
    {
        private readonly ScribeLoader loader = Scribe.loader;
        private readonly ScribeSaver saver = Scribe.saver;
        private readonly LoadSaveMode mode = Scribe.mode;
        private readonly ModContentPack mod;

        internal ScribeScope()
        {
            Scribe.loader = new ScribeLoader();
            Scribe.saver = new ScribeSaver();
            Scribe.mode = LoadSaveMode.Inactive;
            mod = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
            mod.assemblies = new ModAssemblyHandler(mod);
            mod.assemblies.loadedAssemblies.Add(typeof(GameComponent_Storyline).Assembly);
            LoadedModManager.RunningModsListForReading.Add(mod);
            GenTypes.ClearCache();
        }

        internal T Read<T>(string xml, object[] args, params ILoadReferenceable[] references)
        {
            using var errors = new ScribeErrorCapture();
            var document = new XmlDocument();
            document.LoadXml(xml);
            Scribe.loader = new ScribeLoader();
            Scribe.mode = LoadSaveMode.LoadingVars;
            T result = ScribeExtractor.SaveableFromNode<T>(document.DocumentElement!, args);
            Assert.NotNull(result);
            // 使用真实引用目录和解析器，只补齐离线场景中未加载的 Pawn 对象。
            var directory = (LoadedObjectDirectory)typeof(CrossRefHandler).GetField("loadedObjectDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Scribe.loader.crossRefs)!;
            foreach (ILoadReferenceable reference in references)
            {
                directory.RegisterLoaded(reference);
            }

            Scribe.loader.crossRefs.ResolveAllCrossReferences();
            Scribe.mode = LoadSaveMode.Inactive;
            Assert.True(errors.Messages.Count == 0, string.Join("\n", errors.Messages));
            return result;
        }

        internal void PostLoad(IExposable value)
        {
            using var errors = new ScribeErrorCapture();
            Scribe.mode = LoadSaveMode.PostLoadInit;
            try { value.ExposeData(); }
            finally { Scribe.mode = LoadSaveMode.Inactive; }
            Assert.True(errors.Messages.Count == 0, string.Join("\n", errors.Messages));
        }

        public void Dispose()
        {
            LoadedModManager.RunningModsListForReading.Remove(mod);
            GenTypes.ClearCache();
            Scribe.mode = mode;
            Scribe.loader = loader;
            Scribe.saver = saver;
        }
    }
}
