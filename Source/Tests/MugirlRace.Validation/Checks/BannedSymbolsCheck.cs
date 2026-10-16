using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Metadata;
using MugirlRace.Validation.Reporting;
using Mono.Cecil;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// B. 对照构建结果与真实的 1.7 程序集核对 <c>Source/MugirlRace/BannedSymbols.txt</c>（ADR-006 的编译期执行手段）：
/// <list type="number">
/// <item>每个条目都指向存在的符号（分析器静默忽略解析不到的条目，拼错就什么都没禁）；</item>
/// <item>IL 不引用任何被禁符号，除了 <c>Baselines/banned-symbol-exemptions.txt</c> 列出的调用点（审阅过的
/// <c>#pragma warning disable RS0030</c> 位置；分析器可以被抑制，这项检查不能）；</item>
/// <item>生成的“UnityStubs math surface”一节覆盖边界数学类型的全部公开方法与属性，IDE 报出的就是构建后白名单会拒绝的。</item>
/// </list>
/// </summary>
internal static class BannedSymbolsCheck
{
    private const string ExemptionsFile = "banned-symbol-exemptions.txt";

    // BannedSymbols.txt 禁用其全部方法与属性的边界值类型（构造函数与字段仍允许）。
    private static readonly string[] MathSurfaceTypes =
    [
        "UnityEngine.Vector2", "UnityEngine.Vector3", "UnityEngine.Vector4", "UnityEngine.Quaternion",
        "UnityEngine.Matrix4x4", "UnityEngine.Color", "UnityEngine.Color32",
    ];

    private static readonly string[] SymbolAssemblies = ["UnityStubs", "Waylith", "Assembly-CSharp", "System.Private.CoreLib"];

    // ADR-006：只有平台层、数学层与唯一的日志入口可以带理由抑制 RS0030。
    private static readonly string[] SuppressibleOwners = ["Mugirl.Platform.", "Mugirl.Mathematics.", "Mugirl.Core.MugirlLog"];

    public static void Run(Report report, ValidationOptions options, MetadataWorld world)
        => report.Run("B.banned-symbols", "BannedSymbols.txt resolves, covers the Unity math surface and is not bypassed", recorder => Check(recorder, options, world));

    private static void Check(CheckRecorder recorder, ValidationOptions options, MetadataWorld world)
    {
        string bannedFile = Path.Combine(options.SourceDir, "BannedSymbols.txt");
        if (!File.Exists(bannedFile))
        {
            recorder.Fail("BannedSymbols.txt", $"Not found at {bannedFile}.");
            return;
        }

        Dictionary<string, TypeDefinition> types = IndexTypes(world, recorder);
        var memberIds = new Dictionary<TypeDefinition, HashSet<string>>();
        var bannedTypes = new HashSet<string>(StringComparer.Ordinal);
        var bannedMembers = new Dictionary<string, string>(StringComparer.Ordinal);
        int entries = 0;
        foreach (string id in ReadEntries(bannedFile))
        {
            entries++;
            if (!Resolve(id, types, memberIds, out TypeDefinition? owner))
            {
                recorder.Fail(id, "BannedSymbols.txt entry matches nothing in the 1.7 assemblies; the analyzer ignores it, so it bans nothing.");
                continue;
            }

            switch (id[0])
            {
                case 'T':
                    bannedTypes.Add(id[2..]);
                    break;
                case 'P':
                    foreach (MethodDefinition accessor in AccessorsOf(owner!, id))
                    {
                        bannedMembers.TryAdd(DocIds.ForMethod(accessor), id);
                    }

                    break;
                default:
                    bannedMembers.TryAdd(id, id);
                    break;
            }
        }

        CheckCoverage(recorder, types, bannedTypes, bannedMembers);
        AllowList exemptions = AllowList.Load(options.BaselineDir, ExemptionsFile);
        exemptions.AddsMissingForReview = options.UpdateBaselines;
        var reported = new HashSet<string>(StringComparer.Ordinal);
        int exempted = 0;
        foreach (Usage usage in UsageScanner.Scan(world.Mod))
        {
            string? banned = BannedEntryFor(usage.Referenced, bannedTypes, bannedMembers);
            if (banned is null)
            {
                continue;
            }

            string where = usage.Method is null ? CecilNames.Type(usage.Container) : CecilNames.Type(usage.Container) + "::" + usage.Method.Name;
            string key = banned + "\t" + where;
            if (!reported.Add(key))
            {
                continue;
            }

            bool suppressible = MaySuppress(where);
            if (suppressible ? exemptions.AllowsOrReview(key, "copy the reason the #pragma warning disable RS0030 states") : exemptions.Allows(key))
            {
                exempted++;
                if (!suppressible)
                {
                    recorder.Fail(banned + " @ " + where, $"Listed in Baselines/{ExemptionsFile}, but ADR-006 allows RS0030 suppressions only in {string.Join(", ", SuppressibleOwners)}.", where);
                }

                continue;
            }

            recorder.Fail(banned + " @ " + where, "Banned symbol used (the analyzer was suppressed or bypassed). Replace it"
                + (suppressible ? $", or list the call site in Baselines/{ExemptionsFile} with the reason the #pragma states." : $"; suppressions are allowed only in {string.Join(", ", SuppressibleOwners)} (ADR-006)."), where);
        }

        int added = exemptions.WritePendingReview();
        if (added > 0)
        {
            recorder.Info("Baselines/" + ExemptionsFile, $"{added} suppression site(s) in the allowed namespaces appended with a \"{AllowList.ReviewPrefix}\" reason (--update-baselines).");
        }

        exemptions.ReportProblems(recorder);
        recorder.Summary = $"{entries} entries; {exempted} reviewed exemption(s) used";
    }

