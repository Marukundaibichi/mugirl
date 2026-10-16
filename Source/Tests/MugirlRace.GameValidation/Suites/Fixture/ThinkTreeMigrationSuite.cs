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
        string input = ctx.Options.SuiteOption(Name + ".source", "");
        bool expectFailure = ctx.Options.SuiteOption(Name + ".expectFailure", "false") == "true";
        byte[] hash = SHA256.HashData(File.ReadAllBytes(input));
        var source = new XmlDocument { XmlResolver = null };
        source.Load(input);
        XmlElement[] original = source.SelectNodes("//*[lastJobGiverKey]")!.Cast<XmlElement>().ToArray();
        if (!ctx.Report.Check(original.Length > 0, "supplied save has Job sources", () => input)) { yield break; }

        var sample = new XmlDocument { XmlResolver = null };
        sample.LoadXml("<savegame><game><jobs /></game></savegame>");
        sample.DocumentElement!.PrependChild(sample.ImportNode(source.DocumentElement!["meta"]!, true));
        XmlElement jobsNode = sample.DocumentElement["game"]!["jobs"]!;
        string[] fields = ["def", "loadID", "startTick", "expiryInterval", "checkOverrideOnExpire", "playerForced",
            "playerInterruptedForced", "count", "maxNumMeleeAttacks", "maxNumStaticAttacks", "locomotionUrgency",
            "haulMode", "canBash", "canBashFences", "forceSleep", "restUntilHealed", "ignoreForbidden",
            "followRadius", "jobGiverThinkTree", "lastJobGiverKey"];
        var expected = new List<(int Id, int Key, string Tree, bool NativeMissing)>();
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
            ThinkTreeDef tree = DefDatabase<ThinkTreeDef>.GetNamed(treeName);
            bool missing = !tree.TryGetThinkNodeWithSaveKey(key, out _);
            expected.Add((int.Parse(job["loadID"]!.InnerText, CultureInfo.InvariantCulture), key, treeName, missing));
        }
        string[] warnings = [.. expected.Where(item => item.NativeMissing).Select(item => "Could not find think node with key "
            + item.Key.ToString(CultureInfo.InvariantCulture)).Distinct(StringComparer.Ordinal)];
        // 只接受预先经原版查找证明缺失的确切键；生产警告仍然实际发出，并在下面按数量核对。
        ctx.ExpectLog(entry => entry.Level == LogLevel.Warning && warnings.Any(text => entry.Message.Trim() == text));
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
                yield break;
            }
            if (!ctx.Report.Check(failedStep is null && report.RootElement.GetProperty("state").GetString() == "Upgraded",
                "foreign missing sources do not fail the upgrade", () => firstReport)) { yield break; }
        }
        ctx.Report.Check(first.Count == expected.Count, "every sampled Job was read", () => $"expected {expected.Count}, actual {first.Count}");
        string savedPath = ctx.ArtifactPath("think-saved.xml")!;
        Write(savedPath, first, sample["savegame"]!["meta"]!);
        CheckKeys(ctx, savedPath, expected);
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
        CheckKeys(ctx, secondPath, expected);
        ctx.Report.Check(File.ReadAllText(savedPath) == File.ReadAllText(secondPath), "the repeated save is stable", () => "serialized Job data changed");
        LogCapture.DrainInto(ctx.Logs);
        int actualWarnings = ctx.Logs.Skip(logStart).Count(entry => entry.Level == LogLevel.Warning && warnings.Contains(entry.Message.Trim(), StringComparer.Ordinal));
        ctx.Report.Check(actualWarnings == expected.Count(item => item.NativeMissing) * 2, "native missing-key warnings remain visible", () => "actual " + actualWarnings);
        ctx.Report.Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input))), "original input is unchanged", () => input);
        File.WriteAllText(ctx.ArtifactPath("think-replay-summary.json")!, JsonSerializer.Serialize(new
        {
            sourceSha256 = Convert.ToHexString(hash),
            modSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
            jobs = expected.Count, nativeMissing = expected.Count(item => item.NativeMissing), actualWarnings,
            sources = expected.Select(item => new { item.Id, item.Tree, item.Key, item.NativeMissing }),
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CheckKeys(ValidationContext ctx, string path, List<(int Id, int Key, string Tree, bool NativeMissing)> expected)
    {
        var document = new XmlDocument { XmlResolver = null };
        document.Load(path);
        XmlElement[] actual = document.SelectNodes("/savegame/game/jobs/*")!.Cast<XmlElement>().ToArray();
        for (int i = 0; i < expected.Count; i++)
        {
            var row = expected[i];
            ctx.Report.Check(actual[i]["lastJobGiverKey"]?.InnerText == row.Key.ToString(CultureInfo.InvariantCulture)
                && actual[i].SelectNodes("lastJobGiverKey")!.Count == 1,
                "Job " + row.Id + " keeps exactly one original key", () => actual[i].OuterXml);
        }
    }

    private static void Write(string path, List<Job> jobs, XmlElement meta)
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
        // 这里只给隔离的 Job 值夹具标记当前结构，以验证下次普通读取；不是给玩家完整存档盖成功戳。
        XmlElement components = document.CreateElement("components");
        components.InnerXml = "<li Class=\"Mugirl.Core.GameComponent_Mugirl\"><mugirlSaveSchema>1</mugirlSaveSchema></li>";
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
