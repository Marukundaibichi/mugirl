using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Xml;
using MugirlRace.Validation.Checks;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Metadata;
using MugirlRace.Validation.Reporting;
using MugirlRace.Validation.Runtime;

namespace MugirlRace.Validation;

/// <summary>
/// <c>--save-corpus &lt;dir&gt;</c>：对目录下每个真实存档（<c>*.rws</c> 与 gzip 的 <c>*.rws.gz</c>，只读）离线运行升级器的 DOM 流水线
/// （<c>Mugirl.SaveUpgrade.SaveUpgradeOffline.Run(XmlDocument, IDefCatalog)</c>，按名调用），Def 信息由内容模型提供（spec F14）。
/// 存档本身从不写回；有 <c>--artifacts</c> 时写每个存档的报告 JSON。
/// </summary>
/// <remarks>
/// 一个存档通过的条件（决定 §2）：报告的 state 是已知的非失败状态（NotLegacy、Upgraded、MooGirlEra、NoMugirl；Failed 与 Newer 失败），
/// 没有 failedStep；对升级结果再运行一次没有任何动作（步骤幂等）；第一次运行不超过 <see cref="BudgetMs"/> 毫秒。目录中没有存档也失败。
/// </remarks>
internal static class SaveCorpusRun
{
    public const string OfflineType = "Mugirl.SaveUpgrade.SaveUpgradeOffline";
    public const string CatalogType = "Mugirl.SaveUpgrade.IDefCatalog";

    /// <summary>每个存档的升级耗时上限（决定 §2：67 MB 的 S7 不超过 2 秒）。</summary>
    public const int BudgetMs = 2000;

    private static readonly string[] PassingStates = ["NotLegacy", "Upgraded", "MooGirlEra", "NoMugirl"];

    public static int Execute(ValidationOptions options, ModCatalog catalog)
    {
        string directory = options.SaveCorpusDir!;
        if (!Directory.Exists(directory))
        {
            throw new EnvironmentException("Save corpus folder not found: " + directory);
        }

        var report = new Report();
        GameRuntime runtime = GameRuntime.Load(options, catalog, report);
        ContentModel content = ContentModel.Build(catalog, runtime, options.GameDir);
        Type? offline = runtime.Mod.GetType(OfflineType, throwOnError: false);
        Type? catalogInterface = runtime.Mod.GetType(CatalogType, throwOnError: false);
        MethodInfo? run = offline?.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
        if (run is null || catalogInterface is null)
        {
            Console.WriteLine($"SAVE CORPUS FAILED: the build has no {OfflineType}.Run(XmlDocument, {CatalogType}) entry point.");
            return ExitCodes.Failed;
        }

        using var world = new MetadataWorld(options.ModAssembly, null, [options.GameDir, options.HarmonyDir]);
        object proxy = DefCatalogProxy.Create(catalogInterface, content, new HandleDefaults(world));
        List<string> files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".rws", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".rws.gz", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).ToList();
        if (files.Count == 0)
        {
            Console.WriteLine($"SAVE CORPUS FAILED: no *.rws or *.rws.gz below {directory}.");
            return ExitCodes.Failed;
        }

        int failed = 0;
        foreach (string file in files)
        {
            string name = Path.GetRelativePath(directory, file);
            try
            {
                XmlDocument document = Load(file);
                var timer = Stopwatch.StartNew();
                string first = (string?)run.Invoke(null, [document, proxy]) ?? "";
                timer.Stop();
                string second = (string?)run.Invoke(null, [document, proxy]) ?? "";
                (string state, string? failedStep, int steps, int actions) = Summary(first);
                (_, _, _, int repeated) = Summary(second);
                var problems = new List<string>();
                if (!PassingStates.Contains(state, StringComparer.Ordinal))
                {
                    problems.Add($"state {state}");
                }

                if (failedStep is not null)
                {
                    problems.Add($"failed at step {failedStep}");
                }

                if (repeated != 0)
                {
                    problems.Add($"not idempotent: a second run did {repeated} action(s)");
                }

                if (timer.ElapsedMilliseconds > BudgetMs)
                {
                    problems.Add($"{timer.ElapsedMilliseconds} ms exceeds the {BudgetMs} ms budget");
                }

                Console.WriteLine($"{name}: {new FileInfo(file).Length / 1048576.0:F1} MB, {timer.ElapsedMilliseconds} ms: state {state}, {steps} step(s), {actions} action(s)"
                    + (problems.Count == 0 ? "" : " — FAILED: " + string.Join("; ", problems)));
                if (problems.Count > 0)
                {
                    failed++;
                }

                if (options.ArtifactsDir is { } artifacts)
                {
                    Directory.CreateDirectory(artifacts);
                    File.WriteAllText(Path.Combine(artifacts, Path.GetFileName(file) + ".upgrade.json"), first);
                }
            }
            catch (Exception exception) when (exception is XmlException or TargetInvocationException or IOException or InvalidDataException)
            {
                failed++;
                Console.WriteLine($"{name}: FAILED: {(exception.InnerException ?? exception).Message}");
            }
        }

        Console.WriteLine(failed == 0 ? $"SAVE CORPUS PASSED: {files.Count} save(s) upgraded offline." : $"SAVE CORPUS FAILED: {failed} of {files.Count} save(s).");
        return failed == 0 ? ExitCodes.Passed : ExitCodes.Failed;
    }

    private static XmlDocument Load(string file)
    {
        var document = new XmlDocument();
        if (file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            using FileStream stream = File.OpenRead(file);
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            document.Load(gzip);
        }
        else
        {
            document.Load(file);
        }

        return document;
    }

    // 报告（契约 K2）：state、failedStep、steps[].actions。无法解析的报告按失败处理。
    private static (string State, string? FailedStep, int Steps, int Actions) Summary(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string state = root.TryGetProperty("state", out JsonElement s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "?" : "?";
            string? failedStep = root.TryGetProperty("failedStep", out JsonElement f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
            if (!root.TryGetProperty("steps", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
            {
                return (state, failedStep, 0, 0);
            }

            int actions = list.EnumerateArray().Sum(step => step.TryGetProperty("actions", out JsonElement a) && a.TryGetInt32(out int n) ? n : 0);
            return (state, failedStep, list.GetArrayLength(), actions);
        }
        catch (JsonException)
        {
            return ("(report is not JSON)", null, 0, 0);
        }
    }
}
