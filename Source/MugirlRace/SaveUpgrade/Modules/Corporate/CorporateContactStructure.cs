using System.Linq;
using System.Xml;
using Mugirl.Features.Corporate;

namespace Mugirl.SaveUpgrade;

// 重挂原元素，不解析金额、文本或引用，也不把容器中的加载 ID 当作已删除对象。
internal sealed class CorporateContactStructure : DomUpgradeStep
{
    internal const string StepId = "Corporate.ContactStructure";
    internal static OwnerSelector Owner => OwnerSelector.GameComponent(typeof(CorporateIntroduction));

    // StaticCacheLifecycle: 进程级不可变结构描述，仅包含旧档标签与目标位置。
    internal static readonly (string Old, string Group, string New)[] Fields =
    [
        ("corporateIntroductionInitialized", "contact", "attached"),
        ("migrated", "contact", "retrofitted"),
        ("pending", "contact", "visitPending"),
        ("completed", "contact", "accountOpened"),
        ("independentContact", "contact", "usesIndependentContact"),
        ("giftsPrepared", "contact", "giftBundlePrepared"),
        ("independentAggression", "contact", "independentlyAggressive"),
        ("courierKilledByPlayer", "contact", "playerKilledCourier"),
        ("courierDied", "contact", "courierDead"),
        ("courierChoice", "contact", "courierOutcome"),
        ("contactTick", "contact", "lastContactTick"),
        ("nextServiceTick", "contact", "serviceResumeTick"),
        ("courier", "contact", "courierPawn"),
        ("representative", "contact", "representativePawn"),
        ("preferredMap", "contact", "visitMap"),
        ("introductionQuest", "contact", "quest"),
        ("representativeArrivalTick", "contact", "visitDueTick"),
    ];

    internal CorporateContactStructure() : base(StepId, "Regroup corporate state without changing values, ordering or asset ownership.",
        [.. Fields.Select(field => "label:" + Owner.Text + "/" + field.Old)])
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (XmlElement owner in Owner.Candidates(context).Where(context.IsLive))
        {
            bool hasOld = Fields.Any(field => owner[field.Old] is not null);
            bool hasNew = owner["contact"] is not null;
            if (hasNew)
            {
                if (hasOld)
                {
                    throw new InvalidOperationException("Mixed flat and grouped corporate state cannot be merged without losing data.");
                }

                continue;
            }

            var groups = new Dictionary<string, XmlElement>(StringComparer.Ordinal);
            groups.Add("contact", plan.AppendElement(owner, "contact"));
            foreach ((string oldName, string group, string newName) in Fields)
            {
                if (owner[oldName] is not { } element)
                {
                    continue;
                }

                if (group.Length == 0)
                {
                    plan.Rename(element, newName);
                }
                else
                {
                    plan.Move(element, groups[group], newName);
                }
            }
        }
    }
}
