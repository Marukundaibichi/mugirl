using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Xml;
using HarmonyLib;
using Mugirl.SaveUpgrade;
using Verse;

namespace Mugirl.Tests.SaveUpgrade;

[CollectionDefinition("Reference aliases", DisableParallelization = true)]
public sealed class ReferenceAliasCollection;

[Collection("Reference aliases")]
public sealed class ReferenceAliasTests
{
    private const string OldThing = "OPC_Assault_Rifle123";
    private const string NewThing = "Mugirl_OPC_AssaultRifle123";
    private const string Body = "<thing><def>OPC_Assault_Rifle</def><id>" + OldThing + "</id></thing>"
        + "<thing><def>Mugirl_Retired</def><id>Mugirl_Retired7</id></thing>";

    private static UpgradeTables Tables() => SaveXml.Tables(m =>
    {
        m.RenameDef("ThingDef", "OPC_Assault_Rifle", "Mugirl_OPC_AssaultRifle", "test");
        m.RenameDef("RecipeDef", "OldRecipe", "NewRecipe", "test");
        m.DropDef("ThingDef", "Mugirl_Retired", DefDropPolicy.RemoveThings, "test");
    });

    [Fact]
    public void PreservesAllPlayerTextEvenWhenItExactlyMatchesRenamedOrDeletedIdentities()
    {
        const string Text = "<nick>Thing_" + OldThing + "</nick><label>memo_" + OldThing + "_notes</label>"
            + "<text><![CDATA[Thing_Mugirl_Retired7]]></text>"
            + "<notes><li>Thing_Mugirl_Retired7</li><li>Bill_OldRecipe_42</li><li>Verb_CompEquippable_" + OldThing + "_0</li></notes>"
            + "<dictionary><keys><li>Thing_Mugirl_Retired7</li></keys><values><li>Keep this text</li></values></dictionary>";
        XmlDocument document = SaveXml.Save(Body + Text);
        SaveUpgradeSession session = SaveXml.Upgrade(document, Tables());

        Assert.False(session.Report.Failed, session.Report.Error);
        Assert.Equal("<thing><def>OPC_Assault_Rifle</def><id>" + NewThing + "</id></thing><thing IsNull=\"True\" />" + Text, SaveXml.Game(document));
        Assert.Equal("Thing_" + NewThing, session.LoadIds!.Reference("Thing_" + OldThing));
        Assert.Equal("null", session.LoadIds.Reference("Thing_Mugirl_Retired7"));
        Assert.Equal("Bill_NewRecipe_42", session.LoadIds.Reference("Bill_OldRecipe_42"));
        Assert.Equal("Verb_CompEquippable_" + NewThing + "_0", session.LoadIds.Reference("Verb_CompEquippable_" + OldThing + "_0"));
    }

    [Fact]
    public void ReferenceListsCopyOnlyWhenChangedAndPreserveEveryPosition()
    {
        LoadIdRemapper aliases = SaveXml.Upgrade(SaveXml.Save(Body), Tables()).LoadIds!;
        List<string> original = ["Thing_" + OldThing, "Thing_Mugirl_Retired7", "Thing_Steel8", "null", null!];
        List<string> rewritten = aliases.References(original)!;

        Assert.NotSame(original, rewritten);
        Assert.Equal(new[] { "Thing_" + NewThing, "null", "Thing_Steel8", "null", null }, rewritten);
        Assert.Equal(new[] { "Thing_" + OldThing, "Thing_Mugirl_Retired7", "Thing_Steel8", "null", null }, original);
        Assert.Same(rewritten, aliases.References(rewritten));
        List<string> empty = [];
        Assert.Same(empty, aliases.References(empty));
        Assert.Null(aliases.References(null));
        Assert.Null(aliases.Reference(null));
        Assert.Equal(string.Empty, aliases.Reference(string.Empty));
    }

