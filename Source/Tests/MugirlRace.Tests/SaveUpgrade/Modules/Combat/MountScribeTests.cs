using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Xml;
using Mugirl.Features.Mounting;
using Verse;

namespace Mugirl.Tests.SaveUpgrade.Modules.Combat;

[Collection("Mount Scribe")]
public sealed class MountScribeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingFireFlagUsesTheActualCompPropertiesDefault(bool defaultFire)
    {
        using var scope = new ScribeScope();
        Snapshot snapshot = scope.Read("<snapshot />", defaultFire);
        Assert.Equal(defaultFire, snapshot.Comp.FireAtWill);
        Assert.IsType<ThingOwner<Thing>>(snapshot.Comp.GetDirectlyHeldThings());
        Assert.Same(snapshot.Comp, snapshot.Comp.GetDirectlyHeldThings().Owner);
        Assert.False(snapshot.Comp.Turret.aimTarget.IsValid);
        Assert.Equal(-1, snapshot.Comp.Turret.castStartTick);
        Assert.Equal(0, snapshot.Comp.Turret.aimTicksTotal);
        string saved = Scribe.saver.DebugOutputFor(snapshot);
        Assert.Contains("<mugirlMountFireAtWill>" + defaultFire + "</mugirlMountFireAtWill>", saved, StringComparison.Ordinal);
        Snapshot second = scope.Read(saved, defaultFire);
        Assert.Equal(saved, Scribe.saver.DebugOutputFor(second));
    }

    [Fact]
    public void RealScribePreservesGenericContainerTargetIdentityAllCountersAndOwnerChain()
    {
        using var scope = new ScribeScope();
        var first = new Snapshot(false);
        var payload = new Payload();
        Assert.True(first.Comp.GetDirectlyHeldThings().TryAdd(payload, false));
        Assert.Null(first.Comp.MountedPawn);
        Assert.Same(first.Carrier, first.Comp.MooPawn);
        Assert.Same(first.Comp, payload.ParentHolder);
        first.Comp.Schedule.physiology = int.MaxValue - 7;
        first.Comp.Schedule.safety = -7;
        first.Comp.Schedule.turret = 59;
        first.Comp.Schedule.melee = 999;
        first.Comp.Turret.burstCooldownTicksLeft = -3;
        first.Comp.Turret.aimTarget = new LocalTargetInfo(payload);
        first.Comp.Turret.aimTicksLeft = 17;
        first.Comp.Turret.aimTicksTotal = 93;
        first.Comp.Turret.castStartTick = 4001;
        first.Comp.Turret.lastAttackedTarget = new LocalTargetInfo(new IntVec3(13, 0, 14));
        first.Comp.Turret.lastAttackTargetTick = 3987;
        string saved = Scribe.saver.DebugOutputFor(first);
        Snapshot second = scope.Read(saved, true);
        ThingOwner container = second.Comp.GetDirectlyHeldThings();
        Assert.IsType<ThingOwner<Thing>>(container);
        Assert.Single(container);
        Assert.IsType<Payload>(container[0]);
        Assert.Same(second.Comp, container.Owner);
        Assert.Same(second.Comp, container[0].ParentHolder);
        Assert.Same(container[0], second.Comp.Turret.aimTarget.Thing);
        Assert.False(second.Comp.FireAtWill);
        Assert.Equal(int.MaxValue - 7, second.Comp.Schedule.physiology);
        Assert.Equal(-7, second.Comp.Schedule.safety);
        Assert.Equal(59, second.Comp.Schedule.turret);
        Assert.Equal(999, second.Comp.Schedule.melee);
        Assert.Equal(-3, second.Comp.Turret.burstCooldownTicksLeft);
        Assert.Equal(17, second.Comp.Turret.aimTicksLeft);
        Assert.Equal(93, second.Comp.Turret.aimTicksTotal);
        Assert.Equal(4001, second.Comp.Turret.castStartTick);
        Assert.Equal(new IntVec3(13, 0, 14), second.Comp.Turret.lastAttackedTarget.Cell);
        Assert.Equal(3987, second.Comp.Turret.lastAttackTargetTick);
        Assert.Equal(saved, Scribe.saver.DebugOutputFor(second));
        var outer = new OuterHolder();
        second.Carrier.holdingOwner = outer.Container;
        Assert.Same(outer, ((IThingHolder)second.Comp).ParentHolder);
        Assert.Same(outer, ThingOwnerUtility.GetAnyParent<OuterHolder>(container[0]));
    }

    [Fact]
    public void ActualComponentKeepsRiderFacadeAndOnlyAccumulatesWhenARiderIsPresent()
    {
        using var scope = new ScribeScope();
        var snapshot = new Snapshot(false);
        snapshot.Comp.Props!.tickPhysiology = false;
        snapshot.Comp.Props.autoDismount = false;
        snapshot.Comp.Props.turretTickInterval = 1000;
        snapshot.Comp.CompTickInterval(17);
        Assert.Equal(0, snapshot.Comp.Schedule.physiology);
        Pawn rider = new() { def = Definition("MountRider"), thingIDNumber = 100 };
        Assert.True(snapshot.Comp.GetDirectlyHeldThings().TryAdd(rider, false));
        Assert.Same(rider, snapshot.Comp.MountedPawn);
        Assert.True(snapshot.Comp.HasMountedPawn);
        Assert.Same(snapshot.Comp, rider.ParentHolder);
        snapshot.Comp.CompTickInterval(17);
        Assert.Equal(17, snapshot.Comp.Schedule.physiology);
        Assert.Equal(17, snapshot.Comp.Schedule.safety);
        Assert.Equal(17, snapshot.Comp.Schedule.turret);
        Assert.Equal(17, snapshot.Comp.Schedule.melee);
        snapshot.Comp.CompTick();
        Assert.Equal(17, snapshot.Comp.Schedule.turret);
    }

    [Theory]
    [InlineData("<snapshot />")]
    [InlineData("<snapshot><mugirlMountSchedule><turret>7</turret></mugirlMountSchedule></snapshot>")]
    [InlineData("<snapshot><mugirlMountTurret /></snapshot>")]
    [InlineData("<snapshot><mugirlMountSchedule /><mugirlMountTurret /></snapshot>")]
    public void MissingAndEmptyGroupsResolveWithoutAnyActualScribeError(string xml)
    {
        using var scope = new ScribeScope();
        Snapshot loaded = scope.Read(xml, false);
        Assert.False(loaded.Comp.Turret.aimTarget.IsValid);
        Assert.False(loaded.Comp.Turret.lastAttackedTarget.IsValid);
        string saved = Scribe.saver.DebugOutputFor(loaded);
        Assert.Equal(saved, Scribe.saver.DebugOutputFor(scope.Read(saved, false)));
    }

    [Fact]
    public void NegativeControlDetectsAnUnregisteredReferenceAcrossMissingGroupPhases()
    {
        using var scope = new ScribeScope();
        using var errors = new ScribeErrorCapture();
        scope.ReadObject(new MissingGroupControl(), "<snapshot />");
        Assert.Contains(errors.Messages, text => text.Contains("never added during LoadingVars", StringComparison.Ordinal)
            && text.Contains("missingGroup/target", StringComparison.Ordinal));
    }

    [Fact]
    public void ErrorCaptureRestoresItsPatchAfterScopeThrows()
    {
        Action fail = () =>
        {
            using var capture = new ScribeErrorCapture();
            throw new InvalidOperationException("control");
        };
        Assert.Throws<InvalidOperationException>(fail);
        using var second = new ScribeErrorCapture();
        Log.Error("scribe-capture-second-scope-control");
        Assert.Equal(new[] { "scribe-capture-second-scope-control" }, second.Messages);
    }

    private sealed class MissingGroupControl : IExposable
    {
        private LocalTargetInfo target = LocalTargetInfo.Invalid;
        public void ExposeData()
        {
            // 精确复刻故障：LoadingVars 未进入组，CrossRefs 在 XML 父节点清空后却进入。
            if (!Scribe.EnterNode("missingGroup"))
            {
                return;
            }

            try { Scribe_TargetInfo.Look(ref target, "target"); }
            finally { Scribe.ExitNode(); }
        }
    }

    private static ThingDef Definition(string name)
    {
        var definition = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        definition.defName = name;
        definition.stackLimit = 1;
        return definition;
    }

    private sealed class Payload : Thing
    {
        public Payload()
        {
            def = Definition("MountPayload");
            thingIDNumber = 7;
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
        internal Pawn Carrier { get; }
        internal CompMount Comp { get; }

        internal Snapshot(bool defaultFire)
        {
            Carrier = new Pawn { def = Definition("MountCarrier"), thingIDNumber = 99 };
            Comp = new CompMount { parent = Carrier };
            Comp.Initialize(new CompProperties_Mount { turretFireAtWillDefault = defaultFire });
        }

        public void ExposeData() => Comp.PostExposeData();
    }

    private sealed class OuterHolder : IThingHolder
    {
        internal ThingOwner<Thing> Container { get; }
        public IThingHolder? ParentHolder => null;
        internal OuterHolder() => Container = new ThingOwner<Thing>(this);
        public ThingOwner GetDirectlyHeldThings() => Container;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, Container);
    }

    private sealed class ScribeScope : IDisposable
    {
        private readonly ScribeLoader loader = Scribe.loader;
        private readonly ScribeSaver saver = Scribe.saver;
        private readonly LoadSaveMode mode = Scribe.mode;
        private readonly ModContentPack mod;
        private readonly Game previousGame = Current.Game;

        internal ScribeScope()
        {
            Scribe.loader = new ScribeLoader();
            Scribe.saver = new ScribeSaver();
            Scribe.mode = LoadSaveMode.Inactive;
            // ThingOwner 的正常加入回调会查询 Find.Maps；离线场景提供一个空地图列表。
            var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            typeof(Game).GetField("maps", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(game, new List<Map>());
            Current.Game = game;
            mod = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
            mod.assemblies = new ModAssemblyHandler(mod);
            mod.assemblies.loadedAssemblies.Add(typeof(CompMount).Assembly);
            mod.assemblies.loadedAssemblies.Add(typeof(MountScribeTests).Assembly);
            LoadedModManager.RunningModsListForReading.Add(mod);
            GenTypes.ClearCache();
        }

        internal Snapshot Read(string xml, bool defaultFire)
        {
            using var errors = new ScribeErrorCapture();
            var snapshot = new Snapshot(defaultFire);
            ReadObject(snapshot, xml);
            Assert.True(errors.Messages.Count == 0, string.Join("\n", errors.Messages));
            return snapshot;
        }

        internal void ReadObject(IExposable snapshot, string xml)
        {
            var document = new XmlDocument();
            document.LoadXml(xml);
            Scribe.loader = new ScribeLoader();
            Scribe.loader.curXmlParent = document.DocumentElement;
            Scribe.loader.curParent = snapshot;
            Scribe.mode = LoadSaveMode.LoadingVars;
            snapshot.ExposeData();
            Scribe.loader.crossRefs.RegisterForCrossRefResolve(snapshot);
            Scribe.loader.initer.RegisterForPostLoadInit(snapshot);
            Scribe.loader.curXmlParent = null;
            Scribe.loader.crossRefs.ResolveAllCrossReferences();
            Scribe.loader.initer.DoAllPostLoadInits();
            Scribe.mode = LoadSaveMode.Inactive;
        }

        public void Dispose()
        {
            LoadedModManager.RunningModsListForReading.Remove(mod);
            GenTypes.ClearCache();
            Current.Game = previousGame;
            Scribe.mode = mode;
            Scribe.loader = loader;
            Scribe.saver = saver;
        }
    }
}
