using System.IO;
using System.Reflection;
using System.Xml;
using HarmonyLib;
using Mugirl.Features.Incidents;
using Mugirl.SaveUpgrade;
using RimWorld;
using Verse;
using Verse.AI;

namespace Mugirl.Tests.SaveUpgrade;

[Collection("Reference aliases")]
public sealed class ThinkTreeProfileSelectionTests
{
    [Theory]
    [InlineData("resolved", false)]
    [InlineData("resolved", true)]
    [InlineData("ambiguous", false)]
    [InlineData("mixed", false)]
    [InlineData("late", false)]
    [InlineData("current-resolved", false)]
    [InlineData("current-resolved", true)]
    [InlineData("current-ambiguous", false)]
    [InlineData("current-mixed", false)]
    [InlineData("current-late", false)]
    public void RealScribeUsesAllActualJobSourcesAndDefersAmbiguousSourcesWithoutBlockingUpgrade(string scenario, bool reverseReadOrder)
    {
        using var errors = new ScribeErrorCapture();
        var harmony = new Harmony("Mugirl.Tests.ThinkTreeProfileSelection");
        string directory = Path.Combine(Path.GetTempPath(), "mugirl-think-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        ThinkTreeDef[] original = [.. DefDatabase<ThinkTreeDef>.AllDefsListForReading];
        FieldInfo active = typeof(SaveUpgradeRuntime).GetField("activeSession", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previous = active.GetValue(null);
        try
        {
            bool currentLayout = scenario.StartsWith("current-", StringComparison.Ordinal);
            if (currentLayout) { scenario = scenario["current-".Length..]; }
            // 两个真实 JobGiver 的旧整数在两个布局中互换；另一实际读入 Job 才能证明选择。
            var first = new JobGiver_Work();
            var second = new JobGiver_Work();
            ThinkNode earlierOnly = currentLayout ? new JobGiver_RegisterPunisher() : new JobGiver_GetFood();
            var laterOnly = new JobGiver_GetRest();
            var tree = new ThinkTreeDef { defName = "ProfileSelection", thinkRoot = new ThinkNode_Priority
                { subNodes = currentLayout ? [second, first, earlierOnly, laterOnly] : [first, second, earlierOnly, laterOnly] } };
            ThinkTreeKeyAssigner.Reset();
            LegacyThinkTreeKeys.Reset();
            ThinkTreeKeyAssigner.AssignKeys(tree.thinkRoot, currentLayout ? 0 : 817);
            LegacyThinkTreeLedger latest = Ledger("Ledger"), earlier = Ledger(currentLayout ? "CurrentLayout" : "BeforeCorporateDuty");
            latest.Capture(currentLayout ? [first, second, earlierOnly] : [first, second, laterOnly], 0);
            earlier.Capture(currentLayout ? tree.thinkRoot.ThisAndChildrenRecursive : [second, first, earlierOnly], 0);
            int sharedKey = latest.KeyFor(first);
            Assert.Equal(sharedKey, earlier.KeyFor(second));
            if (currentLayout) { Assert.NotEqual(latest.KeyFor(earlierOnly), earlier.KeyFor(earlierOnly)); }
            int firstSavedKey = scenario == "mixed" ? latest.KeyFor(currentLayout ? earlierOnly : laterOnly) : sharedKey;
            DefDatabase<ThinkTreeDef>.Add(tree);
            harmony.CreateClassProcessor(typeof(Harmony_Job_ExposeData_ThinkTreeIdentity)).Patch();
            harmony.CreateClassProcessor(typeof(Harmony_ThinkTreeDef_TryGetThinkNodeWithSaveKey)).Patch();
            string input = Path.Combine(directory, "input.xml");
            string data = JobXml("first", firstSavedKey);
            if (scenario != "ambiguous") { data += JobXml("second", earlier.KeyFor(earlierOnly)); }
            data += "<noSource><loadID>3</loadID></noSource><explicitNoSource><loadID>4</loadID><lastJobGiverKey>-1</lastJobGiverKey></explicitNoSource>";
            SaveXml.Save(data).Save(input);
            Scribe.loader.InitLoading(input);
            XmlDocument document = Scribe.loader.curXmlParent.OwnerDocument!;
            SaveUpgradeSession session = SaveUpgradeSession.Start(941, document, input);
            session.RunDom(SaveXml.Empty(), new TestCatalog());
            session.ReadTrace = new ValidationReadTrace();
            active.SetValue(null, session);
            Scribe.EnterNode("game");
            Job? one = null, two = null, noSource = null, explicitNoSource = null;
            if (reverseReadOrder) { Scribe_Deep.Look(ref two, "second"); }
            Scribe_Deep.Look(ref one, "first");
            if (scenario != "ambiguous" && !reverseReadOrder) { Scribe_Deep.Look(ref two, "second"); }
            Scribe_Deep.Look(ref noSource, "noSource");
            Scribe_Deep.Look(ref explicitNoSource, "explicitNoSource");
            Scribe.ExitNode();
            // 调用 FinalizeLoading 的两段真实读取阶段；离线没有它的 Prefs/DeepProfiler UI 环境。
            Scribe.loader.crossRefs.ResolveAllCrossReferences();
            Scribe.loader.initer.DoAllPostLoadInits();
            Scribe.ForceStop();
            Assert.Empty(errors.Messages);
            Assert.NotNull(one);
            Assert.Null(noSource!.jobGiver);
            Assert.Null(explicitNoSource!.jobGiver);
            if (scenario is "ambiguous" or "mixed")
            {
                Assert.Null(one.jobGiver);
                Assert.False(session.Report.Failed);
                Assert.Equal(scenario == "ambiguous" ? 1 : 2, session.Report.Warnings.Count);
                Assert.Contains(scenario == "ambiguous" ? "ambiguous-layouts" : "incompatible-layouts", session.Report.Warnings[0].Message, StringComparison.Ordinal);
                if (scenario == "mixed")
                {
                    Assert.Null(two!.jobGiver);
                    using var report = System.Text.Json.JsonDocument.Parse(session.Report.ToJson());
                    Assert.Equal(2, report.RootElement.GetProperty("warnings").GetArrayLength());
                }
                string saved = Path.Combine(directory, "deferred.xml");
                Scribe.saver.InitSaving(saved, "savegame");
                Scribe_Deep.Look(ref one, "first");
                Scribe.saver.FinalizeSaving();
                var output = new XmlDocument(); output.Load(saved);
                Assert.Equal(firstSavedKey.ToString(), output.SelectSingleNode("//lastJobGiverKey")!.InnerText);
                Assert.Equal("True", output.SelectSingleNode("//" + UnresolvedThinkTreeKeys.MarkerLabel)!.InnerText);
            }
            else
            {
                Assert.Same(second, one.jobGiver);
                Assert.Same(earlierOnly, two!.jobGiver);
                Assert.False(session.Report.Failed, session.Report.Error);
                if (scenario == "late")
                {
                    var late = new Job { jobGiverThinkTree = tree };
                    LegacyThinkJobScope scope = LegacyThinkTreeKeys.EnterJob(late, session,
                        LoadSaveMode.LoadingVars, document.DocumentElement!["game"]!["first"]);
                    LegacyThinkTreeKeys.LeaveJob(scope);
                    scope = LegacyThinkTreeKeys.EnterJob(late, session, LoadSaveMode.PostLoadInit, null);
                    int key = firstSavedKey;
                    Assert.False(LegacyThinkTreeKeys.PrepareLookup(tree, ref key, out _));
                    LegacyThinkTreeKeys.LeaveJob(scope);
                    Assert.False(session.Report.Failed);
                    Assert.Contains("late-source", Assert.Single(session.Report.Warnings).Message, StringComparison.Ordinal);
                }
                else
                {
                    string saved = Path.Combine(directory, "saved.xml");
                    Scribe.saver.InitSaving(saved, "savegame");
                    Scribe_Deep.Look(ref one, "first");
                    Scribe_Deep.Look(ref two, "second");
                    Scribe.saver.FinalizeSaving();
                    var output = new XmlDocument(); output.Load(saved);
                    Assert.Equal(second.UniqueSaveKey.ToString(), output.DocumentElement!["first"]!["lastJobGiverKey"]!.InnerText);
                    Assert.Equal(earlierOnly.UniqueSaveKey.ToString(), output.DocumentElement!["second"]!["lastJobGiverKey"]!.InnerText);
                }
            }
            session.MarkPhase(UpgradePhase.A);
            session.MarkPhase(UpgradePhase.B);
            session.Complete();
            Assert.True(session.Verified);
        }
        finally
        {
            Scribe.ForceStop();
            active.SetValue(null, previous);
            harmony.UnpatchAll(harmony.Id);
            ThinkTreeKeyAssigner.Reset();
            LegacyThinkTreeKeys.Reset();
            DefDatabase<ThinkTreeDef>.Clear();
            foreach (ThinkTreeDef definition in original) { DefDatabase<ThinkTreeDef>.Add(definition); }
            foreach (string file in Directory.GetFiles(directory)) { File.Delete(file); }
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void EnvironmentDiagnosticSeparatesAddedModsFromReorderingTheCommonMods()
    {
        string[] saved = ["Core", "HAR", "Mugirl"];
        string inserted = LegacyThinkTreeKeys.DescribeModDifference(saved, ["core", "new", "har", "mugirl"]);
        Assert.Contains("added=1 [new]", inserted, StringComparison.Ordinal);
        Assert.Contains("removed=0 []", inserted, StringComparison.Ordinal);
        Assert.Contains("commonOrderChanged=False", inserted, StringComparison.Ordinal);
        string reordered = LegacyThinkTreeKeys.DescribeModDifference(saved, ["core", "mugirl", "new", "har"]);
        Assert.Contains("commonOrderChanged=True", reordered, StringComparison.Ordinal);
        string removed = LegacyThinkTreeKeys.DescribeModDifference(saved, ["core", "mugirl"]);
        Assert.Contains("removed=1 [HAR]", removed, StringComparison.Ordinal);
        Assert.Contains("commonOrderChanged=False", removed, StringComparison.Ordinal);
    }

    private static LegacyThinkTreeLedger Ledger(string field) => (LegacyThinkTreeLedger)typeof(LegacyThinkTreeKeys)
        .GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    private static string JobXml(string name, int key) => "<" + name + "><loadID>" + (name == "first" ? 1 : 2)
        + "</loadID><jobGiverThinkTree>ProfileSelection</jobGiverThinkTree>"
        + "<lastJobGiverKey>" + key + "</lastJobGiverKey></" + name + ">";
}
