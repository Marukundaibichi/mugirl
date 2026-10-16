using System.IO;
using System.Reflection;
using System.Xml;
using HarmonyLib;
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
    public void RealScribeUsesAllActualJobSourcesBeforeItsFirstLookupAndKeepsFailuresUnverified(string scenario, bool reverseReadOrder)
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
            // 两个真实 JobGiver 的旧整数在两个布局中互换；另一实际读入 Job 才能证明选择。
            var first = new JobGiver_Work();
            var second = new JobGiver_Work();
            var earlierOnly = new JobGiver_GetFood();
            var laterOnly = new JobGiver_GetRest();
            var tree = new ThinkTreeDef { defName = "ProfileSelection", thinkRoot = new ThinkNode_Priority
                { subNodes = [first, second, earlierOnly, laterOnly] } };
            ThinkTreeKeyAssigner.Reset();
            LegacyThinkTreeKeys.Reset();
            ThinkTreeKeyAssigner.AssignKeys(tree.thinkRoot, 817);
            LegacyThinkTreeLedger latest = Ledger("Ledger"), earlier = Ledger("BeforeCorporateDuty");
            latest.Capture([first, second, laterOnly], 0);
            earlier.Capture([second, first, earlierOnly], 0);
            int sharedKey = latest.KeyFor(first);
            Assert.Equal(sharedKey, earlier.KeyFor(second));
            int firstSavedKey = scenario == "mixed" ? latest.KeyFor(laterOnly) : sharedKey;
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
                Assert.True(session.Report.Failed);
                Assert.Equal("Framework.ThinkTreeKeys", session.Report.FailedStep);
                Assert.Contains(scenario == "ambiguous" ? "layouts disagree" : "No single captured", session.Report.Error, StringComparison.Ordinal);
            }
            else
            {
                Assert.Same(second, one.jobGiver);
                Assert.Same(earlierOnly, two!.jobGiver);
                Assert.False(session.Report.Failed, session.Report.Error);
                if (scenario == "late")
                {
                    LegacyThinkJobScope scope = LegacyThinkTreeKeys.EnterJob(new Job(), session,
                        LoadSaveMode.LoadingVars, document.DocumentElement!["game"]!["first"]);
                    LegacyThinkTreeKeys.LeaveJob(scope);
                    Assert.True(session.Report.Failed);
                    Assert.Contains("after the saved ThinkTree layout", session.Report.Error, StringComparison.Ordinal);
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
            Assert.Equal(scenario == "resolved", session.Verified);
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

    private static LegacyThinkTreeLedger Ledger(string field) => (LegacyThinkTreeLedger)typeof(LegacyThinkTreeKeys)
        .GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    private static string JobXml(string name, int key) => "<" + name + "><loadID>" + (name == "first" ? 1 : 2)
        + "</loadID><jobGiverThinkTree>ProfileSelection</jobGiverThinkTree>"
        + "<lastJobGiverKey>" + key + "</lastJobGiverKey></" + name + ">";
}
