using System.Globalization;
using System.Xml;
using Mugirl.Features.Lances;

namespace Mugirl.SaveUpgrade;

/// <summary>已起飞的任务原样保留；旧两段步行流程切换到升级层的等布局续接驱动，不重放当前 Toil 初始化。</summary>
internal sealed class LanceJobContinuationStep : DomUpgradeStep
{
    internal const string StepId = "Combat.LanceContinuation";
    private static readonly OwnerSelector Owner = OwnerSelector.Class(typeof(JobDriver_LanceCharge));

    internal LanceJobContinuationStep()
        : base(StepId, "Keep launched jobs; resume the published two-toil walking charge without resetting indices, targets, timers or cooldowns.")
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (XmlElement driver in Owner.Candidates(context))
        {
            if (!context.IsLive(driver) || driver.Name != "curDriver" || driver.ParentNode is not XmlElement { Name: "jobs" } jobs
                || jobs["curJob"] is not { } job)
            {
                continue;
            }
            // 1.6.9438/9676/1.7.4979 的原版驱动都 forceSave 此字段；缺失不能当作“已经初始化的 0”。
            if (driver["curToilIndex"] is null)
            {
                throw new InvalidOperationException("Lance continuation has no explicitly saved current toil index.");
            }
            string? originalDef = job["def"]?.InnerText;
            string? currentDef = originalDef is null ? null : context.Tables.RenamedDef("JobDef", originalDef) ?? originalDef;
            if (currentDef is not (nameof(LancesDefOf.Mugirl_LancePointCharge) or nameof(LancesDefOf.Mugirl_LanceLineCharge)))
            {
                throw new InvalidOperationException("Lance continuation is not one of the two published charge jobs.");
            }
            string? target = job["targetA"]?.InnerText;
            var snapshot = new LanceJobContinuation.Snapshot(
                ReadInt(driver, "curToilIndex", 0), ReadInt(driver, "nextToilIndex", -1), ReadInt(job, "count", -1),
                ReadBool(driver, "ended"), target?.StartsWith("Thing_", StringComparison.Ordinal) == true,
                currentDef == nameof(LancesDefOf.Mugirl_LanceLineCharge) && IsCell(target));
            if (LanceJobContinuation.Resolve(snapshot) == LanceJobContinuation.Action.KeepCurrent)
            {
                continue;
            }
            plan.LegacyClasses.TryAdd(driver, context.OriginalClassOf(driver)!);
            plan.SetAttribute(driver, "Class", SaveTypeNames.Of(typeof(JobDriver_ResumeLanceCharge)));
        }
    }

    private static bool IsCell(string? text)
    {
        if (text is null || !text.StartsWith('(') || !text.EndsWith(')'))
        {
            return false;
        }
        string[] coordinates = text[1..^1].Split(',');
        return coordinates.Length == 3
            && int.TryParse(coordinates[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            && int.TryParse(coordinates[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
            && int.TryParse(coordinates[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z)
            && new IntVec3(x, y, z).IsValid;
    }

    private static int ReadInt(XmlElement owner, string label, int fallback) => owner[label] is not { } node ? fallback
        : int.TryParse(node.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value
        : throw new InvalidOperationException("Invalid lance continuation integer " + label + ".");

    private static bool ReadBool(XmlElement owner, string label) => owner[label] is { } node
        && (bool.TryParse(node.InnerText, out bool value) ? value : throw new InvalidOperationException("Invalid lance continuation flag " + label + "."));
}
