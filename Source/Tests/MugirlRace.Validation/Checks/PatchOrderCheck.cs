using System.Globalization;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Reporting;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// B. 同一目标上同种 Mugirl 补丁的先后：Harmony 按优先级、再按注册顺序排序，而 <c>HarmonyBefore</c>/<c>HarmonyAfter</c> 只对其他 Harmony id
/// 生效，Mugirl 自己的补丁之间只能靠不同的 <c>[HarmonyPriority]</c> 定序；注册顺序取决于补丁类的完整名，改名就会改变先后。
/// 规则：每组（目标、种类）中优先级两两不同。黄金文件 <c>Baselines/patch-order.txt</c> 记录这些组的实际顺序（取自 Apply 之后的
/// <c>Harmony.GetPatchInfo</c>），重命名前后必须相同。
/// </summary>
internal static class PatchOrderCheck
{
    public const string GoldenFile = "patch-order.txt";

    public static void Run(Report report, ValidationOptions options, IReadOnlyList<Applied> applied)
        => report.Run("B.patch-order", "Same-kind Mugirl patches on one target have distinct priorities; the effective order matches the golden list",
            recorder => Check(recorder, options, applied));

    /// <summary>每组的实际顺序行：<c>Target&lt;TAB&gt;Kind&lt;TAB&gt;位置&lt;TAB&gt;PatchClass::Method</c>（组内至少两个补丁）。</summary>
    public static List<string> OrderLines(IReadOnlyList<Applied> applied)
    {
        var lines = new List<string>();
        foreach (IGrouping<(string Target, string Kind), Applied> group in applied.GroupBy(a => (a.Target, a.Kind)).Where(g => g.Count() > 1))
        {
            int position = 0;
            foreach (Applied patch in group.OrderByDescending(a => a.Priority).ThenBy(a => a.Index))
            {
                lines.Add($"{group.Key.Target}\t{group.Key.Kind}\t{(++position).ToString("D2", CultureInfo.InvariantCulture)}\t{patch.Patch}");
            }
        }

        return lines;
    }

    private static void Check(CheckRecorder recorder, ValidationOptions options, IReadOnlyList<Applied> applied)
    {
        if (applied.Count == 0)
        {
            recorder.Skip("B.harmony-apply did not apply any patch");
            return;
        }

        int groups = 0;
        foreach (IGrouping<(string Target, string Kind), Applied> group in applied.GroupBy(a => (a.Target, a.Kind)).Where(g => g.Count() > 1))
        {
            groups++;
            foreach (IGrouping<int, Applied> tie in group.GroupBy(a => a.Priority).Where(g => g.Count() > 1))
            {
                recorder.Fail($"{group.Key.Target} {group.Key.Kind} priority {tie.Key}", $"{tie.Count()} Mugirl {group.Key.Kind} patches share priority {tie.Key} "
                    + $"({string.Join(", ", tie.Select(a => a.Patch))}); their order follows the patch class names. Give each a distinct [HarmonyPriority].");
            }
        }

        List<string> lines = OrderLines(applied);
        if (options.ArtifactsDir is { } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, GoldenFile), string.Join("\n", lines.Order(StringComparer.Ordinal)) + "\n");
        }

        GoldenList.Compare(options.BaselineDir, GoldenFile, lines, options.UpdateBaselines, recorder,
            "Effective order of same-kind Mugirl patches on one target (Harmony: priority, then registration index), from Harmony.GetPatchInfo after Apply.\n"
            + "Target<TAB>Kind<TAB>Position<TAB>PatchClass::Method. A rename must not change it; regenerate with --update-baselines only for a deliberate change.");
        recorder.Summary = $"{groups} target/kind group(s) with several Mugirl patches";
    }

    /// <summary>Apply 之后一个 Mugirl 补丁的位置信息。</summary>
    public sealed record Applied(string Target, string Kind, string Patch, int Priority, int Index);
}
