using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.Logging;
using Mugirl.GameValidation.Core;
using Verse.AI;

namespace Mugirl.GameValidation.Suites;

/// <summary>从报告存档抽取真实 Job 来源及值字段，重放 Scribe 读写；不把缺少第三方内容的完整世界装入游戏。</summary>
internal static class ThinkTreeMigrationSuite
{
    public const string Name = "think-migration";

    public static ValidationSuite Create() => new(Name,
        "replay supplied Job source keys through real Scribe, preserve unresolved native keys and verify current-schema reload",
        SuiteNeeds.MugirlMod, [new ValidationStep("sources", Run, TimeoutSeconds: 180)], inDefaultRun: false)
    {
        InAllRun = false,
        LogPolicy = SuiteLogPolicy.AnyError,
    };

    private static IEnumerable<Wait> Run(ValidationContext ctx)
    {
        bool currentLayout = ctx.Options.SuiteOption(Name + ".currentLayout", "false") == "true";
        string input = currentLayout ? ctx.ArtifactPath("current-layout-schema0.xml")! : ctx.Options.SuiteOption(Name + ".source", "");
        bool expectFailure = ctx.Options.SuiteOption(Name + ".expectFailure", "false") == "true";
        bool allowMissingDefs = ctx.Options.SuiteOption(Name + ".allowMissingDefs", "false") == "true";
        if (currentLayout) { WriteCurrentLayoutSample(input); }
        byte[] hash = SHA256.HashData(File.ReadAllBytes(input));
        var source = new XmlDocument { XmlResolver = null };
        source.Load(input);
        XmlElement[] original = source.SelectNodes("//*[lastJobGiverKey]")!.Cast<XmlElement>().ToArray();
        if (!ctx.Report.Check(original.Length > 0, "supplied save has Job sources", () => input)) { yield break; }
        using var placeholders = new MissingJobDefinitions(original, allowMissingDefs);

        var sample = new XmlDocument { XmlResolver = null };
        sample.LoadXml("<savegame><game><jobs /></game></savegame>");
        sample.DocumentElement!.PrependChild(sample.ImportNode(source.DocumentElement!["meta"]!, true));
        XmlElement jobsNode = sample.DocumentElement["game"]!["jobs"]!;
        string[] fields = ["def", "loadID", "startTick", "expiryInterval", "checkOverrideOnExpire", "playerForced",
            "playerInterruptedForced", "count", "maxNumMeleeAttacks", "maxNumStaticAttacks", "locomotionUrgency",
            "haulMode", "canBash", "canBashFences", "forceSleep", "restUntilHealed", "ignoreForbidden",
            "followRadius", "jobGiverThinkTree", "lastJobGiverKey"];
        var expected = new List<(int Id, int Key, string Tree, bool NativeMissing, ThinkNode? NativeNode)>();
        var missingTrees = new HashSet<string>(StringComparer.Ordinal);
        foreach (XmlElement job in original)
        {
            if (job.HasAttribute("Class")) { throw new InvalidOperationException("This replay expects base Job entries."); }
            XmlElement row = sample.CreateElement("job" + expected.Count.ToString(CultureInfo.InvariantCulture));
            foreach (string field in fields)
            {
                if (job[field] is { } value) { row.AppendChild(sample.ImportNode(value, true)); }
            }
            jobsNode.AppendChild(row);
            string treeName = job["jobGiverThinkTree"]!.InnerText;
            int key = int.Parse(job["lastJobGiverKey"]!.InnerText, CultureInfo.InvariantCulture);
            ThinkTreeDef? tree = allowMissingDefs ? DefDatabase<ThinkTreeDef>.GetNamedSilentFail(treeName) : DefDatabase<ThinkTreeDef>.GetNamed(treeName);
            ThinkNode? nativeNode = null;
            bool missing = tree is null || !tree.TryGetThinkNodeWithSaveKey(key, out nativeNode);
            if (tree is null) { missingTrees.Add(treeName); }
            expected.Add((int.Parse(job["loadID"]!.InnerText, CultureInfo.InvariantCulture), key, treeName, missing, nativeNode));
        }
        string[] warnings = [.. expected.Where((item, i) => (allowMissingDefs || item.NativeMissing) && !missingTrees.Contains(item.Tree) || (expectFailure && currentLayout && i == 0)).Select(item => "Could not find think node with key "
            + item.Key.ToString(CultureInfo.InvariantCulture)).Distinct(StringComparer.Ordinal)];
        // 正向只接受原版已证明缺失的确切键；负对照另核对首次被错误拒绝的当前键，均保留并检查实际警告。
        ctx.ExpectLog(entry => entry.Level == LogLevel.Warning && warnings.Any(text => entry.Message.Trim() == text));
        // 此模式只重放 Job 值；缺失的第三方树保持真正的 null，原版的确切 Def 缺失日志必须逐条计数。
        string[] missingDefErrors = [.. missingTrees.Select(tree => "Could not load reference to Verse.ThinkTreeDef named " + tree)];
        ctx.ExpectLog(entry => entry.Level == LogLevel.Error && missingDefErrors.Contains(entry.Message.Trim(), StringComparer.Ordinal));
        int logStart = ctx.Logs.Count;
        Assembly assembly = ctx.RequireTarget().Assembly ?? throw new InvalidOperationException("MugirlRace is not loaded.");
        var api = new ReplayApi(assembly);
        string firstPath = ctx.ArtifactPath("think-input.xml")!;
        sample.Save(firstPath);
        List<Job> first = api.Read(firstPath, out string firstReport);
        File.WriteAllText(ctx.ArtifactPath("think-first-report.json")!, firstReport);
        using (JsonDocument report = JsonDocument.Parse(firstReport))
        {
            string? failedStep = report.RootElement.GetProperty("failedStep").GetString();
            if (expectFailure)
            {
                ctx.Report.Check(failedStep == "Framework.ThinkTreeKeys", "unfixed build reproduces the reported failure", () => firstReport);
                ctx.Report.Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input))), "original input is unchanged", () => input);
                LogCapture.DrainInto(ctx.Logs);
                if (currentLayout)
                {
                    string rejected = "Could not find think node with key " + expected[0].Key.ToString(CultureInfo.InvariantCulture);
                    ctx.Report.Check(expected.All(item => !item.NativeMissing)
                        && ctx.Logs.Skip(logStart).Count(entry => entry.Level == LogLevel.Warning && entry.Message.Trim() == rejected) == 1,
                        "unfixed upgrade rejects one source that native lookup can resolve", () => firstReport);
                }
                yield break;
            }
            if (!ctx.Report.Check(failedStep is null && report.RootElement.GetProperty("state").GetString() == "Upgraded",
                "proven sources do not fail the upgrade", () => firstReport)) { yield break; }
        }
        ctx.Report.Check(first.Count == expected.Count, "every sampled Job was read", () => $"expected {expected.Count}, actual {first.Count}");
        if (currentLayout)
        {
            ctx.Report.Check(expected.All(item => !item.NativeMissing), "current layout keys exist in the actual tree", () => "a native source is missing");
            ctx.Report.Check(first.Select((job, i) => ReferenceEquals(job.jobGiver, expected[i].NativeNode)).All(same => same),
                "schema-0 reload preserves every actual current source", () => "a current node was rebound");
        }
        string savedPath = ctx.ArtifactPath("think-saved.xml")!;
        Write(savedPath, first, sample["savegame"]!["meta"]!);
        CheckKeys(ctx, savedPath, expected, allowMissingDefs ? first : null);
        List<Job> second = api.Read(savedPath, out string secondReport);
        File.WriteAllText(ctx.ArtifactPath("think-second-report.json")!, secondReport);
        using (JsonDocument report = JsonDocument.Parse(secondReport))
        {
            ctx.Report.Check(report.RootElement.GetProperty("state").GetString() == "NotLegacy", "reload uses current schema", () => secondReport);
        }
        for (int i = 0; i < first.Count; i++)
        {
            Job before = first[i], after = second[i];
            ctx.Report.Check(before.loadID == after.loadID && before.startTick == after.startTick
                && before.expiryInterval == after.expiryInterval && before.count == after.count
                && ReferenceEquals(before.def, after.def) && ReferenceEquals(before.jobGiverThinkTree, after.jobGiverThinkTree)
                && ReferenceEquals(before.jobGiver, after.jobGiver),
                "Job " + before.loadID + " retains its identity, counters and actual source", () => "Job state changed");
        }
        string secondPath = ctx.ArtifactPath("think-resaved.xml")!;
        Write(secondPath, second, sample["savegame"]!["meta"]!);
        CheckKeys(ctx, secondPath, expected, allowMissingDefs ? first : null);
        ctx.Report.Check(File.ReadAllText(savedPath) == File.ReadAllText(secondPath), "the repeated save is stable", () => "serialized Job data changed");
        LogCapture.DrainInto(ctx.Logs);
        int actualWarnings = ctx.Logs.Skip(logStart).Count(entry => entry.Level == LogLevel.Warning && warnings.Contains(entry.Message.Trim(), StringComparer.Ordinal));
        int expectedWarnings = allowMissingDefs ? first.Count(job => job.jobGiver is null && job.jobGiverThinkTree is not null)
            : expected.Count(item => item.NativeMissing && !missingTrees.Contains(item.Tree));
        ctx.Report.Check(actualWarnings == expectedWarnings * 2, "native missing-key warnings remain visible", () => "actual " + actualWarnings);
        int actualMissingDefs = ctx.Logs.Skip(logStart).Count(entry => entry.Level == LogLevel.Error && missingDefErrors.Contains(entry.Message.Trim(), StringComparer.Ordinal));
        ctx.Report.Check(actualMissingDefs == expected.Count(item => missingTrees.Contains(item.Tree)) * 2,
            "unavailable third-party trees remain explicitly recorded", () => "actual " + actualMissingDefs);
        ctx.Report.Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input))), "original input is unchanged", () => input);
        File.WriteAllText(ctx.ArtifactPath("think-replay-summary.json")!, JsonSerializer.Serialize(new
        {
            sourceSha256 = Convert.ToHexString(hash),
            modSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
            jobs = expected.Count, nativeMissing = expected.Count(item => item.NativeMissing), actualWarnings,
            valueOnlyJobDefinitions = placeholders.Names, missingTrees = missingTrees.ToArray(), actualMissingDefs,
            sources = expected.Select(item => new { item.Id, item.Tree, item.Key, item.NativeMissing }),
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    // 缺少实际第三方驱动时，只提供按名字解析 Job.def 的值载体；没有伪造任务树或模拟其玩法。
    private sealed class MissingJobDefinitions : IDisposable
    {
        private readonly JobDef[] previous;
        internal string[] Names { get; }
        internal MissingJobDefinitions(XmlElement[] jobs, bool enabled)
        {
            previous = [.. DefDatabase<JobDef>.AllDefsListForReading];
            Names = enabled ? [.. jobs.Select(job => job["def"]!.InnerText).Distinct(StringComparer.Ordinal)
                .Where(name => DefDatabase<JobDef>.GetNamedSilentFail(name) is null)] : [];
            foreach (string name in Names) { DefDatabase<JobDef>.Add(new JobDef { defName = name, driverClass = typeof(JobDriver) }); }
        }
        public void Dispose()
        {
            if (Names.Length == 0) { return; }
            DefDatabase<JobDef>.Clear();
            foreach (JobDef definition in previous) { DefDatabase<JobDef>.Add(definition); }
        }
    }

    private static void CheckKeys(ValidationContext ctx, string path, List<(int Id, int Key, string Tree, bool NativeMissing, ThinkNode? NativeNode)> expected,
        IReadOnlyList<Job>? resolved = null)
    {
        var document = new XmlDocument { XmlResolver = null };
        document.Load(path);
        XmlElement[] actual = document.SelectNodes("/savegame/game/jobs/*")!.Cast<XmlElement>().ToArray();
        for (int i = 0; i < expected.Count; i++)
        {
            var row = expected[i];
            int key = resolved?[i].jobGiver?.UniqueSaveKey ?? row.Key;
            ctx.Report.Check(actual[i]["lastJobGiverKey"]?.InnerText == key.ToString(CultureInfo.InvariantCulture)
                && actual[i].SelectNodes("lastJobGiverKey")!.Count == 1,
                "Job " + row.Id + " keeps its original unresolved key or proven current source", () => actual[i].OuterXml);
            if (resolved is not null)
            {
                ctx.Report.Check((actual[i]["mugirlUnresolvedThinkSource"]?.InnerText == "True") == (resolved[i].jobGiver is null),
                    "Job " + row.Id + " carries the precise unresolved-source marker", () => actual[i].OuterXml);
                ctx.Report.Check(actual[i]["jobGiverThinkTree"]?.InnerText == row.Tree,
                    "Job " + row.Id + " retains its original tree name even when unavailable", () => actual[i].OuterXml);
            }
        }
    }

    private static void WriteCurrentLayoutSample(string path)
    {
        ThinkTreeDef tree = DefDatabase<ThinkTreeDef>.GetNamed("MugirlLike");
        // 真实 Job writer 写当前来源，同时保留失败升级后的 schema 0；不硬编码本机碰撞键。
        List<Job> jobs = [.. tree.thinkRoot.ThisAndChildrenRecursive.OfType<RimWorld.JobGiver_GetRest>()
            .Select((node, i) => new Job
            {
                def = RimWorld.JobDefOf.LayDown, loadID = i + 1, startTick = 1234 + i, expiryInterval = 321, count = 7,
                jobGiverThinkTree = tree, jobGiver = node,
            })];
        if (jobs.Count == 0) { throw new InvalidOperationException("No current MugirlLike rest sources were found."); }
        var meta = new XmlDocument { XmlResolver = null };
        meta.LoadXml("<meta><modIds><li>har.mugirlrace</li></modIds></meta>");
        Write(path, jobs, meta.DocumentElement!, schema: 0);
    }

    private static void Write(string path, List<Job> jobs, XmlElement meta, int schema = 1)
    {
        Scribe.saver.InitSaving(path, "savegame");
        Scribe.EnterNode("game");
        Scribe.EnterNode("jobs");
        for (int i = 0; i < jobs.Count; i++)
        {
            Job job = jobs[i];
            Scribe_Deep.Look(ref job, "job" + i.ToString(CultureInfo.InvariantCulture));
        }
        Scribe.ExitNode();
        Scribe.ExitNode();
        Scribe.saver.FinalizeSaving();
        var document = new XmlDocument { XmlResolver = null };
        document.Load(path);
        document.DocumentElement!.PrependChild(document.ImportNode(meta, true));
        // 这里只标记隔离的 Job 值夹具；schema 0 模拟失败后重存，schema 1 验证升级后的普通读取。
        XmlElement components = document.CreateElement("components");
        components.InnerXml = "<li Class=\"Mugirl.Core.GameComponent_Mugirl\"><mugirlSaveSchema>"
            + schema.ToString(CultureInfo.InvariantCulture) + "</mugirlSaveSchema></li>";
        document.DocumentElement["game"]!.PrependChild(components);
        document.Save(path);
    }

    private sealed class ReplayApi(Assembly assembly)
    {
        private const BindingFlags AllStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal List<Job> Read(string path, out string report)
        {
            Type sessionType = assembly.GetType("Mugirl.SaveUpgrade.SaveUpgradeSession", true)!;
            FieldInfo active = assembly.GetType("Mugirl.SaveUpgrade.SaveUpgradeRuntime", true)!.GetField("activeSession", AllStatic)!;
            object? previous = active.GetValue(null);
            try
            {
                Scribe.loader.InitLoading(path);
                object session = sessionType.GetMethod("Start", AllStatic)!.Invoke(null, [961, Scribe.loader.curXmlParent.OwnerDocument!, path])!;
                object tables = assembly.GetType("Mugirl.SaveUpgrade.UpgradeTables", true)!.GetProperty("Default", AllStatic)!.GetValue(null)!;
                object catalog = assembly.GetType("Mugirl.SaveUpgrade.DefDatabaseCatalog", true)!.GetProperty("Instance", AllStatic)!.GetValue(null)!;
                sessionType.GetMethod("RunDom", AllInstance)!.Invoke(session, [tables, catalog]);
                active.SetValue(null, session);
                List<Job> jobs = [];
                Scribe.EnterNode("game");
                Scribe.EnterNode("jobs");
                int count = Scribe.loader.curXmlParent.ChildNodes.OfType<XmlElement>().Count();
                for (int i = 0; i < count; i++)
                {
                    Job? job = null;
                    Scribe_Deep.Look(ref job, "job" + i.ToString(CultureInfo.InvariantCulture));
                    jobs.Add(job ?? throw new InvalidOperationException("A sampled Job was not loaded."));
                }
                Scribe.ExitNode();
                Scribe.ExitNode();
                // 复现 FinalizeLoading 的游标清理，再运行两段真实读取；不触发整个游戏的阶段 A 升级钩子。
                Scribe.loader.curXmlParent = null;
                Scribe.loader.curParent = null;
                Scribe.loader.curPathRelToParent = null;
                Scribe.mode = LoadSaveMode.Inactive;
                Scribe.loader.crossRefs.ResolveAllCrossReferences();
                Scribe.loader.initer.DoAllPostLoadInits();
                Scribe.ForceStop();
                Type phase = assembly.GetType("Mugirl.SaveUpgrade.UpgradePhase", true)!;
                foreach (string name in new[] { "A", "B" }) { sessionType.GetMethod("MarkPhase", AllInstance)!.Invoke(session, [Enum.Parse(phase, name)]); }
                sessionType.GetMethod("Complete", AllInstance)!.Invoke(session, null);
                object value = sessionType.GetProperty("Report", AllInstance)!.GetValue(session)!;
                report = (string)value.GetType().GetMethod("ToJson", AllInstance)!.Invoke(value, null)!;
                return jobs;
            }
            finally
            {
                Scribe.ForceStop();
                active.SetValue(null, previous);
            }
        }
    }
}