    [Fact]
    public void AliasesAreImmutableSnapshots()
    {
        var things = new Dictionary<string, string>(StringComparer.Ordinal) { [OldThing] = NewThing };
        var recipes = new Dictionary<string, string>(StringComparer.Ordinal) { ["OldRecipe"] = "NewRecipe" };
        var removed = new HashSet<string>(StringComparer.Ordinal) { "Lord_5" };
        var removedRecipes = new HashSet<string>(StringComparer.Ordinal) { "RetiredRecipe" };
        var aliases = new LoadIdRemapper(things, recipes, removed, removedRecipes);
        things.Clear();
        recipes.Clear();
        removed.Clear();
        removedRecipes.Clear();

        Assert.Equal("Thing_" + NewThing, aliases.Reference("Thing_" + OldThing));
        Assert.Equal("Bill_NewRecipe_42", aliases.Reference("Bill_OldRecipe_42"));
        Assert.Equal("null", aliases.Reference("Lord_5"));
        Assert.Equal("null", aliases.Reference("Bill_RetiredRecipe_42"));
    }

    [Fact]
    public void SessionGateRejectsOtherDocumentsFailuresCompletionAndNonLegacySaves()
    {
        XmlDocument document = SaveXml.Save(Body);
        SaveUpgradeSession session = SaveXml.Upgrade(document, Tables());
        Assert.True(LoadIdRemapper.TryForDocument(session, document.DocumentElement!["game"], out _));
        Assert.False(LoadIdRemapper.TryForDocument(session, SaveXml.Save(Body).DocumentElement!["game"], out _));
        Assert.False(LoadIdRemapper.TryForDocument(null, document.DocumentElement!["game"], out _));
        Assert.False(LoadIdRemapper.TryForDocument(session, null, out _));
        foreach (XmlDocument other in new[] { SaveXml.Save(Body, stamp: SaveSchema.Current), SaveXml.Save(Body, stamp: SaveSchema.Current + 1), SaveXml.Save(Body, mugirl: false) })
        {
            string before = other.OuterXml;
            SaveUpgradeSession skipped = SaveXml.Upgrade(other, Tables());
            Assert.Null(skipped.LoadIds);
            Assert.False(LoadIdRemapper.TryForDocument(skipped, other.DocumentElement!["game"], out _));
            Assert.Equal(before, other.OuterXml);
        }
        session.Report.Fail("Test", "failure after DOM");
        Assert.False(LoadIdRemapper.TryForDocument(session, document.DocumentElement!["game"], out _));
        SaveUpgradeSession completed = SaveXml.Upgrade(document, Tables());
        completed.MarkPhase(UpgradePhase.A);
        completed.MarkPhase(UpgradePhase.B);
        completed.Complete();
        Assert.False(LoadIdRemapper.TryForDocument(completed, document.DocumentElement!["game"], out _));
    }

    [Fact]
    public void SecondDomPassRetainsAliasesUntilActualReferencesAreRead()
    {
        XmlDocument document = SaveXml.Save(Body + "<nick>Thing_" + OldThing + "</nick><ref>Thing_" + OldThing + "</ref>");
        SaveUpgradeSession first = SaveXml.Upgrade(document, Tables());
        string once = document.OuterXml;
        SaveUpgradeSession second = SaveXml.Upgrade(document, Tables());

        Assert.Equal(once, document.OuterXml);
        Assert.Equal(first.LoadIds!.Reference("Thing_" + OldThing), second.LoadIds!.Reference("Thing_" + OldThing));
        Assert.Equal("Thing_" + NewThing, second.LoadIds.Reference("Thing_" + NewThing));
        Assert.Equal("CompEquippable_" + NewThing + "_0", second.LoadIds.VerbIdentity("CompEquippable_" + NewThing + "_0"));
    }

