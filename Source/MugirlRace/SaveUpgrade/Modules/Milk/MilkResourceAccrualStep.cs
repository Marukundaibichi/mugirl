using System.Globalization;
using System.Xml;
using Mugirl.Features.Milk;

namespace Mugirl.SaveUpgrade;

/// <summary>旧绝对时刻转换成可存档的单次续接，不在 DOM 阶段抢先按加载时刻结算。</summary>
internal sealed class MilkResourceAccrualStep : DomUpgradeStep
{
    internal const string StepId = "Milk.ResourceAccrual";
    internal const string OldLabel = "milkFullness_lastResourceUpdateTick";
    internal const string NewLabel = "mugirlMilkAccrualContinuation";
    internal static readonly OwnerSelector Owner = OwnerSelector.ThingComp(typeof(CompMilkProduction));

    internal MilkResourceAccrualStep()
        : base(StepId, "Keep the absolute legacy accrual anchor until the first active resource update.", "label:" + Owner.Text + "/" + OldLabel)
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (XmlElement element in context.Index.ElementsNamed(OldLabel))
        {
            if (!context.IsLive(element) || element.ParentNode is not XmlElement owner || !Owner.Matches(owner, context))
            {
                continue;
            }

            if (owner[NewLabel] is not null || !int.TryParse(element.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tick))
            {
                throw new InvalidOperationException("Ambiguous or invalid milk accrual continuation in " + owner.Name + ".");
            }

            XmlElement continuation = plan.AppendElement(owner, NewLabel);
            plan.SetAttribute(continuation, "Class", SaveTypeNames.Of(typeof(LegacyMilkAccrualContinuation)));
            plan.Move(element, continuation, "anchorTick");
        }
    }
}
