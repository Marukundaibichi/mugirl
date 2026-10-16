using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using Mugirl.Core;
using Mugirl.SaveUpgrade;
using Verse;

namespace Mugirl.Tests.SaveUpgrade;

/// <summary>生命周期处理器的发现、显式排序与隔离，以及游戏中心组件的版本戳规则。</summary>
[Collection("Reference aliases")]
public sealed class GameLifecycleTests
{
    [Theory]
    [InlineData(1, true, "NotLegacy", false)]
    [InlineData(1, false, "NotLegacy", true)]
    [InlineData(2, false, "Newer", false)]
    [InlineData(0, false, "Failed", true)]
    [InlineData(2, false, "Failed", true)]
    public void UndeserializedHubWritesTheDetectedSourceWhenTheLoadIsNotVerified(int source, bool verified, string state, bool isError)
    {
        ScribeSaver previousSaver = Scribe.saver;
        LoadSaveMode previousMode = Scribe.mode;
        using var errors = new ScribeErrorCapture();
        try
        {
            Scribe.saver = new ScribeSaver();
            Scribe.mode = LoadSaveMode.Inactive;
            var hub = new GameComponent_Mugirl(null!); // FillComponents can create this without an ExposeData read.
            string report = "{\"state\":\"" + state + "\"}";
            hub.AcceptLoad(source, verified, report, record: null,
                verified ? null : new UnverifiedStamp("Test: detected source must be retained.", isError));
            string actual = Scribe.saver.DebugOutputFor(hub);
            var document = new XmlDocument { XmlResolver = null };
            document.LoadXml(actual);
            Assert.Equal(source.ToString(System.Globalization.CultureInfo.InvariantCulture), document.DocumentElement![SaveSchema.StampLabel]!.InnerText);
            Assert.Equal(source, hub.SaveSchema);
            Assert.Equal(source, hub.SourceSchema);
            Assert.Equal(report, hub.LastUpgradeReportJson);
            Assert.Null(hub.LastUpgrade);
            if (!verified && isError) { Assert.Single(errors.Messages); }
            else { Assert.Empty(errors.Messages); }
            Assert.Equal(actual, Scribe.saver.DebugOutputFor(hub));
            Assert.Equal(!verified && isError ? 1 : 0, errors.Messages.Count); // The existing diagnostic is emitted once per game.
        }
        finally
        {
            Scribe.saver = previousSaver;
            Scribe.mode = previousMode;
        }
    }

    [Fact]
    public void NegativeDiagnosticSourceDoesNotReplaceTheLastDetectedStoredStamp()
    {
        var hub = new GameComponent_Mugirl(null!);
        hub.AcceptLoad(2, verified: false, "{}", record: null, problem: null);
        hub.AcceptLoad(SaveSchema.NewGame, verified: false, "{}", record: null, problem: null);
        Assert.Equal(2, hub.SaveSchema);
    }

    [Fact]
    public void HooksRunInExplicitOrderAndFailuresAreIsolated()
    {
        Hooks.Calls.Clear();
        GameLifecycleRegistry registry = GameLifecycleRegistry.FromMethods(typeof(Hooks).GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        List<string> failures = [];

        int succeeded = registry.Raise(GameLifecycleEvent.FinalizeInit, (hook, _) => failures.Add(hook.Id));

        Assert.Equal(new[] { "Early", "B", "C", "Late" }, Hooks.Calls);
        Assert.Equal(4, succeeded);
        Assert.Equal(new[] { typeof(Hooks).FullName + ".Throws" }, failures);
        Assert.Single(registry.HooksFor(GameLifecycleEvent.BeforeLoad));
        Assert.Empty(registry.HooksFor(GameLifecycleEvent.AddedToExistingGame));
    }

    [Fact]
    public void InvalidHookSignaturesAreReportedAndSkipped()
    {
        GameLifecycleRegistry registry = GameLifecycleRegistry.FromMethods(typeof(BadHooks).GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));

        Assert.Equal(3, registry.Problems.Count);
        Assert.Empty(registry.HooksFor(GameLifecycleEvent.LoadedGame));
    }

    [Fact]
    public void ProductionHooksAreValid()
    {
        GameLifecycleRegistry registry = GameLifecycleRegistry.FromAssembly(typeof(GameLifecycle).Assembly);

        Assert.DoesNotContain(registry.Problems, problem => problem.Contains("lifecycle hook", StringComparison.Ordinal));
    }

    [Fact]
    public void HubWritesTheCurrentStampOnlyWhenTheLoadWasVerified()
    {
        var hub = new GameComponent_Mugirl(null!);
        Assert.Equal(0, hub.SaveSchema);
        Assert.Equal(0, hub.SourceSchema);
        Assert.Null(hub.LastUpgradeReportJson);

        hub.AcceptLoad(0, verified: false, "{}", record: null, new UnverifiedStamp("the save upgrade failed at Test.Step.", IsError: true));
        Assert.Equal(0, hub.SaveSchema);

        var record = new SaveUpgradeRecord();
        hub.AcceptLoad(0, verified: true, "{\"state\":\"Upgraded\"}", record, problem: null);
        Assert.Equal(SaveSchema.Current, hub.SaveSchema);
        Assert.Equal(0, hub.SourceSchema);
        Assert.Equal("{\"state\":\"Upgraded\"}", hub.LastUpgradeReportJson);
        Assert.Same(record, hub.LastUpgrade);
    }

    private static class Hooks
    {
        internal static List<string> Calls { get; } = [];

        [GameLifecycleHook(GameLifecycleEvent.FinalizeInit, Order = 10)]
        private static void Late() => Calls.Add("Late");

        [GameLifecycleHook(GameLifecycleEvent.FinalizeInit)]
        private static void C() => Calls.Add("C");

        [GameLifecycleHook(GameLifecycleEvent.FinalizeInit)]
        [GameLifecycleHook(GameLifecycleEvent.BeforeLoad)]
        private static void B() => Calls.Add("B");

        [GameLifecycleHook(GameLifecycleEvent.FinalizeInit, Order = -10)]
        private static void Early() => Calls.Add("Early");

        [GameLifecycleHook(GameLifecycleEvent.FinalizeInit, Order = 5)]
        private static void Throws() => throw new InvalidOperationException("hook failure");
    }

    private sealed class BadHooks
    {
        [GameLifecycleHook(GameLifecycleEvent.LoadedGame)]
        private static void WithParameter(int value) => GC.KeepAlive(value);

        [GameLifecycleHook(GameLifecycleEvent.LoadedGame)]
        private static int WithResult() => 1;

        [GameLifecycleHook(GameLifecycleEvent.LoadedGame)]
        private void Instance() => GC.KeepAlive(this);
    }
}