    [Fact]
    public void ActualScribeRegistrationHooksSeparateReferencesFromValuesAndOtherBanks()
    {
        XmlDocument document = SaveXml.Save(Body
            + "<nick>Thing_" + OldThing + "</nick><target>Thing_" + OldThing + "</target>"
            + "<bill>Bill_OldRecipe_42</bill><verb>Verb_CompEquippable_" + OldThing + "_0</verb>"
            + "<refs><li>Thing_" + OldThing + "</li><li>Thing_Mugirl_Retired7</li></refs>"
            + "<notes><li>Thing_" + OldThing + "</li><li>Thing_Mugirl_Retired7</li></notes>");
        SaveUpgradeSession session = SaveXml.Upgrade(document, Tables());
        session.ReadTrace = new ValidationReadTrace();
        using var scope = new ScribeScope(session, document.DocumentElement!["game"]!);
        var owner = new ReferenceObject("owner");
        Scribe.loader.curParent = owner;
        string? nick = null;
        Scribe_Values.Look(ref nick, "nick");
        Thing? target = null;
        Scribe_References.Look(ref target, "target");
        ReferenceObject? bill = null;
        ReferenceObject? verb = null;
        Scribe_References.Look(ref bill, "bill");
        Scribe_References.Look(ref verb, "verb");
        List<ReferenceObject>? refs = null;
        Scribe_Collections.Look(ref refs, "refs", LookMode.Reference);
        List<string>? notes = null;
        Scribe_Collections.Look(ref notes, "notes", LookMode.Value);

        Assert.Equal("Thing_" + OldThing, nick);
        Assert.Equal(new[] { "Thing_" + OldThing, "Thing_Mugirl_Retired7" }, notes);
        Assert.Equal("Thing_" + NewThing, Scribe.loader.crossRefs.loadIDs.Take<Thing>("/target", owner));
        Assert.Equal("Bill_NewRecipe_42", Scribe.loader.crossRefs.loadIDs.Take<ReferenceObject>("/bill", owner));
        Assert.Equal("Verb_CompEquippable_" + NewThing + "_0", Scribe.loader.crossRefs.loadIDs.Take<ReferenceObject>("/verb", owner));
        Assert.Equal(new[] { "Thing_" + NewThing, "null" }, Scribe.loader.crossRefs.loadIDs.TakeList("/refs", owner));

        var unrelatedBank = new LoadIDsWantedBank();
        unrelatedBank.RegisterLoadIDReadFromXml("Thing_" + OldThing, typeof(Thing), "x", owner);
        Assert.Equal("Thing_" + OldThing, unrelatedBank.Take<Thing>("x", owner));
        using (JsonDocument trace = JsonDocument.Parse(session.ReadTrace.ToJson()))
        {
            Assert.Empty(trace.RootElement.GetProperty("errors").EnumerateArray());
            JsonElement[] events = [.. trace.RootElement.GetProperty("events").EnumerateArray()];
            Assert.Equal(5, events.Length);
            Assert.Equal("/savegame[1]/game[1]/target[1]", events[0].GetProperty("path").GetString());
            Assert.Equal("/savegame[1]/game[1]/refs[1]/li[2]", events[4].GetProperty("path").GetString());
            Assert.Equal("null", events[4].GetProperty("newValue").GetString());
        }
        Scribe.loader.curXmlParent = SaveXml.Save(Body).DocumentElement!["game"];
        Scribe.loader.crossRefs.loadIDs.RegisterLoadIDReadFromXml("Thing_" + OldThing, typeof(Thing), "otherDoc", owner);
        Assert.Equal("Thing_" + OldThing, Scribe.loader.crossRefs.loadIDs.Take<Thing>("otherDoc", owner));
    }

