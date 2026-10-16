using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using Mugirl.Features.Abilities;
using Mugirl.Features.Milk;
using Mugirl.SaveUpgrade;
using RimWorld;
using Verse;

namespace Mugirl.Tests.SaveUpgrade.Modules.Milk;

[CollectionDefinition("Milk Scribe", DisableParallelization = true)]
public sealed class MilkScribeCollection;

[Collection("Milk Scribe")]
public sealed class MilkScribeRoundtripTests : IDisposable
{
    private readonly ModContentPack mod;

    public MilkScribeRoundtripTests()
    {
        // 原版只从当前 Mod 列表发现多态保存类型；测试登记真实 DLL，不伪造类型解析结果。
        mod = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
        mod.assemblies = new ModAssemblyHandler(mod);
        mod.assemblies.loadedAssemblies.Add(typeof(CompMilkProduction).Assembly);
        LoadedModManager.RunningModsListForReading.Add(mod);
        GenTypes.ClearCache();
    }

    public void Dispose()
    {
        LoadedModManager.RunningModsListForReading.Remove(mod);
        GenTypes.ClearCache();
    }

    [Fact]
    public void DeferredAccrualSurvivesSavingBeforeFirstActiveTick()
    {
        var document = SaveXml.Save("<thing Class=\"Pawn\"><def>Mugirl</def><id>Mugirl1</id><milkFullness>0.3150001</milkFullness><milkFullness_pendingResourceTicks>37</milkFullness_pendingResourceTicks><milkFullness_lastResourceUpdateTick>990</milkFullness_lastResourceUpdateTick></thing>");
        Assert.False(SaveXml.Upgrade(document, UpgradeTables.Default).Report.Failed);
        Assert.Equal(typeof(LegacyMilkAccrualContinuation), GenTypes.GetTypeInAnyAssembly(typeof(LegacyMilkAccrualContinuation).FullName));
        MilkSnapshot first = Read(document.SelectSingleNode("//thing")!.OuterXml, static () => new MilkSnapshot());
        Assert.True(Field<IResourceAccrualContinuation?>(first.Comp, "mugirlMilkAccrualContinuation") is not null, document.OuterXml + "\n" + string.Join("\n", Log.Messages.Select(message => message.text)));
        string saved = Write(first);
        MilkSnapshot second = Read(saved, static () => new MilkSnapshot());
        Assert.Equal(saved, Write(second));
        Bits.Equal(0.3150001f, second.Comp.Fullness);
        Assert.Equal(37, Field<int>(second.Comp, "mugirlMilkPendingTicks"));
        IResourceAccrualContinuation continuation = Field<IResourceAccrualContinuation>(second.Comp, "mugirlMilkAccrualContinuation");
        Assert.True(continuation is not null, saved);
        continuation.Apply(second.Comp, 1005, 60);
        Assert.Equal(15, Field<int>(second.Comp, "mugirlMilkPendingTicks"));
    }

    [Theory]
    [InlineData("0.25", "0.75")]
    [InlineData("0.25", "0.25")]
    [InlineData(null, "0.75")]
    public void TwoMilkComponentsReadAndResaveTheirIndependentValues(string? own, string foreign)
    {
        string fields = (own is null ? "" : "<milkFullness>" + own + "</milkFullness>")
            + "<milkFullness_lastResourceUpdateTick>990</milkFullness_lastResourceUpdateTick><separator />"
            + "<milkFullness>" + foreign + "</milkFullness><CanBeFedBy />";
        XmlDocument document = SaveXml.Save("<thing><def>Mugirl</def><id>Mugirl1</id>" + fields + "</thing>");
        Assert.False(SaveXml.Upgrade(document, UpgradeTables.Default).Report.Failed);
        AssertMilkRoundtrip((XmlElement)document.SelectSingleNode("//thing")!, own ?? "0", foreign);
    }

