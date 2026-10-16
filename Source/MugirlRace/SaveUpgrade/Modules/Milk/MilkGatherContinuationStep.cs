using System.Globalization;
using System.Xml;
using Mugirl.Features.Milk;

namespace Mugirl.SaveUpgrade;

/// <summary>保留已发布的两段采集流程，只续接能由双向 Job 引用证明归属的等待状态。</summary>
internal sealed class MilkGatherContinuationStep : DomUpgradeStep
{
    internal const string StepId = "Milk.RelinkGatherContinuation";
    private static readonly OwnerSelector Owner = OwnerSelector.Class(typeof(JobDriver_GatherMilk));

    internal MilkGatherContinuationStep()
        : base(StepId, "Keep saved toil indices/progress; relink a partner wait only when its target points back to the collector.", "type:Mugirl.JobDriver_GatherMilk")
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        Dictionary<string, XmlElement> pawns = new(StringComparer.Ordinal);
        HashSet<string> ambiguous = new(StringComparer.Ordinal);
        foreach (var thing in context.Index.Things)
        {
            if (context.IsLive(thing.Element) && thing.Element["jobs"] is not null && thing.Element["id"] is { } id)
            {
                if (!pawns.TryAdd("Thing_" + id.InnerText, thing.Element))
                {
                    ambiguous.Add("Thing_" + id.InnerText);
                }
            }
        }

        foreach (XmlElement driver in Owner.Candidates(context))
        {
            if (!context.IsLive(driver) || driver.Name != "curDriver" || driver.ParentNode is not XmlElement jobs
                || jobs.Name != "jobs" || jobs.ParentNode is not XmlElement actor || jobs["curJob"] is not { } job
                || driver["curToilIndex"]?.InnerText != "1" || driver["activeGatherTarget"] is not null
                || driver["gatherEffectsActive"] is not null || driver[MilkPartnerWait.ScribeLabel] is not null)
            {
                continue;
            }

            string? targetReference = job["targetA"]?.InnerText;
            if (targetReference is null || !pawns.TryGetValue(targetReference, out XmlElement? target)
                || actor["id"] is not { } actorId)
            {
                continue;
            }

            string actorReference = "Thing_" + actorId.InnerText;
            if (ambiguous.Contains(targetReference) || ambiguous.Contains(actorReference))
            {
                throw new InvalidOperationException("Duplicate Pawn ID while relinking milk gathering.");
            }

            int waitId = MilkPartnerWait.NoJob;
            if (!ReferenceEquals(actor, target))
            {
                XmlElement? wait = target["jobs"]?["curJob"];
                if (wait?["def"]?.InnerText != "Job_MugirlMilkingTargetWait"
                    || wait["targetA"]?.InnerText != actorReference
                    || !int.TryParse(wait["loadID"]?.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out waitId)
                    || waitId < 0)
                {
                    // 原版 ForceWait 没有持久的来源关系；保持未认领，不猜测当前等待属于谁。
                    continue;
                }
            }

            plan.Append(driver, "activeGatherTarget", targetReference);
            plan.Append(driver, "gatherEffectsActive", "True");
            plan.Append(driver, MilkPartnerWait.ScribeLabel, waitId.ToString(CultureInfo.InvariantCulture));
        }
    }
}