    [Fact]
    public void ActualVerbReadKeepsBurstStateAndMatchesItsNewIdentity()
    {
        XmlDocument document = SaveXml.Save(Body
            + "<verb><loadID>CompEquippable_" + OldThing + "_0</loadID><state>Bursting</state>"
            + "<burstShotsLeft>3</burstShotsLeft><ticksToNextBurstShot>17</ticksToNextBurstShot><lastShotTick>91</lastShotTick>"
            + "<currentTarget>null</currentTarget><surpriseAttack>True</surpriseAttack></verb>");
        SaveUpgradeSession session = SaveXml.Upgrade(document, Tables());
        session.ReadTrace = new ValidationReadTrace();
        using var scope = new ScribeScope(session, document.DocumentElement!["game"]!["verb"]!);
        var verb = new Verb_Shoot();
        Scribe.loader.curParent = verb;
        verb.ExposeData();

        Assert.Equal("CompEquippable_" + NewThing + "_0", verb.loadID);
        Assert.Equal("Verb_CompEquippable_" + NewThing + "_0", verb.GetUniqueLoadID());
        Assert.Equal(VerbState.Bursting, verb.state);
        Assert.Equal(3, ReadVerbField<int>(verb, "burstShotsLeft"));
        Assert.Equal(17, ReadVerbField<int>(verb, "ticksToNextBurstShot"));
        Assert.Equal(91, ReadVerbField<int>(verb, "lastShotTick"));
        Assert.True(ReadVerbField<bool>(verb, "surpriseAttack"));
        Assert.Equal(LocalTargetInfo.Invalid, verb.CurrentTarget);
        Assert.Equal("CompEquippable_" + NewThing + "_0", session.LoadIds!.VerbIdentity(verb.loadID));
        using JsonDocument trace = JsonDocument.Parse(session.ReadTrace.ToJson());
        Assert.Empty(trace.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Equal("VerbIdentity", Assert.Single(trace.RootElement.GetProperty("events").EnumerateArray()).GetProperty("kind").GetString());
    }

    [Fact]
    public void ActualDirectoryResolvesRenamedThingBillAndVerbReferences()
    {
        LoadIdRemapper aliases = SaveXml.Upgrade(SaveXml.Save(Body), Tables()).LoadIds!;
        var directory = new LoadedObjectDirectory();
        var thing = new ThingWithComps { def = new ThingDef { defName = "Mugirl_OPC_AssaultRifle" }, thingIDNumber = 123 };
        var bill = new ReferenceObject("Bill_NewRecipe_42");
        var verb = new Verb_Shoot { loadID = "CompEquippable_" + NewThing + "_0" };
        directory.RegisterLoaded(thing);
        directory.RegisterLoaded(bill);
        directory.RegisterLoaded(verb);

        Assert.Same(thing, directory.ObjectWithLoadID<Thing>(aliases.Reference("Thing_" + OldThing)));
        Assert.Same(bill, directory.ObjectWithLoadID<ReferenceObject>(aliases.Reference("Bill_OldRecipe_42")));
        Assert.Same(verb, directory.ObjectWithLoadID<Verb>(aliases.Reference("Verb_CompEquippable_" + OldThing + "_0")));
        Assert.Null(directory.ObjectWithLoadID<Thing>(aliases.Reference("Thing_Mugirl_Retired7")));
    }

    private sealed class ReferenceObject(string? id) : ILoadReferenceable, IExposable
    {
        public ReferenceObject() : this(string.Empty) { }
        public string GetUniqueLoadID() => id ?? string.Empty;
        public void ExposeData() => Scribe_Values.Look(ref id, "id");
    }

    [Fact]
    public void ActualScribeSaveReloadAndSecondSaveAreIdenticalWithAliasesDisabled()
    {
        XmlDocument legacy = SaveXml.Save(Body + "<snapshot><objects><li><id>Thing_" + NewThing
            + "</id></li><li><id>Bill_NewRecipe_42</id></li><li><id>Verb_CompEquippable_" + NewThing
            + "_0</id></li></objects><links><li>Thing_" + OldThing + "</li><li>Bill_OldRecipe_42</li><li>Verb_CompEquippable_"
            + OldThing + "_0</li></links><nick>Thing_" + OldThing + "</nick></snapshot>");
        SaveUpgradeSession upgrading = SaveXml.Upgrade(legacy, Tables());
        ReferenceSnapshot first = ReadSnapshot(upgrading, legacy.DocumentElement!["game"]!["snapshot"]!);
        string saved = WriteSnapshot(first);
        Assert.Contains("<links><li>Thing_" + NewThing + "</li><li>Bill_NewRecipe_42</li><li>Verb_CompEquippable_" + NewThing + "_0</li></links>", saved, StringComparison.Ordinal);
        Assert.Contains("<nick>Thing_" + OldThing + "</nick>", saved, StringComparison.Ordinal);

        XmlDocument current = SaveXml.Save(saved, stamp: SaveSchema.Current);
        SaveUpgradeSession noUpgrade = SaveXml.Upgrade(current, Tables());
        Assert.Null(noUpgrade.LoadIds);
        ReferenceSnapshot second = ReadSnapshot(noUpgrade, current.DocumentElement!["game"]!["snapshot"]!);
        Assert.Equal(saved, WriteSnapshot(second));
        Assert.Equal(3, second.Links!.Count);
        for (int i = 0; i < second.Links.Count; i++)
        {
            Assert.Same(second.Objects![i], second.Links[i]);
        }
    }

    private static ReferenceSnapshot ReadSnapshot(SaveUpgradeSession session, XmlElement element)
    {
        using var scope = new ScribeScope(session, element);
        var snapshot = new ReferenceSnapshot();
        Scribe.loader.curParent = snapshot;
        snapshot.ExposeData();
        Scribe.loader.crossRefs.RegisterForCrossRefResolve(snapshot);
        Scribe.loader.crossRefs.ResolveAllCrossReferences();
        return snapshot;
    }

    private static string WriteSnapshot(ReferenceSnapshot snapshot)
    {
        string path = Path.Combine(Path.GetTempPath(), "mugirl-reference-roundtrip-" + Guid.NewGuid().ToString("N") + ".xml");
        ScribeSaver oldSaver = Scribe.saver;
        LoadSaveMode oldMode = Scribe.mode;
        try
        {
            Scribe.saver = new ScribeSaver();
            Scribe.mode = LoadSaveMode.Inactive;
            Scribe.saver.InitSaving(path, "snapshot");
            snapshot.ExposeData();
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
            Scribe.saver = oldSaver;
            Scribe.mode = oldMode;
            File.Delete(path);
        }
    }

    private sealed class ReferenceSnapshot : IExposable
    {
        internal List<ReferenceObject>? Objects;
        internal List<ReferenceObject>? Links;
        internal string? Nick;
        public void ExposeData()
        {
            Scribe_Collections.Look(ref Objects, "objects", LookMode.Deep);
            Scribe_Collections.Look(ref Links, "links", LookMode.Reference);
            Scribe_Values.Look(ref Nick, "nick");
        }
    }

    [Fact]
    public void ActualReferenceDictionaryRemovesOnlyDeletedKeyValuePairsBeforeReadingValues()
    {
        XmlDocument document = SaveXml.Save(Body + "<memory><keys><li>Thing_Mugirl_Retired7</li><li>Thing_" + OldThing
            + "</li></keys><values><li>17</li><li>23</li></values></memory>");
        SaveUpgradeSession session = SaveXml.Upgrade(document, Tables());
        session.ReadTrace = new ValidationReadTrace();
        using var scope = new ScribeScope(session, document.DocumentElement!["game"]!);
        var reader = new DictionaryReader<int>(LookMode.Value);
        Scribe.loader.curParent = reader;
        reader.ExposeData();

        Assert.False(session.Report.Failed, session.Report.Error);
        Assert.Equal("<keys><li>Thing_" + OldThing + "</li></keys><values><li>23</li></values>", document.DocumentElement!["game"]!["memory"]!.InnerXml);
        Assert.Equal(new[] { 23 }, reader.Values);
        var key = new ReferenceObject("Thing_" + NewThing);
        Scribe.loader.crossRefs.RegisterForCrossRefResolve(reader);
        Scribe.loader.crossRefs.RegisterForCrossRefResolve(key);
        Scribe.loader.crossRefs.ResolveAllCrossReferences();

        Assert.Equal(23, Assert.Single(reader.Items!).Value);
        Assert.Same(key, Assert.Single(reader.Items!).Key);
        Assert.Same(key, Assert.Single(reader.Keys!));
        Assert.Equal(2, SaveXml.Actions(session, "Framework.LoadIds.DictionaryReferences"));
        using JsonDocument trace = JsonDocument.Parse(session.ReadTrace.ToJson());
        Assert.Empty(trace.RootElement.GetProperty("errors").EnumerateArray());
        JsonElement[] events = [.. trace.RootElement.GetProperty("events").EnumerateArray()];
        Assert.Equal(3, events.Length);
        Assert.Equal("DictionaryPairs", events[2].GetProperty("kind").GetString());
        Assert.Equal("/savegame[1]/game[1]/memory[1]", events[2].GetProperty("path").GetString());
        Assert.Equal(0, Assert.Single(events[2].GetProperty("indices").EnumerateArray()).GetInt32());
        Assert.Equal("null", Assert.Single(events[2].GetProperty("oldKeys").EnumerateArray()).GetString());
        Assert.Equal("17", Assert.Single(events[2].GetProperty("oldValues").EnumerateArray()).GetString());
    }

    [Fact]
    public void TargetInfoTraceUsesTheActualScalarAndNeverAnotherMatchingString()
    {
        XmlDocument document = SaveXml.Save(Body + "<text>Thing_" + OldThing + "</text><target>Thing_" + OldThing + "</target>");
        SaveUpgradeSession session = SaveXml.Upgrade(document, Tables());
        session.ReadTrace = new ValidationReadTrace();
        using var scope = new ScribeScope(session, document.DocumentElement!["game"]!);
        var owner = new ReferenceObject("owner");
        Scribe.loader.curParent = owner;
        LocalTargetInfo target = LocalTargetInfo.Invalid;
        Scribe_TargetInfo.Look(ref target, "target");
        Assert.Equal("Thing_" + NewThing, Scribe.loader.crossRefs.loadIDs.Take<Thing>("/target/thing", owner));
        using JsonDocument trace = JsonDocument.Parse(session.ReadTrace.ToJson());
        Assert.Empty(trace.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Equal("/savegame[1]/game[1]/target[1]", Assert.Single(trace.RootElement.GetProperty("events").EnumerateArray()).GetProperty("path").GetString());

        session.ReadTrace.Scalar("Reference", document.DocumentElement!["game"]!["missing"], "Thing_" + OldThing, "Thing_" + NewThing);
        using JsonDocument unknown = JsonDocument.Parse(session.ReadTrace.ToJson());
        Assert.Single(unknown.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Single(unknown.RootElement.GetProperty("events").EnumerateArray());
        Assert.Equal("Thing_" + OldThing, document.DocumentElement!["game"]!["text"]!.InnerText);
    }

    [Fact]
    public void ActualReferenceDictionaryKeepsReferenceValuesAndOriginalPairing()
    {
        XmlDocument document = SaveXml.Save(Body + "<memory><keys><li>Thing_Mugirl_Retired7</li><li>Thing_" + OldThing
            + "</li></keys><values><li>Unused_5</li><li>Bill_OldRecipe_42</li></values></memory>");
        SaveUpgradeSession session = SaveXml.Upgrade(document, Tables());
        using var scope = new ScribeScope(session, document.DocumentElement!["game"]!);
        var reader = new DictionaryReader<ReferenceObject>(LookMode.Reference);
        Scribe.loader.curParent = reader;
        reader.ExposeData();
        Assert.False(session.Report.Failed, session.Report.Error);
        var key = new ReferenceObject("Thing_" + NewThing);
        var value = new ReferenceObject("Bill_NewRecipe_42");
        Scribe.loader.crossRefs.RegisterForCrossRefResolve(reader);
        Scribe.loader.crossRefs.RegisterForCrossRefResolve(key);
        Scribe.loader.crossRefs.RegisterForCrossRefResolve(value);
        Scribe.loader.crossRefs.ResolveAllCrossReferences();
        Assert.Same(key, Assert.Single(reader.Items!).Key);
        Assert.Same(value, Assert.Single(reader.Items!).Value);
        Assert.Equal("<keys><li>Thing_" + OldThing + "</li></keys><values><li>Bill_OldRecipe_42</li></values>", document.DocumentElement!["game"]!["memory"]!.InnerXml);
    }

    [Fact]
    public void NestedDeepValuesWithOrdinaryKeysListsDoNotInheritTheOuterDictionaryContract()
    {
        const string Lists = "<keys><li>Thing_Mugirl_Retired7</li><li>Thing_Steel8</li></keys><values><li>Keep both</li><li>Thing_Mugirl_Retired7</li></values>";
        XmlDocument document = SaveXml.Save(Body + "<memory><keys><li>1</li></keys><values><li>" + Lists + "</li></values></memory>");
        SaveUpgradeSession session = SaveXml.Upgrade(document, Tables());
        using var scope = new ScribeScope(session, document.DocumentElement!["game"]!);
        Dictionary<int, NestedReferences>? outer = null;
        List<int>? keys = null;
        List<NestedReferences>? values = null;
        Scribe.loader.curParent = new ReferenceObject("outer");
        Scribe_Collections.Look(ref outer, "memory", LookMode.Value, LookMode.Deep, ref keys, ref values);

        Assert.False(session.Report.Failed, session.Report.Error);
        Assert.Equal(Lists, document.DocumentElement!["game"]!["memory"]!["values"]!["li"]!.InnerXml);
        NestedReferences nested = outer![1];
        Assert.Equal(new[] { "Keep both", "Thing_Mugirl_Retired7" }, nested.Values);
        Assert.Equal(new[] { "null", "Thing_Steel8" }, Scribe.loader.crossRefs.loadIDs.TakeList("/keys", nested));
        Assert.Equal(-1, SaveXml.Actions(session, "Framework.LoadIds.DictionaryReferences"));
    }

    [Fact]
    public void UnknownKeyReaderAndMismatchedDictionaryCountsFailWithoutRemovingXml()
    {
        const string Dictionary = "<memory><keys><li>Thing_Mugirl_Retired7</li><li>Thing_" + OldThing + "</li></keys><values><li>17</li></values></memory>";
        XmlDocument document = SaveXml.Save(Body + Dictionary);
        SaveUpgradeSession session = SaveXml.Upgrade(document, Tables());
        using (var scope = new ScribeScope(session, document.DocumentElement!["game"]!))
        {
            Scribe.loader.curParent = new ReferenceObject("mismatch");
            new DictionaryReader<int>(LookMode.Value).ExposeData();
            Assert.True(session.Report.Failed);
            Assert.Equal("Framework.LoadIds.DictionaryReferences", session.Report.FailedStep);
            Assert.Contains("mismatched", session.Report.Error, StringComparison.Ordinal);
            Assert.Equal(Dictionary, document.DocumentElement!["game"]!["memory"]!.OuterXml);
        }

        XmlDocument unknown = SaveXml.Save(Body + "<keys><li>Thing_Mugirl_Retired7</li></keys><values><li>17</li></values>");
        SaveUpgradeSession unknownSession = SaveXml.Upgrade(unknown, Tables());
        using (var scope = new ScribeScope(unknownSession, unknown.DocumentElement!["game"]!["keys"]!))
        {
            List<string> original = ["Thing_Mugirl_Retired7"];
            Scribe.loader.crossRefs.loadIDs.RegisterLoadIDListReadFromXml(original, "unknown", new ReferenceObject("custom"));
            Assert.True(unknownSession.Report.Failed);
            Assert.Contains("could not be verified", unknownSession.Report.Error, StringComparison.Ordinal);
            Assert.Equal("Thing_Mugirl_Retired7", Assert.Single(original));
            Assert.Equal("<li>17</li>", unknown.DocumentElement!["game"]!["values"]!.InnerXml);
        }
    }

    [Fact]
    public void DeletedDictionaryKeysWithDeepValuesRequireExplicitLifetimeMigration()
    {
        const string Memory = "<memory><keys><li>Thing_Mugirl_Retired7</li></keys><values><li><keys /><values><li>Keep owned data</li></values></li></values></memory>";
        XmlDocument document = SaveXml.Save(Body + Memory);
        SaveUpgradeSession session = SaveXml.Upgrade(document, Tables());
        using var scope = new ScribeScope(session, document.DocumentElement!["game"]!);
        var reader = new DictionaryReader<NestedReferences>(LookMode.Deep);
        Scribe.loader.curParent = reader;
        reader.ExposeData();

        Assert.True(session.Report.Failed);
        Assert.Contains("deep value", session.Report.Error, StringComparison.Ordinal);
        Assert.False(session.Verified);
        Assert.Equal(Memory, document.DocumentElement!["game"]!["memory"]!.OuterXml);
        Assert.Equal("Keep owned data", Assert.Single(Assert.Single(reader.Values!).Values!));
        Assert.Equal(-1, SaveXml.Actions(session, "Framework.LoadIds.DictionaryReferences"));
    }

    private sealed class DictionaryReader<T>(LookMode valuesMode) : IExposable
    {
        internal Dictionary<ReferenceObject, T>? Items;
        internal List<ReferenceObject>? Keys;
        internal List<T>? Values;
        public void ExposeData() => Scribe_Collections.Look(ref Items, "memory", LookMode.Reference, valuesMode, ref Keys, ref Values);
    }

    private sealed class NestedReferences : IExposable
    {
        internal List<ReferenceObject>? Keys;
        internal List<string>? Values;
        public NestedReferences() { }
        public void ExposeData()
        {
            Scribe_Collections.Look(ref Keys, "keys", LookMode.Reference);
            Scribe_Collections.Look(ref Values, "values", LookMode.Value);
        }
    }

    private static T ReadVerbField<T>(Verb verb, string name) =>
        (T)typeof(Verb).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(verb)!;

    private sealed class ScribeScope : IDisposable
    {
        private static readonly FieldInfo ActiveSession = typeof(SaveUpgradeRuntime).GetField("activeSession", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? previousSession = ActiveSession.GetValue(null);
        private readonly ScribeLoader previousLoader = Scribe.loader;
        private readonly LoadSaveMode previousMode = Scribe.mode;
        private readonly Harmony harmony = new("Mugirl.Tests.ReferenceAliases");

        internal ScribeScope(SaveUpgradeSession session, XmlElement node)
        {
            try
            {
                harmony.CreateClassProcessor(typeof(Harmony_LoadIDsWantedBank_RegisterLoadIDReadFromXml)).Patch();
                harmony.CreateClassProcessor(typeof(Harmony_LoadIDsWantedBank_RegisterLoadIDListReadFromXml)).Patch();
                harmony.CreateClassProcessor(typeof(Harmony_Verb_ExposeData_LoadId)).Patch();
                ActiveSession.SetValue(null, session);
                Scribe.loader = new ScribeLoader { curXmlParent = node };
                Scribe.mode = LoadSaveMode.LoadingVars;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            harmony.UnpatchAll(harmony.Id);
            Scribe.loader = previousLoader;
            Scribe.mode = previousMode;
            ActiveSession.SetValue(null, previousSession);
        }
    }
}
