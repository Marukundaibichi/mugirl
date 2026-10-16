using System.Linq;
using System.Xml;
using Mugirl.Features.Corporate;

namespace Mugirl.SaveUpgrade;

internal sealed class CorporateSupportGraph : DomUpgradeStep
{
    internal const string StepId = "Corporate.SupportGraph";

    internal CorporateSupportGraph() : base(StepId, "Preserve Lord graph indexes, trigger clocks and first-tick timing with a durable continuation.",
        "label:Class(Mugirl.Features.Corporate.LordJob_CorporateSupport)/supportGraphVersion")
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (XmlElement job in OwnerSelector.Class(typeof(LordJob_CorporateSupport)).Candidates(context).Where(context.IsLive))
        {
            XmlElement? version = job["supportGraphVersion"];
            if (job["continuation"] is not null)
            {
                if (version is not null)
                {
                    throw new InvalidOperationException("Mixed corporate support graph version and continuation.");
                }

                continue;
            }

            if (CorporateReceiptDefaults.ReadInt(job, "supportGraphVersion") == 0)
            {
                XmlElement pending = plan.AppendElement(job, "continuation");
                plan.SetAttribute(pending, "Class", SaveTypeNames.Of(typeof(CorporateSupportGraphCompletion)));
            }

            if (CorporateReceiptDefaults.ReadInt(job, "supportGraphVersion") != 0)
            {
                XmlElement completed = plan.AppendElement(job, "continuation");
                plan.SetAttribute(completed, "IsNull", "True");
            }

            if (version is not null)
            {
                plan.Remove(version);
            }
        }
    }
}
