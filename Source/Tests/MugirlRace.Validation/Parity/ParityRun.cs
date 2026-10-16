using System.Text.Encodings.Web;
using System.Text.Json;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Legacy;
using MugirlRace.Validation.Metadata;
using Mono.Cecil;

namespace MugirlRace.Validation.Parity;

/// <summary>
/// <c>--parity A B</c>：Wave R 的行为保持证明（spec/critique-validation.md 提案 (a) §2.3–2.4）。A 是改名前的构建，B 是改名后的构建，
/// <c>--map</c> 是实际应用的改名映射；<c>--content-a/--content-b</c> 给出两侧的 Mod 根目录时同时比较内容。没有未解释的差异时退出码 0。
/// </summary>
/// <remarks>
/// 校准：同一份源码在两个目录各构建一次（A 对 A′、空映射）必须零差异，证明比较本身没有噪声。<c>--parity-accept</c> 中列出的
/// （类别、主题）差异带理由接受；<c>--expect</c> 给负对照：其中每一行都必须匹配至少一个差异（<c>类别|主题子串|说明子串</c>），<c>absent:</c> 行要求没有差异匹配。
/// </remarks>
internal static class ParityRun
{
    public static int Execute(ValidationOptions options)
    {
        (string pathA, string pathB) = options.Parity!.Value;
        foreach (string path in new[] { pathA, pathB })
        {
            if (!File.Exists(path))
            {
                throw new EnvironmentException("Parity input not found: " + path);
            }
        }

        RenameMap map = RenameMap.Load(options.ParityMap);
        Console.WriteLine($"IL parity: A = {pathA}");
        Console.WriteLine($"           B = {pathB}");
        Console.WriteLine($"           map = {options.ParityMap ?? "(none: calibration)"} ({map.Count} entries)");
        IEnumerable<string> search = new[] { options.GameDir, options.HarmonyDir, Path.GetDirectoryName(typeof(object).Assembly.Location)!, Path.GetDirectoryName(pathA)!, Path.GetDirectoryName(pathB)! }
            .Concat(new[] { "839005762/1.7/Assemblies", "1635901197/1.6/Assemblies", "2023507013/1.7/Assemblies" }
                .Select(relative => Path.Combine(options.WorkshopDir, relative)).Where(Directory.Exists));
        using var resolver = new GameCecilResolver(search);
        using ModuleDefinition a = Read(pathA, resolver);
        using ModuleDefinition b = Read(pathB, resolver);
        map.BindAccessors(a, b);
        var il = new IlParity(a, b, map);
        il.Run();
        var diffs = new List<ParityDiff>(il.Diffs);
        if (options.ContentA is { } contentA && options.ContentB is { } contentB)
        {
            using TypeUniverse universe = TypeUniverse.Create("1.7", Path.Combine(options.GameDir, "Assembly-CSharp.dll"),
                search.Where(path => path != Path.GetDirectoryName(pathA) && path != Path.GetDirectoryName(pathB) && path != Path.GetDirectoryName(typeof(object).Assembly.Location)).ToList(), redirectUnity: true);
            diffs.AddRange(ContentParity.Compare(contentA, contentB, map, universe, a, b));
        }

        List<(string Category, string Subject, string Reason)> accepted = ReadAccept(options.ParityAccept);
        var used = new HashSet<int>();
        List<ParityDiff> unexplained = diffs.Where(diff =>
        {
            int index = accepted.FindIndex(entry => entry.Category == diff.Category && entry.Subject == diff.Subject);
            if (index < 0)
            {
                return true;
            }

            used.Add(index);
            return false;
        }).ToList();

        Console.WriteLine($"{il.MatchedTypes} type(s) matched, {il.IdenticalBodies} identical method bodies; {diffs.Count} difference(s), {diffs.Count - unexplained.Count} accepted.");
        foreach (ParityDiff diff in unexplained)
        {
            Console.WriteLine("  DIFF " + diff);
        }

        foreach ((int index, (string category, string subject, _)) in accepted.Select((entry, i) => (i, entry)).Where(pair => !used.Contains(pair.i)))
        {
            Console.WriteLine($"  STALE accept entry: {category} {subject} (no such difference any more)");
        }

        Console.WriteLine($"{il.NameSensitiveSites.Count} name-sensitive site(s) in B (read Type.Name/FullName or MemberInfo.Name; sign off when a renamed type reaches them).");
        if (options.ReportPath is { } report)
        {
            WriteReport(report, il, diffs, unexplained);
        }

        if (options.Expect is { } expectFile)
        {
            return CheckExpectations(expectFile, diffs);
        }

        Console.WriteLine(unexplained.Count == 0 ? "PARITY PASSED: no unexplained difference." : $"PARITY FAILED: {unexplained.Count} unexplained difference(s).");
        return unexplained.Count == 0 ? ExitCodes.Passed : ExitCodes.Failed;
    }

