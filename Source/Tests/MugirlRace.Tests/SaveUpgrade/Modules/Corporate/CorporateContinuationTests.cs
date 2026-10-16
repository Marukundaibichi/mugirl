using System;
using System.Xml;
using System.Linq;
using System.Runtime.CompilerServices;
using Mugirl.Features.Corporate;
using Mugirl.SaveUpgrade;
using Verse;
using Verse.AI.Group;

namespace Mugirl.Tests.SaveUpgrade.Modules.Corporate;

[Collection("Corporate Scribe")]
public sealed class CorporateContinuationTests
{
    [Fact]
    public void RealScribeRestoresInterfaceClassAfterSavingBeforeFirstTick()
    {
        using var scope = new ScribeScope();
        var job = new LordJob_CorporateSupport(new IntVec3(3, 0, 5)) { continuation = new CorporateSupportGraphCompletion() };
        string first = Scribe.saver.DebugOutputFor(job);
        Assert.Contains("Class=\"Mugirl.SaveUpgrade.CorporateSupportGraphCompletion\"", first, StringComparison.Ordinal);
        LordJob_CorporateSupport loaded = scope.Read<LordJob_CorporateSupport>(first);
        Assert.True(loaded.continuation is CorporateSupportGraphCompletion, string.Join("\n", Log.Messages.Select(message => message.text)));
        Scribe.mode = LoadSaveMode.PostLoadInit;
        Assert.True(loaded.AddFleeToil);
        Scribe.mode = LoadSaveMode.Inactive;
        Assert.False(loaded.AddFleeToil);
        string second = Scribe.saver.DebugOutputFor(loaded);
        LordJob_CorporateSupport reloaded = scope.Read<LordJob_CorporateSupport>(second);
        Assert.IsType<CorporateSupportGraphCompletion>(reloaded.continuation);
        Scribe.mode = LoadSaveMode.Inactive;
        Assert.Equal(first, Scribe.saver.DebugOutputFor(reloaded));
    }

