using System.Reflection;
using System.Reflection.Emit;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Reporting;
using MugirlRace.Validation.Runtime;
using HarmonyLib;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// B. 在目标方法真实的 1.7 IL 上运行每个 Transpiler（此时尚未打任何补丁），确认它找到了锚点：不写警告或错误、输出与输入不同、
/// 每个分支标签存在、异常块完整、栈平衡。它插入的钩子（对本 Mod 的调用及次数）必须等于 <c>Baselines/transpiler-hooks.txt</c>，
/// 游戏更新改变锚点匹配次数时能被发现。
/// </summary>
internal static class TranspilerCheck
{
    public const string GoldenFile = "transpiler-hooks.txt";

    public static void Run(Report report, ValidationOptions options, GameRuntime runtime, PatchPlan plan)
        => report.Run("B.transpilers", "Transpilers find their anchors on the 1.7 IL", recorder => Check(recorder, options, runtime, plan));

    private static void Check(CheckRecorder recorder, ValidationOptions options, GameRuntime runtime, PatchPlan plan)
    {
        if (!plan.Resolved)
        {
            recorder.Skip("patch targets were not resolved");
            return;
        }

        var lines = new List<string>();
        List<PlannedPatch> transpilers = plan.Patches.Where(p => p.Role == "Transpiler").ToList();
        foreach (PlannedPatch patch in transpilers)
        {
            string subject = $"{RuntimeNames.Type(patch.PatchClass)} on {RuntimeNames.Method(patch.Target)}";
            try
            {
                CheckOne(recorder, runtime, patch, subject, lines);
            }
            catch (Exception exception) when (exception is TargetInvocationException or InvalidCastException or ArgumentException or NotSupportedException)
            {
                Exception cause = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
                recorder.Fail(subject, "The transpiler threw: " + cause.GetType().Name + ": " + cause.Message);
            }
        }

        GoldenList.Compare(options.BaselineDir, GoldenFile, lines, options.UpdateBaselines, recorder,
            "Calls into MugirlRace that each Transpiler inserts into its 1.7 target: PatchClass<TAB>Target<TAB>hook ×count.\n"
            + "Regenerate with --update-baselines after reviewing the transpiler against the new IL.");
        recorder.Summary = $"{transpilers.Count} transpiler(s) checked";
    }

    private static void CheckOne(CheckRecorder recorder, GameRuntime runtime, PlannedPatch patch, string subject, List<string> lines)
    {
        List<CodeInstruction> input = PatchProcessor.GetOriginalInstructions(patch.Target, out ILGenerator generator);
        // 复制构造函数保留 labels 与 blocks；Harmony 的 Clone() 会清空二者，用它做快照时异常块比较恒不相等。
        List<CodeInstruction> original = input.Select(instruction => new CodeInstruction(instruction)).ToList();
        object?[] arguments = patch.PatchMethod.GetParameters().Select(parameter =>
            parameter.ParameterType == typeof(ILGenerator) ? generator
            : typeof(MethodBase).IsAssignableFrom(parameter.ParameterType) ? patch.Target
            : parameter.ParameterType.IsAssignableFrom(typeof(List<CodeInstruction>)) ? input
            : (object?)null).ToArray();

        int logBefore = LogCapture.Entries.Count;
        var output = ((IEnumerable<CodeInstruction>)patch.PatchMethod.Invoke(null, arguments)!).ToList();
        foreach (LogEntry entry in LogCapture.Entries.Skip(logBefore).Where(e => e.Level != LogLevel.Message))
        {
            recorder.Fail(subject, $"Logged a {entry.Level.ToString().ToLowerInvariant()} while transpiling (anchor not found?): {FirstLine(entry.Text)}");
        }

        if (output.Count == original.Count && output.Zip(original).All(pair => pair.First.opcode == pair.Second.opcode && Equals(pair.First.operand, pair.Second.operand)))
        {
            recorder.Fail(subject, "The output equals the original IL: the transpiler changed nothing (its anchor no longer matches).");
        }

        if (original.Sum(i => i.blocks.Count) != output.Sum(i => i.blocks.Count))
        {
            recorder.Fail(subject, "Exception block markers were lost or added.");
        }

        if (IlStackValidator.Validate(output, patch.Target) is { } problem)
        {
            recorder.Fail(subject, "Invalid IL: " + problem);
        }

        var hooks = output.Where(i => i.operand is MethodBase m && m.DeclaringType?.Assembly == runtime.Mod)
            .GroupBy(i => RuntimeNames.Method((MethodBase)i.operand))
            .Select(g => $"{RuntimeNames.Type(patch.PatchClass)}\t{RuntimeNames.Method(patch.Target)}\t{g.Key} ×{g.Count()}")
            .ToList();
        if (hooks.Count == 0)
        {
            lines.Add($"{RuntimeNames.Type(patch.PatchClass)}\t{RuntimeNames.Method(patch.Target)}\t(no call into MugirlRace)");
        }

        lines.AddRange(hooks);
    }

    private static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();
}
