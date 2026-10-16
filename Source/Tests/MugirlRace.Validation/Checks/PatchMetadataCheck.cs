using System.Reflection;
using MugirlRace.Validation.Reporting;
using MugirlRace.Validation.Runtime;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// B. 补丁审计元数据（ADR-002，1.7 由 <c>[MugirlPatch]</c> 代替手写的 MugirlPatchCatalog）：每个 Harmony 补丁类都标注所属模块、
/// 失效风险与失效后的表现；没有补丁的类不带这个特性。目标方法与补丁种类由 <c>B.patch-targets</c> 从 Harmony 特性推出，不重复记录。
/// </summary>
internal static class PatchMetadataCheck
{
    private const string AttributeTypeName = "Mugirl.Core.MugirlPatchAttribute";

    public static void Run(Report report, GameRuntime runtime)
        => report.Run("B.patch-metadata", "Every Harmony patch class carries [MugirlPatch] module, risk and failure behaviour", recorder => Check(recorder, runtime));

    private static void Check(CheckRecorder recorder, GameRuntime runtime)
    {
        Type? attributeType = runtime.Mod.GetType(AttributeTypeName, throwOnError: false);
        if (attributeType is null)
        {
            recorder.Fail(AttributeTypeName, "The patch metadata attribute is missing from the assembly (ADR-002).");
            return;
        }

        IReadOnlyList<Type> patchClasses = PatchTargetCheck.PatchClasses(runtime);
        var patchSet = new HashSet<Type>(patchClasses);
        var byRisk = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var deferred = new List<string>();
        foreach (Type type in patchClasses)
        {
            string name = RuntimeNames.Type(type);
            object? metadata = type.GetCustomAttributes(attributeType, inherit: false).FirstOrDefault();
            if (metadata is null)
            {
                recorder.Fail(name, "Harmony patch class without [MugirlPatch(module, risk, failureBehavior)]: state the feature it belongs to, the risk and what players see when the patch is not applied.");
                continue;
            }

            object? moduleValue = attributeType.GetProperty("Module")?.GetValue(metadata);
            Type? moduleEnum = runtime.Mod.GetType("Mugirl.Core.PatchModule", throwOnError: false);
            if (moduleEnum is not null && (!moduleEnum.IsEnum || attributeType.GetProperty("Module")?.PropertyType != moduleEnum
                || moduleValue is null || !Enum.IsDefined(moduleEnum, moduleValue)))
            {
                recorder.Fail(name, "[MugirlPatch] module must be a defined PatchModule enum value.");
            }

            string module = Read(metadata, "Module");
            string failure = Read(metadata, "FailureBehavior");
            if (module.Trim().Length == 0)
            {
                recorder.Fail(name, "[MugirlPatch] module is empty.");
            }

            if (failure.Trim().Length == 0)
            {
                recorder.Fail(name, "[MugirlPatch] failureBehavior is empty: describe what players see when the patch is not applied.");
            }

            object? riskValue = attributeType.GetProperty("Risk")?.GetValue(metadata);
            if (riskValue is null || !riskValue.GetType().IsEnum || !Enum.IsDefined(riskValue.GetType(), riskValue))
            {
                recorder.Fail(name, "[MugirlPatch] risk must be a defined risk enum value.");
            }

            string risk = Read(metadata, "Risk");
            byRisk[risk] = byRisk.GetValueOrDefault(risk) + 1;
            if (attributeType.GetProperty("Deferred")?.GetValue(metadata) is true)
            {
                deferred.Add(name);
            }
        }

        foreach (Type type in runtime.ModTypes.Where(t => !patchSet.Contains(t) && t.IsDefined(attributeType, inherit: false)).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            recorder.Fail(RuntimeNames.Type(type), "[MugirlPatch] on a class that is not a Harmony patch class (stale metadata).");
        }

        foreach (string name in deferred)
        {
            recorder.Info(name, "Deferred: patched after the loading long event instead of in the Mod constructor.");
        }

        recorder.Summary = $"{patchClasses.Count} patch class(es); risk " + string.Join(", ", byRisk.Select(pair => $"{pair.Key} {pair.Value}"));
    }

    private static string Read(object metadata, string property)
        => metadata.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.GetValue(metadata)?.ToString() ?? "";
}