    [Fact]
    public void CompletedSupportSerializesNullAndDoesNotRecreateOldGraph()
    {
        using var scope = new ScribeScope();
        var job = new LordJob_CorporateSupport(new IntVec3(3, 0, 5));
        string saved = Scribe.saver.DebugOutputFor(job);
        LordJob_CorporateSupport loaded = scope.Read<LordJob_CorporateSupport>(saved);
        Assert.Null(loaded.continuation);
        Scribe.mode = LoadSaveMode.PostLoadInit;
        Assert.False(loaded.AddFleeToil);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstTickClearsContinuationBeforeInvocationEvenIfItThrows(bool throws)
    {
        var job = new LordJob_CorporateSupport();
        var continuation = new CountingContinuation(() =>
        {
            Assert.Null(job.continuation);
            if (throws)
            {
                throw new InvalidOperationException("control");
            }
        });
        job.continuation = continuation;
        if (throws)
        {
            Assert.Throws<InvalidOperationException>(job.LordJobTick);
        }
        else
        {
            job.LordJobTick();
        }

        job.LordJobTick();
        Assert.Equal(1, continuation.Count);
    }

    [Fact]
    public void OfferPriceContinuationAlsoSurvivesRealInterfaceScribeRoundTrip()
    {
        using var scope = new ScribeScope();
        var offer = new CorporatePersonOffer { id = 12, price = 4710, paid = true, paidAmount = 4710, continuation = new CorporateOfferPriceCompletion() };
        string saved = Scribe.saver.DebugOutputFor(offer);
        CorporatePersonOffer loaded = scope.Read<CorporatePersonOffer>(saved);
        Assert.True(loaded.continuation is CorporateOfferPriceCompletion, string.Join("\n", Log.Messages.Select(message => message.text)));
        Assert.Equal(4710, loaded.paidAmount);
        Assert.Equal(4710, loaded.price);
    }

    [Fact]
    public void GroupedComponentActuallyLoadsItsStateAndKeepsTheVaultOwner()
    {
        using var scope = new ScribeScope();
        Game game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        const string xml = """
            <saveable Class="Mugirl.Features.Corporate.CorporateNetwork"><sequence>20</sequence><nextRefreshTick>420007</nextRefreshTick>
            <vault><innerList /></vault><history><entries><li><tick>17</tick><key>Foreign.Unknown</key><detail>原始文案</detail><amount>-9</amount></li></entries></history>
            <loan><principal>315.0001</principal><interest>0.0002</interest><dailyRate>0.019999998</dailyRate><collateral /></loan>
            <trade><stock /><orders /></trade><people><offers /><awaitingWorld /><pendingSilver>7</pendingSilver></people>
            <missionBoard><missions /><nextId>7</nextId></missionBoard><services><turnover>400001</turnover><creditedPawnIds><li>18</li><li>8</li></creditedPawnIds></services></saveable>
            """;
        CorporateNetwork loaded = scope.Read<CorporateNetwork>(xml, game);
        Assert.Equal(315.0001f, loaded.LoanPrincipal);
        Assert.Equal(0.0002f, loaded.LoanInterest);
        Assert.Equal(400001, loaded.TradeTurnover);
        Assert.Equal(7, loaded.PeoplePendingSilver);
        Assert.Equal(420007, loaded.NextRefreshTick);
        Assert.Equal("原始文案", loaded.Records[0].detail);
        Assert.Same(loaded, loaded.Vault.Owner);
        Assert.Equal(21, loaded.NewId());
        string saved = Scribe.saver.DebugOutputFor(loaded);
        CorporateNetwork reloaded = scope.Read<CorporateNetwork>(saved, game);
        Assert.Equal(BitConverter.SingleToInt32Bits(loaded.LoanPrincipal), BitConverter.SingleToInt32Bits(reloaded.LoanPrincipal));
        Assert.Equal(BitConverter.SingleToInt32Bits(loaded.LoanInterest), BitConverter.SingleToInt32Bits(reloaded.LoanInterest));
        Assert.Same(reloaded, reloaded.Vault.Owner);
        Assert.Equal(saved, Scribe.saver.DebugOutputFor(reloaded));
    }

    [Fact]
    public void ClientIndexRebuildKeepsReceiptOrderAndPreventsDoubleCredit()
    {
        using var scope = new ScribeScope();
        var account = new CorporateClientAccount();
        Assert.True(account.TryCreditPerson(18));
        Assert.True(account.TryCreditPerson(8));
        Assert.False(account.TryCreditPerson(18));
        string saved = Scribe.saver.DebugOutputFor(account);
        CorporateClientAccount loaded = scope.Read<CorporateClientAccount>(saved);
        // The value aggregate is exposed synchronously by its component in PostLoadInit.
        Scribe.mode = LoadSaveMode.PostLoadInit;
        loaded.ExposeData();
        Scribe.mode = LoadSaveMode.Inactive;
        Assert.False(loaded.TryCreditPerson(18));
        Assert.False(loaded.TryCreditPerson(8));
        Assert.Equal(saved, Scribe.saver.DebugOutputFor(loaded));
        Assert.True(loaded.TryCreditPerson(2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<loan />")]
    [InlineData("<people />")]
    [InlineData("<missionBoard />")]
    public void MissingCorporateGroupsHaveNoUnregisteredCrossReferences(string groups)
    {
        using var scope = new ScribeScope();
        Game game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        CorporateNetwork network = scope.Read<CorporateNetwork>("<saveable Class=\"Mugirl.Features.Corporate.CorporateNetwork\">" + groups + "</saveable>", game);
        string saved = Scribe.saver.DebugOutputFor(network);
        CorporateNetwork second = scope.Read<CorporateNetwork>(saved, game);
        Assert.Equal(saved, Scribe.saver.DebugOutputFor(second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<contact />")]
    public void MissingContactGroupHasNoUnregisteredCrossReferences(string group)
    {
        using var scope = new ScribeScope();
        Game game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        CorporateIntroduction introduction = scope.Read<CorporateIntroduction>("<saveable Class=\"Mugirl.Features.Corporate.CorporateIntroduction\">" + group + "</saveable>", game);
        string saved = Scribe.saver.DebugOutputFor(introduction);
        Assert.Equal(saved, Scribe.saver.DebugOutputFor(scope.Read<CorporateIntroduction>(saved, game)));
    }

    private sealed class CountingContinuation(Action complete) : ICorporateSupportContinuation
    {
        internal int Count { get; private set; }
        public bool IncludeFleeToil => false;
        public void Complete(Lord lord)
        {
            Count++;
            complete();
        }

        public void ExposeData()
        {
        }
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
            // GenTypes searches active mod assemblies, not AppDomain assemblies. Register the actual mod DLL.
            mod = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
            mod.assemblies = new ModAssemblyHandler(mod);
            mod.assemblies.loadedAssemblies.Add(typeof(LordJob_CorporateSupport).Assembly);
            LoadedModManager.RunningModsListForReading.Add(mod);
            GenTypes.ClearCache();
        }

        internal T Read<T>(string xml, params object[] ctorArgs)
        {
            using var errors = new ScribeErrorCapture();
            var document = new XmlDocument();
            document.LoadXml(xml);
            Scribe.loader = new ScribeLoader();
            Scribe.mode = LoadSaveMode.LoadingVars;
            T result = ScribeExtractor.SaveableFromNode<T>(document.DocumentElement!, ctorArgs);
            Assert.NotNull(result);
            Scribe.loader.curXmlParent = null;
            Scribe.loader.crossRefs.ResolveAllCrossReferences();
            Scribe.loader.initer.DoAllPostLoadInits();
            Scribe.mode = LoadSaveMode.Inactive;
            Assert.True(errors.Messages.Count == 0, string.Join("\n", errors.Messages));
            return result;
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
