using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using HarmonyLib;
using Mugirl.Features.Incidents;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

[Collection("A5 real Scribe")]
public sealed class MissingGroupDefaultsTests
{
    private static readonly Assembly Production = typeof(GameComponent_Storyline).Assembly;
    private const string Story = "Mugirl.Features.Incidents.GameComponent_Storyline";
    private const string Network = "Mugirl.Features.Corporate.CorporateNetwork";

    [Fact]
    public void ThreeMissingFieldsAreLimitedToTheirPresentTypedGroups()
    {
        XDocument doc = Document("<opening><nested><pawns> player text </pawns></nested></opening><investment />", "<services />");
        var projection = Apply(doc);
        Assert.Equal(3, projection.Actions.Count);
        Assert.Equal("null", doc.Descendants("investor").Single().Value);
        Assert.Empty(doc.Descendants("creditedPawnIds").Single().Nodes());
        Assert.Equal(" player text ", doc.Descendants("nested").Single().Value);
        Assert.Empty(Apply(doc).Actions);
    }

    [Theory]
    [InlineData("<opening><pawns IsNull='True' /></opening><investment><investor>Thing_Mugirl1</investor></investment>", "<services><creditedPawnIds><li>7</li><li>-1</li></creditedPawnIds></services>")]
    [InlineData("<opening><pawns> </pawns></opening><investment><investor>literal null </investor></investment>", "<services><creditedPawnIds>text</creditedPawnIds></services>")]
    [InlineData("<opening Class='Other.State' /><investment Class='Other.State' />", "<services Class='Other.State' />")]
    [InlineData("", "")]
    public void ExistingMalformedNonemptyOrUnknownGroupsAreNotRewritten(string story, string network)
    {
        XDocument doc = Document(story, network);
        string before = doc.ToString(SaveOptions.DisableFormatting);
        Assert.Empty(Apply(doc).Actions);
        Assert.Equal(before, doc.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void WrongOwnersAndNestedNamesAreNotInitializationEvidence()
    {
        XDocument doc = Saves.Game("<components><li Class='Other.Story'><opening/><investment/></li><li Class='Other.Network'><services/></li>"
            + "<li Class='" + Story + "'><nested><opening/><investment/></nested></li><li Class='" + Network + "'><nested><services/></nested></li></components>");
        Assert.Empty(Apply(doc).Actions);
    }

    [Fact]
    public void DuplicateGroupsAndFieldsFailInsteadOfSelectingOne()
    {
        Assert.Throws<InvalidOperationException>(() => Apply(Document("<opening/><opening/>", "")));
        Assert.Throws<InvalidOperationException>(() => Apply(Document("<investment><investor/><investor/></investment>", "")));
        Assert.Throws<InvalidOperationException>(() => Apply(Document("", "<services><creditedPawnIds/><creditedPawnIds/></services>")));
    }

    [Fact]
    public void ModifiedGroupReaderCannotAuthorizeTheMissingReference()
    {
        var harmony = new Harmony("A5.MissingGroup.ReaderNegative");
        MethodInfo reader = Production.GetType("Mugirl.Features.Incidents.FusionInvestmentState")!.GetMethod("ExposeData")!;
        harmony.Patch(reader, prefix: new HarmonyMethod(typeof(MissingGroupDefaultsTests), nameof(Noop)));
        try { Assert.Throws<InvalidOperationException>(() => Apply(Document("<investment/>", ""))); }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    [Fact]
    public void ActualScribeMissingStorylineFieldsMatchTheProjectionWithoutAnyTicks()
    {
        XElement saved = Roundtrip(new GameComponent_Storyline(), "<snapshot><opening/><courier/><investment/></snapshot>");
        XDocument expected = Document("<opening/><courier/><investment/>", "");
        Assert.Equal(2, Apply(expected).Actions.Count);
        Assert.True(XNode.DeepEquals(expected.Descendants("opening").Single(), saved.Element("opening")));
        Assert.True(XNode.DeepEquals(expected.Descendants("investment").Single(), saved.Element("investment")));
    }

    [Fact]
    public void ActualValueCollectionPostLoadProducesOnlyTheEmptyCreditedList()
    {
        var account = (IExposable)Activator.CreateInstance(Production.GetType("Mugirl.Features.Corporate.CorporateClientAccount")!, nonPublic: true)!;
        XElement saved = Roundtrip(account, "<snapshot/>");
        XDocument expected = Document("", "<services/>");
        Assert.Single(Apply(expected).Actions);
        Assert.True(XNode.DeepEquals(expected.Descendants("creditedPawnIds").Single(), saved.Element("creditedPawnIds")));
    }

    private static void Noop() { }

    private static XDocument Document(string story, string network)
        => Saves.Game("<components><li Class='" + Story + "'>" + story + "</li><li Class='" + Network + "'>" + network + "</li></components>");

    private static CurrentSerializationDefaults Apply(XDocument document)
    {
        var projection = new CurrentSerializationDefaults(Production, new Dictionary<string, SerializationPawnDefaults>());
        projection.Apply(document);
        return projection;
    }

    private static XElement Roundtrip(IExposable value, string source)
    {
        ScribeLoader loader = Scribe.loader;
        ScribeSaver saver = Scribe.saver;
        LoadSaveMode mode = Scribe.mode;
        string path = Path.Combine(Path.GetTempPath(), "mugirl-group-proof-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            var document = new XmlDocument { XmlResolver = null };
            document.LoadXml(source);
            Scribe.loader = new ScribeLoader { curXmlParent = document.DocumentElement, curParent = value };
            Scribe.mode = LoadSaveMode.LoadingVars;
            value.ExposeData();
            Scribe.mode = LoadSaveMode.ResolvingCrossRefs;
            value.ExposeData();
            Scribe.mode = LoadSaveMode.PostLoadInit;
            value.ExposeData();
            Scribe.saver = new ScribeSaver();
            Scribe.mode = LoadSaveMode.Inactive;
            Scribe.saver.InitSaving(path, "snapshot");
            value.ExposeData();
            Scribe.saver.FinalizeSaving();
            return XElement.Load(path);
        }
        finally
        {
            if (Scribe.mode == LoadSaveMode.Saving) { Scribe.saver.ForceStop(); }
            Scribe.loader = loader;
            Scribe.saver = saver;
            Scribe.mode = mode;
            if (File.Exists(path)) { File.Delete(path); }
        }
    }
}
