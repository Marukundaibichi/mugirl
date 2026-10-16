using System.Linq;
using System.Xml;
using Mugirl.Features.Abilities;

namespace Mugirl.SaveUpgrade;

/// <summary>把控制器的运动字段无损重挂到规范状态对象，不推断阶段、轨迹或动画模式。</summary>
internal sealed class MilkDunkMotionStep : DomUpgradeStep
{
    internal const string StepId = "Milk.DunkMotion";
    internal static readonly OwnerSelector Owner = OwnerSelector.Class(typeof(DunkController));
    internal static readonly (string Old, string Current)[] Fields =
    [
        ("state", "phase"), ("stateTicks", "phaseTicks"), ("runningTicks", "runningTicks"),
        ("launchDrawPos", "launchHeadPosition"), ("launchCarrierDrawPos", "launchCarrierPosition"),
        ("slamStartDrawPos", "impactStartHeadPosition"), ("slamStartCarrierDrawPos", "impactStartCarrierPosition"),
        ("followsCarrier", "followsCarrier"), ("slamAnimating", "impactAnimating"), ("slamTracksCarrier", "impactTracksCarrier"),
        ("launchSpinDegrees", "launchSpinDegrees"), ("slamStartSpinDegrees", "impactStartSpinDegrees"),
    ];

    internal MilkDunkMotionStep()
        : base(StepId, "Group exact motion values without deriving or discarding saved state.", [.. Fields.Select(field => "label:" + Owner.Text + "/" + field.Old)])
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (XmlElement controller in Owner.Candidates(context))
        {
            if (!context.IsLive(controller))
            {
                continue;
            }

            if (controller["motion"] is not null)
            {
                if (Fields.Any(field => controller[field.Old] is not null))
                {
                    throw new InvalidOperationException("Ambiguous dunk motion state.");
                }

                continue;
            }

            XmlElement motion = plan.AppendElement(controller, "motion");
            foreach (var field in Fields)
            {
                if (controller[field.Old] is { } value)
                {
                    plan.Move(value, motion, field.Current);
                }
            }
        }
    }
}
