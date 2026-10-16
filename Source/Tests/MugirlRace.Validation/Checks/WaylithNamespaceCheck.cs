using MugirlRace.Validation.Metadata;
using MugirlRace.Validation.Reporting;
using Mono.Cecil;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// B. 原生引擎访问集中在一处（ADR-006 规则 3）：只有 <c>Mugirl.Platform</c>（及其子命名空间）中的类型可以引用 Waylith
/// （<c>Waylith.Time</c>、<c>Waylith.Input</c>、渲染等），其余代码都经由 Platform 包装，引擎 API 变化时只改一个目录。
/// </summary>
internal static class WaylithNamespaceCheck
{
    private const string PlatformNamespace = "Mugirl.Platform";

    public static void Run(Report report, MetadataWorld world)
        => report.Run("B.waylith-namespace", "Waylith is referenced only from Mugirl.Platform", recorder => Check(recorder, world.Mod));

    private static void Check(CheckRecorder recorder, ModuleDefinition module)
    {
        var violations = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        int platformTypes = 0;
        foreach (Usage usage in UsageScanner.Scan(module))
        {
            TypeReference referencedType = usage.Referenced as TypeReference ?? usage.Referenced.DeclaringType;
            if (!CecilNames.AssemblyOf(referencedType).StartsWith("Waylith", StringComparison.Ordinal))
            {
                continue;
            }

            string ns = CecilNames.NamespaceOf(usage.Container);
            if (ns == PlatformNamespace || ns.StartsWith(PlatformNamespace + ".", StringComparison.Ordinal))
            {
                platformTypes++;
                continue;
            }

            string container = CecilNames.Type(CecilNames.UserType(usage.Container));
            if (!violations.TryGetValue(container, out SortedSet<string>? referenced))
            {
                violations[container] = referenced = new SortedSet<string>(StringComparer.Ordinal);
            }

            referenced.Add(usage.Referenced is TypeReference type ? CecilNames.Type(type) : CecilNames.MemberShort(usage.Referenced));
        }

        foreach ((string container, SortedSet<string> referenced) in violations)
        {
            recorder.Fail(container, $"References the Waylith engine outside {PlatformNamespace}: {string.Join(", ", referenced)}. "
                + "Use (or add) a wrapper in Source/MugirlRace/Platform.");
        }

        recorder.Summary = $"{platformTypes} Waylith references inside {PlatformNamespace}";
    }
}
