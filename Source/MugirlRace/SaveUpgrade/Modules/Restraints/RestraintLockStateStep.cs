using System.Globalization;
using System.Xml;
using Mugirl.Features.Restraints;

namespace Mugirl.SaveUpgrade;

/// <summary>锁数和锁定标志独立保存；缺失锁数使用原 Def 读取方的动态默认值。</summary>
internal sealed class RestraintLockStateStep : DomUpgradeStep
{
    internal const string StepId = "Restraints.LockState";
    internal static readonly OwnerSelector Owner = OwnerSelector.Class(typeof(RestraintApparel), typeof(AdvancedRestraintApparel), typeof(BrainwashHelmetApparel));
    internal static readonly OwnerSelector Advanced = OwnerSelector.Class(typeof(AdvancedRestraintApparel), typeof(BrainwashHelmetApparel));

    internal RestraintLockStateStep()
        : base(StepId, "Preserve independent lock/cracked values and materialize Def-dependent omitted lock count.",
            "label:" + Owner.Text + "/lockCount", "label:" + Owner.Text + "/isLocked", "label:" + Advanced.Text + "/isCracked")
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (XmlElement owner in Owner.Candidates(context))
        {
            if (!context.IsLive(owner))
            {
                continue;
            }

            if (owner["mugirlRestraintLockCount"] is null && owner["lockCount"] is null)
            {
                string def = owner["def"]?.InnerText ?? string.Empty;
                string currentDef = context.Tables.RenamedDef("ThingDef", def) ?? def;
                // 旧 SlaveDef 转型失败时的默认也是 0；有效公开别名的继承默认由目录读取。
                int value = context.Catalog.IntegerField("ThingDef", currentDef, "needkeynumber") ?? 0;
                plan.Append(owner, "mugirlRestraintLockCount", value.ToString(CultureInfo.InvariantCulture));
            }

            MoveValue(owner, "lockCount", "mugirlRestraintLockCount", plan);
            MoveValue(owner, "isLocked", "mugirlRestraintLocked", plan);
            if (Advanced.Matches(owner, context))
            {
                MoveValue(owner, "isCracked", "mugirlRestraintCracked", plan);
            }
        }
    }

    private static void MoveValue(XmlElement owner, string oldLabel, string currentLabel, EditPlan plan)
    {
        XmlElement? first = owner[oldLabel];
        bool retainFirst = owner[currentLabel] is null;
        foreach (XmlNode child in owner.ChildNodes)
        {
            if (child is XmlElement element && element.Name == oldLabel)
            {
                if (retainFirst && ReferenceEquals(first, element))
                {
                    plan.Rename(element, currentLabel);
                }
                else
                {
                    plan.Remove(element);
                }
            }
        }
    }
}
