using System.Linq;
using System.Xml;
using Mugirl.Features.Restraints;

namespace Mugirl.SaveUpgrade;

/// <summary>共享旧标签按 Scribe 的第一个直接子节点语义复制；新独有标签优先，嵌套播放状态不参与。</summary>
internal sealed class RestraintBrainwashLabelsStep : DomUpgradeStep
{
    internal const string StepId = "Restraints.BrainwashTriggerLabels";
    internal static readonly (OwnerSelector Owner, string Age, string? Triggered)[] Sites =
    [
        (OwnerSelector.HediffComp(typeof(HediffComp_AddMentalState)), "mugirlAddMentalStateAge", "mugirlAddMentalStateTriggered"),
        (OwnerSelector.HediffComp(typeof(HediffComp_AddThought)), "mugirlAddThoughtAge", "mugirlAddThoughtTriggered"),
        (OwnerSelector.HediffComp(typeof(HediffComp_AddTrait)), "mugirlAddTraitAge", "mugirlAddTraitTriggered"),
        (OwnerSelector.HediffComp(typeof(HediffComp_ReduceWillOrEnslave)), "mugirlReduceWillAge", "mugirlReduceWillTriggered"),
        (OwnerSelector.HediffComp(typeof(HediffComp_SuppressionEnhancer)), "mugirlSuppressionAge", null),
    ];

    internal RestraintBrainwashLabelsStep()
        : base(StepId, "Copy each first shared value to its actual comp without replacing existing unique labels.",
            [.. Sites.SelectMany(site => site.Triggered is null
                ? new[] { "label:" + site.Owner.Text + "/age" }
                : new[] { "label:" + site.Owner.Text + "/age", "label:" + site.Owner.Text + "/triggered" })])
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (var hediff in context.Index.Hediffs)
        {
            XmlElement owner = hediff.Element;
            if (!context.IsLive(owner))
            {
                continue;
            }

            XmlElement? age = owner["age"];
            XmlElement? triggered = owner["triggered"];
            bool matchedAge = false;
            bool matchedTrigger = false;
            foreach (var site in Sites)
            {
                if (!site.Owner.Matches(owner, context))
                {
                    continue;
                }

                matchedAge = true;
                if (age is not null && owner[site.Age] is null)
                {
                    plan.Append(owner, site.Age, age.InnerText);
                }

                if (site.Triggered is { } label)
                {
                    matchedTrigger = true;
                    if (triggered is not null && owner[label] is null)
                    {
                        plan.Append(owner, label, triggered.InnerText);
                    }
                }
            }

            foreach (XmlNode child in owner.ChildNodes)
            {
                if (child is XmlElement element && ((matchedAge && element.Name == "age") || (matchedTrigger && element.Name == "triggered")))
                {
                    plan.Remove(element);
                }
            }
        }
    }
}