    private static ModuleDefinition Read(string path, IAssemblyResolver resolver)
        => ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver, InMemory = true, ReadingMode = ReadingMode.Immediate, ReadSymbols = File.Exists(Path.ChangeExtension(path, ".pdb")) });

    private static List<(string, string, string)> ReadAccept(string? path)
    {
        var entries = new List<(string, string, string)>();
        if (path is null || !File.Exists(path))
        {
            return entries;
        }

        foreach (string line in File.ReadLines(path).Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0 && !l.StartsWith('#')))
        {
            string[] parts = line.Split('\t');
            if (parts.Length < 3 || parts[2].Trim().Length == 0)
            {
                throw new UsageException($"{path}: '{line}' needs category<TAB>subject<TAB>reason.");
            }

            entries.Add((parts[0], parts[1], parts[2]));
        }

        return entries;
    }

    // 负对照：每一行 "类别|主题子串[|说明子串]" 都必须匹配至少一个差异；"absent: " 开头的行（正对照）要求没有任何差异匹配。
    private static int CheckExpectations(string path, List<ParityDiff> diffs)
    {
        int problems = 0;
        int rules = 0;
        foreach (string line in File.ReadLines(path).Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#')))
        {
            rules++;
            string rule = line.Split('\t')[0];
            bool absent = rule.StartsWith("absent:", StringComparison.Ordinal);
            string[] parts = (absent ? rule["absent:".Length..] : rule).Split('|').Select(p => p.Trim()).ToArray();
            List<ParityDiff> hits = diffs.Where(diff => diff.Category == parts[0]
                && (parts.Length < 2 || diff.Subject.Contains(parts[1], StringComparison.Ordinal))
                && (parts.Length < 3 || diff.Detail.Contains(parts[2], StringComparison.Ordinal))).ToList();
            if (absent && hits.Count > 0)
            {
                problems++;
                Console.WriteLine("PARITY CONTROL fired although it must not: " + line);
                hits.ForEach(hit => Console.WriteLine("    " + hit));
            }
            else if (!absent && hits.Count == 0)
            {
                problems++;
                Console.WriteLine("PARITY CONTROL did not fire: " + line);
            }
        }

        Console.WriteLine(problems == 0 ? $"PARITY CONTROLS PASSED: all {rules} expectation(s) met." : $"PARITY CONTROLS FAILED: {problems} of {rules} expectation(s) not met.");
        return problems == 0 ? ExitCodes.Passed : ExitCodes.Failed;
    }

    private static void WriteReport(string path, IlParity il, List<ParityDiff> diffs, List<ParityDiff> unexplained)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        writer.WriteStartObject();
        writer.WriteString("result", unexplained.Count == 0 ? "pass" : "fail");
        writer.WriteNumber("matchedTypes", il.MatchedTypes);
        writer.WriteNumber("identicalBodies", il.IdenticalBodies);
        writer.WriteStartArray("differences");
        foreach (ParityDiff diff in diffs)
        {
            writer.WriteStartObject();
            writer.WriteString("category", diff.Category);
            writer.WriteString("subject", diff.Subject);
            writer.WriteString("detail", diff.Detail);
            writer.WriteBoolean("accepted", !unexplained.Contains(diff));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("nameSensitiveSites");
        foreach (string site in il.NameSensitiveSites.Distinct(StringComparer.Ordinal))
        {
            writer.WriteStringValue(site);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