    private static bool MaySuppress(string where) => SuppressibleOwners.Any(prefix => where.StartsWith(prefix, StringComparison.Ordinal));

    private static void CheckCoverage(CheckRecorder recorder, Dictionary<string, TypeDefinition> types, HashSet<string> bannedTypes, Dictionary<string, string> bannedMembers)
    {
        foreach (string typeName in MathSurfaceTypes)
        {
            if (!types.TryGetValue(typeName, out TypeDefinition? type) || bannedTypes.Contains(typeName))
            {
                continue;
            }

            foreach (MethodDefinition method in type.Methods.Where(m => m.IsPublic && !m.IsConstructor))
            {
                string id = DocIds.ForMethod(method);
                if (!bannedMembers.ContainsKey(id))
                {
                    recorder.Fail("not banned: " + id, "Public UnityStubs math member missing from BannedSymbols.txt (regenerate the UnityStubs math surface section).");
                }
            }
        }
    }

    private static string? BannedEntryFor(MemberReference referenced, HashSet<string> bannedTypes, Dictionary<string, string> bannedMembers)
    {
        TypeReference owner = referenced as TypeReference ?? referenced.DeclaringType;
        for (TypeReference? current = owner is GenericInstanceType generic ? generic.ElementType : owner; current is not null; current = current.DeclaringType)
        {
            if (bannedTypes.Contains(DocIds.DeclarationName(current)))
            {
                return "T:" + DocIds.DeclarationName(current);
            }
        }

        string? id = referenced switch
        {
            MethodReference method => DocIds.ForMethod(method),
            FieldReference field => DocIds.ForField(field),
            _ => null,
        };
        return id is not null && bannedMembers.TryGetValue(id, out string? entry) ? entry : null;
    }

    private static IEnumerable<string> ReadEntries(string file)
    {
        foreach (string raw in File.ReadLines(file))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int semicolon = line.IndexOf(';', StringComparison.Ordinal);
            yield return (semicolon < 0 ? line : line[..semicolon]).Trim();
        }
    }

    private static Dictionary<string, TypeDefinition> IndexTypes(MetadataWorld world, CheckRecorder recorder)
    {
        var types = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
        foreach (string name in SymbolAssemblies)
        {
            AssemblyDefinition? assembly = world.TryResolve(name);
            if (assembly is null)
            {
                recorder.Fail(name, "Cannot read this assembly; BannedSymbols.txt entries for it cannot be verified.");
                continue;
            }

            foreach (TypeDefinition type in CecilNames.AllTypes(assembly.MainModule))
            {
                types.TryAdd(DocIds.DeclarationName(type), type);
            }
        }

        return types;
    }

    private static bool Resolve(string id, Dictionary<string, TypeDefinition> types, Dictionary<TypeDefinition, HashSet<string>> memberIds, out TypeDefinition? owner)
    {
        owner = null;
        if (id.Length < 3 || id[1] != ':')
        {
            return false;
        }

        if (id[0] == 'N')
        {
            string ns = id[2..];
            return types.Values.Any(type => type.Namespace == ns || type.Namespace.StartsWith(ns + ".", StringComparison.Ordinal));
        }

        if (id[0] == 'T')
        {
            return types.TryGetValue(id[2..], out owner);
        }

        string body = id[2..];
        int parenthesis = body.IndexOf('(', StringComparison.Ordinal);
        string path = parenthesis >= 0 ? body[..parenthesis] : body;
        int tilde = path.IndexOf('~', StringComparison.Ordinal);
        if (tilde >= 0)
        {
            path = path[..tilde];
        }

        int dot = path.LastIndexOf('.');
        if (dot <= 0 || !types.TryGetValue(path[..dot], out owner))
        {
            return false;
        }

        if (!memberIds.TryGetValue(owner, out HashSet<string>? ids))
        {
            ids = new HashSet<string>(StringComparer.Ordinal);
            ids.UnionWith(owner.Methods.Select(DocIds.ForMethod));
            ids.UnionWith(owner.Properties.Select(DocIds.ForProperty));
            ids.UnionWith(owner.Fields.Select(DocIds.ForField));
            ids.UnionWith(owner.Events.Select(DocIds.ForEvent));
            memberIds[owner] = ids;
        }

        return ids.Contains(id);
    }

    private static IEnumerable<MethodDefinition> AccessorsOf(TypeDefinition owner, string propertyId)
    {
        PropertyDefinition? property = owner.Properties.FirstOrDefault(p => DocIds.ForProperty(p) == propertyId);
        if (property?.GetMethod is { } getter)
        {
            yield return getter;
        }

        if (property?.SetMethod is { } setter)
        {
            yield return setter;
        }
    }
}
