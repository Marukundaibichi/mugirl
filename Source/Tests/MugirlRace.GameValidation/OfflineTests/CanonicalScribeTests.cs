using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using Mugirl.Features.Milk;
using Mugirl.GameValidation.Core;
using Xunit;

namespace Mugirl.GameValidation.OfflineTests;

[CollectionDefinition("A5 real Scribe", DisableParallelization = true)]
public sealed class CanonicalScribeCollection;

[Collection("A5 real Scribe")]
public sealed class CanonicalScribeTests
{
    [Fact]
    public void RealProductionScribeRoundtripPreservesEveryDiscoveredMilkRoot()
    {
        var comp = new CompMilkProduction();
        Set(comp, "mugirlMilkFullness", 0.3150001f);
        Set(comp, "mugirlMilkPendingTicks", 17);
        Set(comp, "mugirlMilkThreshold", 0.625f);
        Set(comp, "mugirlMilkFullNotified", true);
        Set(comp, "mugirlMilkLastFullNotifyTick", 12341);
        XElement first = Write(comp);
        var reloaded = Read(first);
        XElement second = Write(reloaded);
        SaveDiff diff = Diff();
        Assert.Empty(diff.Compare(Pawn(first), Pawn(second)));
        Assert.Equal(6, diff.ItemCounts["comp-label"]);
    }

    [Theory]
    [InlineData("mugirlMilkFullness", 0.3150002f)]
    [InlineData("mugirlMilkPendingTicks", 18)]
    [InlineData("mugirlMilkThreshold", 0.6250001f)]
    public void RealScribeOutputCannotLoseTinyResourceOrTimerChanges(string field, object value)
    {
        var comp = new CompMilkProduction();
        Set(comp, "mugirlMilkFullness", 0.3150001f);
        Set(comp, "mugirlMilkPendingTicks", 17);
        Set(comp, "mugirlMilkThreshold", 0.625f);
        XElement first = Write(comp);
        CompMilkProduction second = Read(first);
        Set(second, field, value);
        Assert.Single(Diff().Compare(Pawn(first), Pawn(Write(second))));
    }

    private static SaveDiff Diff()
    {
        var roots = new CurrentScribeRoots(typeof(CompMilkProduction).Assembly);
        roots.Discover([typeof(CompMilkProduction)]);
        Assert.Empty(roots.Problems);
        var catalog = new SaveOwnerCatalog(SaveNameMap.Empty, LegacyCorpus.Empty,
            [("ThingDef", "Mugirl", typeof(CompMilkProduction).FullName!)], roots.Labels);
        return new SaveDiff(new SaveDiffScope(new HashSet<string> { "Mugirl" }, new HashSet<string> { "Mugirl" }, catalog.IsModCompLabel, new HashSet<string>()));
    }

    private static XDocument Pawn(XElement state)
        => new(new XElement("savegame", new XElement("game", new XElement("things", new XElement("thing",
            new XAttribute("Class", "Pawn"), new XElement("def", "Mugirl"), new XElement("id", "Mugirl1"), new XElement("kindDef", "MugirlColonist"), state.Elements())))));

    private static void Set(CompMilkProduction comp, string field, object value)
        => typeof(CompMilkProduction).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(comp, value);

    private static CompMilkProduction Read(XElement xml)
    {
        var document = new XmlDocument { XmlResolver = null };
        document.LoadXml(xml.ToString(SaveOptions.DisableFormatting));
        ScribeLoader previousLoader = Scribe.loader;
        LoadSaveMode previousMode = Scribe.mode;
        try
        {
            var comp = new CompMilkProduction();
            Scribe.loader = new ScribeLoader { curXmlParent = document.DocumentElement };
            Scribe.mode = LoadSaveMode.LoadingVars;
            comp.PostExposeData();
            Scribe.mode = LoadSaveMode.PostLoadInit;
            comp.PostExposeData();
            return comp;
        }
        finally
        {
            Scribe.loader = previousLoader;
            Scribe.mode = previousMode;
        }
    }

    private static XElement Write(CompMilkProduction comp)
    {
        string path = Path.Combine(Path.GetTempPath(), "mugirl-a5-scribe-" + Guid.NewGuid().ToString("N") + ".xml");
        ScribeSaver previousSaver = Scribe.saver;
        LoadSaveMode previousMode = Scribe.mode;
        try
        {
            Scribe.saver = new ScribeSaver();
            Scribe.mode = LoadSaveMode.Inactive;
            Scribe.saver.InitSaving(path, "snapshot");
            comp.PostExposeData();
            Scribe.saver.FinalizeSaving();
            return XElement.Load(path, LoadOptions.PreserveWhitespace);
        }
        finally
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Scribe.saver.ForceStop();
            }
            Scribe.saver = previousSaver;
            Scribe.mode = previousMode;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
