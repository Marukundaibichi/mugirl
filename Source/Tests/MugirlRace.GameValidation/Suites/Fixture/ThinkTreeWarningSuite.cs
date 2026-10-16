using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.Logging;
using Mugirl.GameValidation.Core;
using RimWorld;
using Verse.AI;

namespace Mugirl.GameValidation.Suites;

/// <summary>精确重现报告中的两种来源失败，读取真实 Pawn_JobTracker/JobDriver，再保存为当前结构并重载。</summary>
internal static class ThinkTreeWarningSuite
{
    internal const string Name = "think-warnings";
    private const int ToiletKey = -1642323718;
    private const int RestKey = 566784108;
    private const string Marker = "mugirlUnresolvedThinkSource";
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static ValidationSuite Create() => new(Name,
        "reported wrong-tree and uncaptured keys preserve Job/driver progress, schema and repeated saves",
        SuiteNeeds.MugirlMod, [new ValidationStep("reported-cases", Run, TimeoutSeconds: 180)], inDefaultRun: false)
    {
        InAllRun = false,
        LogPolicy = SuiteLogPolicy.AnyError,
    };

    private static IEnumerable<Wait> Run(ValidationContext ctx)
    {
        Assembly assembly = ctx.RequireTarget().Assembly!;
        bool negative = ctx.Options.SuiteOption(Name + ".expectFailure", "false") == "true";
        ThinkTreeDef human = DefDatabase<ThinkTreeDef>.GetNamed("Humanlike");
        ThinkTreeDef mugirl = DefDatabase<ThinkTreeDef>.GetNamed("MugirlLike");
        ThinkNode normal = mugirl.thinkRoot.ThisAndChildrenRecursive.OfType<JobGiver_GetRest>().First();
        if (normal.UniqueSaveKey is ToiletKey or RestKey) { throw new InvalidOperationException("Fixture key overlaps the positive control."); }
        ctx.ExpectLog(entry => entry.Level == LogLevel.Warning && (entry.Message.Trim() == "Could not find think node with key " + ToiletKey
            || entry.Message.Trim() == "Could not find think node with key " + RestKey));
        var toilet = new JobDef { defName = "UseToilet", driverClass = typeof(JobDriver_ThinkSourceProbe) };
        if (DefDatabase<JobDef>.GetNamedSilentFail(toilet.defName) is not null) { throw new InvalidOperationException("The isolated fixture requires no installed UseToilet definition."); }
        DefDatabase<JobDef>.Add(toilet);
        using var ledger = new LedgerFixture(assembly, mugirl.thinkRoot);
        try
        {
            ctx.Report.Check(!human.TryGetThinkNodeWithSaveKey(ToiletKey, out _) && !mugirl.TryGetThinkNodeWithSaveKey(RestKey, out _),
                "both reported keys are absent from their actual trees", () => "native key collision in fixture environment");
            foreach (string scenario in negative ? new[] { "toilet", "rest" } : new[] { "toilet", "rest", "mixed", "mixed-reverse" })
            {
                string input = ctx.ArtifactPath(scenario + "-input.xml")!;
                WriteInput(input, scenario, normal.UniqueSaveKey);
                byte[] beforeHash = SHA256.HashData(File.ReadAllBytes(input));
                var api = new ReadApi(assembly);
                List<Pawn_JobTracker> first = api.Read(input, out string report, out IExposable hub);
                File.WriteAllText(ctx.ArtifactPath(scenario + "-report.json")!, report);
                using JsonDocument diagnostics = JsonDocument.Parse(report);
                JsonElement result = diagnostics.RootElement;
                if (negative)
                {
                    ctx.Report.Check(result.GetProperty("state").GetString() == "Failed"
                        && result.GetProperty("failedStep").GetString() == "Framework.ThinkTreeKeys"
                        && result.GetProperty("error").GetString()!.Contains("Reason=" + (scenario == "toilet" ? "wrong-tree-or-writer" : "uncaptured-key"), StringComparison.Ordinal),
                        "1016.3 reproduces " + scenario + " failure", () => report);
                    continue;
                }
                int missing = scenario.StartsWith("mixed", StringComparison.Ordinal) ? 2 : 1;
                if (!ctx.Report.Check(result.GetProperty("state").GetString() == "Upgraded"
                    && result.GetProperty("failedStep").ValueKind == JsonValueKind.Null
                    && result.GetProperty("warnings").GetArrayLength() == missing,
                    scenario + " upgrades with exactly one warning per unresolved source", () => report)) { continue; }
                ctx.Report.Check(first.Where(item => item.curJob.loadID != 9003).All(item => item.curJob.jobGiver is null)
                    && ReferenceEquals(first.Single(item => item.curJob.loadID == 9003).curJob.jobGiver, normal),
                    scenario + " keeps unresolved sources null and the proven source exact", () => "source binding changed");
                string saved = ctx.ArtifactPath(scenario + "-saved.xml")!;
                Write(saved, first, hub);
                CheckProgress(ctx, input, saved, scenario);
                List<Pawn_JobTracker> second = api.Read(saved, out string secondReport, out IExposable secondHub);
                File.WriteAllText(ctx.ArtifactPath(scenario + "-reload-report.json")!, secondReport);
                using (JsonDocument reload = JsonDocument.Parse(secondReport))
                {
                    ctx.Report.Check(reload.RootElement.GetProperty("state").GetString() == "NotLegacy"
                        && reload.RootElement.GetProperty("failedStep").ValueKind == JsonValueKind.Null
                        && reload.RootElement.GetProperty("warnings").GetArrayLength() == missing,
                        scenario + " reload keeps schema 1 and warning-only sources", () => secondReport);
                }
                string resaved = ctx.ArtifactPath(scenario + "-resaved.xml")!;
                Write(resaved, second, secondHub);
                CheckProgress(ctx, input, resaved, scenario + " reload");
                XmlDocument one = Load(saved), two = Load(resaved);
                ctx.Report.Check(one.SelectSingleNode("/savegame/game/trackers")!.OuterXml == two.SelectSingleNode("/savegame/game/trackers")!.OuterXml,
                    scenario + " repeated save preserves every Job/driver field", () => "tracker serialization differs");
                foreach (Pawn_JobTracker tracker in second)
                {
                    ctx.Report.Check(ReferenceEquals(tracker.curDriver.job, tracker.curJob) && ReferenceEquals(tracker.curDriver.pawn.jobs, tracker)
                        && !tracker.curDriver.ended, scenario + " restored tracker/driver identity " + tracker.curJob.loadID, () => "job was reset");
                    if (tracker.curDriver is JobDriver_ThinkSourceProbe probe)
                    {
                        int ticks = probe.ticksLeftThisToil, work = probe.workDone, init = probe.initCount;
                        probe.DriverTick(); probe.DriverTick(); probe.DriverTick();
                        ctx.Report.Check(probe.ticksLeftThisToil == ticks - 3 && probe.workDone == work + 3 && probe.initCount == init
                            && probe.CurToilIndex == 1 && !probe.ended,
                            scenario + " external driver continues from its saved progress without replaying init", () => "continuation changed");
                    }
                }
                ctx.Report.Check(beforeHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input))), scenario + " input remains unchanged", () => input);
            }
            File.WriteAllText(ctx.ArtifactPath("think-warnings-build.json")!, JsonSerializer.Serialize(new
            {
                dllSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))), negative,
                note = "Synthetic exact-key ledger conditions; real Pawn_JobTracker, JobDriver_LayDown and Scribe. UseToilet uses a progress probe, not the unavailable hygiene mod.",
            }));
        }
        finally { DefDatabase<JobDef>.AllDefsListForReading.Remove(toilet); }
        yield break;
    }

    private static void WriteInput(string path, string scenario, int normalKey)
    {
        string toilet = Tracker(9001, "UseToilet", "Humanlike", ToiletKey, typeof(JobDriver_ThinkSourceProbe).FullName!,
            "<initCount>4</initCount><workDone>117</workDone>");
        // 原版 writer 省略 RimWorld 命名空间；夹具直接使用其规范写出名，避免把名字缩写误报成换驱动。
        string rest = Tracker(9002, "LayDown", "MugirlLike", RestKey, typeof(JobDriver_LayDown).Name,
            "<canMoveOrCrawl>True</canMoveOrCrawl><hasSavedValues>True</hasSavedValues><asleep>True</asleep>");
        string normal = Tracker(9003, "LayDown", "MugirlLike", normalKey, typeof(JobDriver_LayDown).Name,
            "<canMoveOrCrawl>True</canMoveOrCrawl><hasSavedValues>True</hasSavedValues>");
        string rows = scenario switch { "toilet" => toilet, "rest" => rest, "mixed" => toilet + rest, _ => rest + toilet };
        var xml = new XmlDocument { XmlResolver = null };
        xml.LoadXml("<savegame><meta><modIds><li>har.mugirlrace</li></modIds></meta><game><trackers>" + rows + normal + "</trackers></game></savegame>");
        xml.Save(path);
    }

    private static string Tracker(int id, string job, string tree, int key, string driver, string extra) =>
        "<li><curJob><def>" + job + "</def><loadID>" + id + "</loadID><startTick>1234</startTick><expiryInterval>5432</expiryInterval>"
        + "<count>7</count><targetA>(4, 0, 7)</targetA><targetB>(8, 0, 9)</targetB><forceSleep>True</forceSleep>"
        + "<jobGiverThinkTree>" + tree + "</jobGiverThinkTree><lastJobGiverKey>" + key + "</lastJobGiverKey></curJob>"
        + "<curDriver Class=\"" + driver + "\"><curToilIndex>1</curToilIndex><nextToilIndex>2</nextToilIndex>"
        + "<ticksLeftThisToil>137</ticksLeftThisToil><startTick>1200</startTick><uninstallWorkLeft>23.5</uninstallWorkLeft>" + extra + "</curDriver></li>";

    private static void CheckProgress(ValidationContext ctx, string original, string saved, string label)
    {
        XmlDocument before = Load(original), after = Load(saved);
        ctx.Report.Check(after.SelectSingleNode("/savegame/game/components/li/mugirlSaveSchema")?.InnerText == "1",
            label + " actual game component writes schema 1", () => after.OuterXml);
        XmlElement[] actual = after.SelectNodes("/savegame/game/trackers/li")!.Cast<XmlElement>().ToArray();
        foreach (XmlElement expected in before.SelectNodes("/savegame/game/trackers/li")!)
        {
            XmlElement row = actual.Single(item => item["curJob"]!["loadID"]!.InnerText == expected["curJob"]!["loadID"]!.InnerText);
            foreach (string section in new[] { "curJob", "curDriver" })
            {
                foreach (XmlElement field in expected[section]!.ChildNodes.OfType<XmlElement>())
                {
                    ctx.Report.Check(row[section]![field.Name]?.InnerText == field.InnerText,
                        label + " " + expected["curJob"]!["loadID"]!.InnerText + " preserves " + section + "/" + field.Name, () => row.OuterXml);
                }
            }
            bool missing = expected["curJob"]!["loadID"]!.InnerText != "9003";
            ctx.Report.Check((row["curJob"]![Marker]?.InnerText == "True") == missing
                && row["curJob"]!.SelectNodes("lastJobGiverKey")!.Count == 1,
                label + " source retention marker is precise", () => row.OuterXml);
            ctx.Report.Check(row["curDriver"]!.GetAttribute("Class") == expected["curDriver"]!.GetAttribute("Class"),
                label + " driver type is unchanged", () => row.OuterXml);
        }
    }

    private static void Write(string path, List<Pawn_JobTracker> trackers, IExposable hub)
    {
        Scribe.saver.InitSaving(path, "savegame");
        Scribe.EnterNode("game");
        Scribe.EnterNode("components");
        Scribe_Deep.Look(ref hub, "li");
        Scribe.ExitNode();
        Scribe.EnterNode("trackers");
        foreach (Pawn_JobTracker item in trackers) { Pawn_JobTracker value = item; Scribe_Deep.Look(ref value, "li"); }
        Scribe.ExitNode(); Scribe.ExitNode();
        Scribe.saver.FinalizeSaving();
        XmlDocument xml = Load(path);
        XmlElement meta = xml.CreateElement("meta");
        meta.InnerXml = "<modIds><li>har.mugirlrace</li></modIds>";
        xml.DocumentElement!.PrependChild(meta);
        xml.Save(path);
    }

    private static XmlDocument Load(string path) { var document = new XmlDocument { XmlResolver = null }; document.Load(path); return document; }

    private sealed class ReadApi(Assembly assembly)
    {
        internal List<Pawn_JobTracker> Read(string path, out string report, out IExposable hub)
        {
            Type sessionType = assembly.GetType("Mugirl.SaveUpgrade.SaveUpgradeSession", true)!;
            FieldInfo active = assembly.GetType("Mugirl.SaveUpgrade.SaveUpgradeRuntime", true)!.GetField("activeSession", Static)!;
            object? previous = active.GetValue(null);
            try
            {
                Scribe.loader.InitLoading(path);
                object session = sessionType.GetMethod("Start", Static)!.Invoke(null, [973, Scribe.loader.curXmlParent.OwnerDocument!, path])!;
                object tables = assembly.GetType("Mugirl.SaveUpgrade.UpgradeTables", true)!.GetProperty("Default", Static)!.GetValue(null)!;
                object catalog = assembly.GetType("Mugirl.SaveUpgrade.DefDatabaseCatalog", true)!.GetProperty("Instance", Static)!.GetValue(null)!;
                sessionType.GetMethod("RunDom", Instance)!.Invoke(session, [tables, catalog]);
                active.SetValue(null, session);
                var trackers = new List<Pawn_JobTracker>();
                Scribe.EnterNode("game"); Scribe.EnterNode("trackers");
                XmlNode parent = Scribe.loader.curXmlParent;
                foreach (XmlElement row in parent.ChildNodes.OfType<XmlElement>())
                {
                    var pawn = new Pawn();
                    var tracker = new Pawn_JobTracker(pawn);
                    pawn.jobs = tracker;
                    Scribe.loader.curXmlParent = row;
                    Scribe.loader.curParent = tracker;
                    Scribe.loader.curPathRelToParent = "";
                    // 顶层 tracker 按原版字段读取；Job/driver/queue 仍经真实 Scribe_Deep 注册与恢复。
                    tracker.ExposeData();
                    trackers.Add(tracker);
                }
                Scribe.loader.curXmlParent = null; Scribe.loader.curParent = null; Scribe.loader.curPathRelToParent = null;
                Scribe.mode = LoadSaveMode.Inactive;
                Scribe.loader.crossRefs.ResolveAllCrossReferences();
                Scribe.loader.initer.DoAllPostLoadInits();
                Scribe.ForceStop();
                Type phase = assembly.GetType("Mugirl.SaveUpgrade.UpgradePhase", true)!;
                foreach (string name in new[] { "A", "B" }) { sessionType.GetMethod("MarkPhase", Instance)!.Invoke(session, [Enum.Parse(phase, name)]); }
                sessionType.GetMethod("Complete", Instance)!.Invoke(session, null);
                object value = sessionType.GetProperty("Report", Instance)!.GetValue(session)!;
                report = (string)value.GetType().GetMethod("ToJson", Instance)!.Invoke(value, null)!;
                bool verified = (bool)sessionType.GetProperty("Verified", Instance)!.GetValue(session)!;
                Type hubType = assembly.GetType("Mugirl.Core.GameComponent_Mugirl", true)!;
                hub = (IExposable)Activator.CreateInstance(hubType, [Current.Game])!;
                int schema = (int)sessionType.GetProperty("SourceSchema", Instance)!.GetValue(session)!;
                hubType.GetMethod("AcceptLoad", Instance)!.Invoke(hub, [schema, verified, report, null, null]);
                return trackers;
            }
            finally { Scribe.ForceStop(); active.SetValue(null, previous); }
        }
    }

    // 只替换两个键的账本条目，模拟报告中的拓扑证据；不改真实节点键或分配顺序，结束时逐项还原。
    private sealed class LedgerFixture : IDisposable
    {
        private readonly List<(Dictionary<int, ThinkNode> Nodes, int Key, ThinkNode? Previous)> originals = [];
        internal LedgerFixture(Assembly assembly, ThinkNode wrongTreeNode)
        {
            Type type = assembly.GetType("Mugirl.SaveUpgrade.LegacyThinkTreeKeys", true)!;
            foreach (string name in new[] { "Ledger", "BeforeCorporateDuty", "FirstPublishedLayout", "CurrentLayout" })
            {
                object ledger = type.GetField(name, Static)!.GetValue(null)!;
                var nodes = (Dictionary<int, ThinkNode>)ledger.GetType().GetField("nodes", Instance)!.GetValue(ledger)!;
                foreach (int key in new[] { ToiletKey, RestKey })
                {
                    nodes.TryGetValue(key, out ThinkNode? old);
                    originals.Add((nodes, key, old));
                    if (key == ToiletKey) { nodes[key] = wrongTreeNode; } else { nodes.Remove(key); }
                }
            }
        }
        public void Dispose()
        {
            foreach (var item in originals) { if (item.Previous is { } node) { item.Nodes[item.Key] = node; } else { item.Nodes.Remove(item.Key); } }
        }
    }
}
