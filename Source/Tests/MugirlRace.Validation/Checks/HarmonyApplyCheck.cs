using System.Collections;
using System.Reflection;
using MugirlRace.Validation.Reporting;
using MugirlRace.Validation.Runtime;
using HarmonyLib;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// B. 用 Mod 真实的 Harmony id（<c>MugirlMod.Mod</c>）对真实的 1.7 Assembly-CSharp 运行正式入口
/// <c>Mugirl.Core.HarmonyBootstrap.Apply(Harmony)</c>（同步应用全部补丁类，含游戏内延迟注册的类）：Harmony 3 生成每个替换方法，
/// 任何补丁类都不能失败，实际应用的补丁必须与 <c>B.patch-targets</c> 解析的完全一致。最后 <c>UnpatchAll</c>，并确认该 id 不再留下补丁。
/// </summary>
/// <remarks>
/// <c>Apply</c> 返回 <c>IReadOnlyList&lt;HarmonyBootstrap.PatchFailure&gt;</c>；失败项按属性名 <c>PatchClass</c> 与 <c>Exception</c> 读取。
/// </remarks>
internal static class HarmonyApplyCheck
{
    private const string BootstrapType = "Mugirl.Core.HarmonyBootstrap";

    public static void Run(Report report, Infrastructure.ValidationOptions options, GameRuntime runtime, PatchPlan plan)
    {
        var order = new List<PatchOrderCheck.Applied>();
        report.Run("B.harmony-apply", "HarmonyBootstrap.Apply patches the 1.7 game with zero failures, then UnpatchAll", recorder => Check(recorder, runtime, plan, order));
        PatchOrderCheck.Run(report, options, order);
    }

    private static void Check(CheckRecorder recorder, GameRuntime runtime, PatchPlan plan, List<PatchOrderCheck.Applied> order)
    {
        Type? bootstrap = runtime.Mod.GetType(BootstrapType, throwOnError: false);
        MethodInfo? apply = bootstrap?.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static, [typeof(Harmony)]);
        FieldInfo? idField = bootstrap?.GetField("HarmonyId", BindingFlags.Public | BindingFlags.Static);
        string? id = (idField is { IsLiteral: true } ? idField.GetRawConstantValue() : idField?.GetValue(null)) as string;
        if (apply is null || id is null)
        {
            recorder.Fail(BootstrapType, "The production entry point HarmonyBootstrap.Apply(Harmony) / HarmonyBootstrap.HarmonyId is missing.");
            return;
        }

        if (PatchedBy(id).Any())
        {
            recorder.Fail(id, "Methods were already patched with the mod's Harmony id before Apply ran (a static constructor applied patches early?).");
        }

        var harmony = new Harmony(id);
        try
        {
            object? failures = apply.Invoke(null, [harmony]);
            foreach (object failure in failures as IEnumerable ?? Array.Empty<object>())
            {
                Type? patchClass = failure.GetType().GetProperty("PatchClass")?.GetValue(failure) as Type;
                Exception? exception = failure.GetType().GetProperty("Exception")?.GetValue(failure) as Exception;
                Exception? cause = exception?.InnerException ?? exception;
                recorder.Fail(patchClass is null ? "(patch class)" : RuntimeNames.Type(patchClass),
                    "HarmonyBootstrap could not apply this patch class: " + (cause is null ? "unknown error" : cause.GetType().Name + ": " + cause.Message));
            }

            var actual = new SortedSet<string>(StringComparer.Ordinal);
            int methods = 0;
            foreach (MethodBase original in PatchedBy(id))
            {
                methods++;
                Patches info = Harmony.GetPatchInfo(original)!;
                foreach ((string role, IEnumerable<Patch> patches) in Kinds(info))
                {
                    foreach (Patch patch in patches.Where(p => p.owner == id))
                    {
                        actual.Add(PlannedPatch.KeyOf(patch.PatchMethod.DeclaringType!, role, patch.PatchMethod.Name, original));
                        order.Add(new PatchOrderCheck.Applied(RuntimeNames.Method(original), role, RuntimeNames.Type(patch.PatchMethod.DeclaringType!) + "::" + patch.PatchMethod.Name,
                            patch.priority, patch.index));
                    }
                }
            }

            if (plan.Resolved)
            {
                var expected = new SortedSet<string>(plan.Patches.Select(p => p.Key), StringComparer.Ordinal);
                foreach (string missing in expected.Except(actual))
                {
                    recorder.Fail("not applied: " + missing.Replace('\t', ' '), "Resolved by B.patch-targets but not applied by HarmonyBootstrap.Apply.");
                }

                foreach (string extra in actual.Except(expected))
                {
                    recorder.Fail("unexpected: " + extra.Replace('\t', ' '), "Applied by Harmony but not predicted by B.patch-targets (the resolver and Harmony disagree).");
                }
            }

            recorder.Summary = $"Harmony {typeof(Harmony).Assembly.GetName().Version} generated {methods} replacement methods ({actual.Count} patches)";
        }
        catch (TargetInvocationException exception)
        {
            recorder.Fail(BootstrapType + ".Apply", "Threw: " + (exception.InnerException ?? exception));
        }
        finally
        {
            harmony.UnpatchAll(id);
            List<MethodBase> residual = PatchedBy(id).ToList();
            foreach (MethodBase method in residual)
            {
                recorder.Fail("residual: " + RuntimeNames.Method(method), "Still patched after UnpatchAll.");
            }
        }
    }

    private static IEnumerable<MethodBase> PatchedBy(string id)
        => Harmony.GetAllPatchedMethods().Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(id) == true).ToList();

    private static IEnumerable<(string Role, IEnumerable<Patch> Patches)> Kinds(Patches info) =>
    [
        ("Prefix", info.Prefixes), ("Postfix", info.Postfixes), ("Transpiler", info.Transpilers), ("Finalizer", info.Finalizers),
        ("InnerPrefix", info.InnerPrefixes), ("InnerPostfix", info.InnerPostfixes), ("InnerFinalizer", info.InnerFinalizers),
    ];
}