    [Fact(Explicit = true)]
    public void SuppliedSavePreservesEveryMilkValueAndRoundtripsBothReaders()
    {
        // 玩家数据只从本次显式指定的副本读取，不随测试源码保存，也不写回输入。
        string input = Environment.GetEnvironmentVariable("MUGIRL_MILK_SAVE") ?? throw new InvalidOperationException("Set MUGIRL_MILK_SAVE to the supplied save copy.");
        byte[] bytes = File.ReadAllBytes(input);
        byte[] hash = SHA256.HashData(bytes);
        var document = new XmlDocument { XmlResolver = null };
        using (var stream = new MemoryStream(bytes))
        {
            document.Load(stream);
        }

        XmlElement[] pawns = document.SelectNodes("//*[def='Mugirl' and milkFullness]")!.Cast<XmlElement>().ToArray();
        Assert.NotEmpty(pawns);
        var pairs = pawns.Select(pawn =>
        {
            XmlElement[] milk = pawn.ChildNodes.OfType<XmlElement>().Where(child => child.Name == "milkFullness").ToArray();
            Assert.Equal(2, milk.Length);
            Assert.StartsWith("milkFullness_", milk[0].NextSibling!.Name, StringComparison.Ordinal);
            return (Pawn: pawn, OwnNode: milk[0], Own: milk[0].InnerText, ForeignNode: milk[1], Foreign: milk[1].InnerText);
        }).ToArray();
        var owned = pairs.Select(pair => pair.OwnNode).ToHashSet();
        var untouched = document.SelectNodes("//milkFullness")!.Cast<XmlElement>().Where(node => !owned.Contains(node))
            .Select(node => (Node: node, Parent: node.ParentNode, Xml: node.OuterXml)).ToArray();

        SaveUpgradeSession first = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(first.Report.Failed, first.Report.Error);
        foreach (var pair in pairs)
        {
            Assert.Equal(pair.Own, pair.Pawn["mugirlMilkFullness"]!.InnerText);
            Assert.Same(pair.ForeignNode, pair.Pawn["milkFullness"]);
            Assert.Equal(pair.Foreign, pair.ForeignNode.InnerText);
            AssertMilkRoundtrip(pair.Pawn, pair.Own, pair.Foreign);
        }
        foreach (var foreign in untouched)
        {
            Assert.Same(foreign.Parent, foreign.Node.ParentNode);
            Assert.Equal(foreign.Xml, foreign.Node.OuterXml);
        }
        Assert.Equal(untouched.Length, document.SelectNodes("//milkFullness")!.Count);
        string upgraded = document.OuterXml;
        SaveUpgradeSession second = SaveXml.Upgrade(document, UpgradeTables.Default);
        Assert.False(second.Report.Failed, second.Report.Error);
        Assert.Equal(0, second.Report.TotalActions);
        Assert.Equal(upgraded, document.OuterXml);
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(input)));

        if (Environment.GetEnvironmentVariable("MUGIRL_MILK_EVIDENCE") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "supplied-save-milk.json"), JsonSerializer.Serialize(new
            {
                sourceSha256 = Convert.ToHexString(hash),
                pairedPawns = pairs.Length,
                foreignMilkNodesPreserved = untouched.Length,
                differentValuePairs = pairs.Count(pair => pair.Own != pair.Foreign),
                firstReport = JsonSerializer.Deserialize<JsonElement>(first.Report.ToJson()),
                secondReport = JsonSerializer.Deserialize<JsonElement>(second.Report.ToJson()),
                pairs = pairs.Select(pair => new { id = pair.Pawn["id"]!.InnerText, own = pair.Own, foreign = pair.Foreign, scribeRoundtrip = true }),
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static void AssertMilkRoundtrip(XmlElement pawn, string own, string foreign)
    {
        using var errors = new ScribeErrorCapture();
        MilkPair first = Read(pawn.OuterXml, static () => new MilkPair());
        Bits.Equal(float.Parse(own, CultureInfo.InvariantCulture), first.Own.Fullness);
        Bits.Equal(float.Parse(foreign, CultureInfo.InvariantCulture), first.Foreign.Fullness);
        string saved = Write(first);
        MilkPair second = Read(saved, static () => new MilkPair());
        Assert.Equal(saved, Write(second));
        Bits.Equal(first.Own.Fullness, second.Own.Fullness);
        Bits.Equal(first.Foreign.Fullness, second.Foreign.Fullness);
        Assert.Empty(errors.Messages);
    }

    [Theory]
    [InlineData("Bounce", DunkPhase.Shattering, false, true)]
    [InlineData("Flight", DunkPhase.Flight, false, false)]
    [InlineData("Slam", DunkPhase.Slam, true, false)]
    [InlineData("Slam", DunkPhase.Slam, false, true)]
    public void MotionStateRoundtripKeepsIndependentFlagsAndFloatBits(string oldPhase, object expectedPhase, bool animate, bool tracks)
    {
        var document = SaveXml.Save("<thing Class=\"Mugirl.Thing_MugirlDunkProp\"><def>Mugirl_DunkProp</def><id>Mugirl_DunkProp1</id><state>" + oldPhase + "</state><stateTicks>17</stateTicks><launchDrawPos>(315.0001, 1.00001, 10000.1)</launchDrawPos><followsCarrier>False</followsCarrier><slamAnimating>" + animate + "</slamAnimating><slamTracksCarrier>" + tracks + "</slamTracksCarrier></thing>");
        Assert.False(SaveXml.Upgrade(document, UpgradeTables.Default).Report.Failed);
        MotionSnapshot first = Read(document.SelectSingleNode("//thing")!.OuterXml, static () => new MotionSnapshot());
        string saved = Write(first);
        MotionSnapshot second = Read(saved, static () => new MotionSnapshot());
        Assert.Equal(saved, Write(second));
        Assert.Equal((DunkPhase)expectedPhase, second.Motion!.phase);
        Assert.Equal(17, second.Motion.phaseTicks);
        Assert.False(second.Motion.followsCarrier);
        Assert.Equal(animate, second.Motion.impactAnimating);
        Assert.Equal(tracks, second.Motion.impactTracksCarrier);
        Bits.Equal(315.0001f, second.Motion.launchHeadPosition.x);
        Bits.Equal(1.00001f, second.Motion.launchHeadPosition.y);
        Bits.Equal(10000.1f, second.Motion.launchHeadPosition.z);
    }

    [Theory]
    [InlineData(true, 172)]
    [InlineData(false, -3)]
    [InlineData(true, 0)]
    public void XenotypeComponentRoundtripPreservesPendingSchedule(bool pending, int ticks)
    {
        GameComponent_MilkUpgrade first = Read("<snapshot><xenotypeRepairPending>" + pending + "</xenotypeRepairPending><xenotypeRepairTicks>" + ticks + "</xenotypeRepairTicks></snapshot>", static () => new GameComponent_MilkUpgrade());
        string saved = Write(first);
        GameComponent_MilkUpgrade second = Read(saved, static () => new GameComponent_MilkUpgrade());
        Assert.Equal(saved, Write(second));
        Assert.Equal(pending, Field<bool>(second, "xenotypeRepairPending"));
        Assert.Equal(ticks, Field<int>(second, "xenotypeRepairTicks"));
    }

    private sealed class MotionSnapshot : IExposable
    {
        internal DunkMotionState? Motion;
        public void ExposeData() => Scribe_Deep.Look(ref Motion, "motion");
    }

    private sealed class MilkSnapshot : IExposable
    {
        internal readonly CompMilkProduction Comp = new();
        public void ExposeData() => Comp.PostExposeData();
    }

    private sealed class MilkPair : IExposable
    {
        internal readonly CompMilkProduction Own = new();
        internal readonly CompMilkable Foreign = new();

        public void ExposeData()
        {
            Own.PostExposeData();
            Foreign.PostExposeData();
        }
    }

    private static T Read<T>(string xml, Func<T> factory) where T : IExposable
    {
        var document = new XmlDocument { XmlResolver = null };
        document.LoadXml(xml);
        ScribeLoader previousLoader = Scribe.loader;
        LoadSaveMode previousMode = Scribe.mode;
        try
        {
            T result = factory();
            Scribe.loader = new ScribeLoader { curXmlParent = document.DocumentElement, curParent = result };
            Scribe.mode = LoadSaveMode.LoadingVars;
            result.ExposeData();
            return result;
        }
        finally
        {
            Scribe.loader = previousLoader;
            Scribe.mode = previousMode;
        }
    }

    private static string Write(IExposable value)
    {
        string path = Path.Combine(Path.GetTempPath(), "mugirl-milk-roundtrip-" + Guid.NewGuid().ToString("N") + ".xml");
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
        => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
}
