using System.Linq;
using System.Xml;
using Mugirl.Features.Milk;

namespace Mugirl.SaveUpgrade;

/// <summary>按本组件连续写出的私有状态识别旧奶量，保留同一 Pawn 上其他组件的同名字段。</summary>
internal sealed class MilkProductionLabelsStep : DomUpgradeStep
{
    internal const string StepId = "Milk.ProductionLabels";
    internal const string OldFullness = "milkFullness";
    internal const string NewFullness = "mugirlMilkFullness";
    internal static readonly OwnerSelector Owner = OwnerSelector.ThingComp(typeof(CompMilkProduction));

    // StaticCacheLifecycle: 不可变的存档协议表，进程内共享，不持有游戏对象。
    internal static readonly (string Old, string New)[] Fields =
    [
        (OldFullness, NewFullness),
        ("milkFullness_fullNotified", "mugirlMilkFullNotified"),
        ("milkFullness_lastFullNotifyTick", "mugirlMilkLastFullNotifyTick"),
        ("milkFullness_pendingResourceTicks", "mugirlMilkPendingTicks"),
        ("milkFullness_MilkThreshold", "mugirlMilkThreshold"),
    ];

    internal MilkProductionLabelsStep()
        : base(StepId, "Separate milk values by the contiguous legacy production state, leaving foreign fields intact.",
            Fields.Select(field => "label:" + Owner.Text + "/" + field.Old).ToArray())
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (XmlElement owner in Owner.Candidates(context).Where(context.IsLive))
        {
            XmlElement[] children = owner.ChildNodes.OfType<XmlElement>().ToArray();
            XmlElement[] fullness = children.Where(child => child.Name == OldFullness).ToArray();
            XmlElement[] state = children.Where(child => IsLegacyState(child.Name)).ToArray();
            if (state.Length == 0)
            {
                // 新字段已明确归属本组件；剩余旧名可由其他组件读取。离线再次运行也不能认领它们。
                if (fullness.Length == 0 || HasCurrentState(owner))
                {
                    continue;
                }

                if (fullness.Length != 1 || HasForeignMilkControls(owner))
                {
                    throw Ambiguous(owner, "no private production state identifies the shared milk label");
                }

                // 单一旧字段沿用既有协议；多个同名字段绝不凭位置或数值选一个。
                plan.Rename(fullness[0], NewFullness);
                continue;
            }

            if (HasCurrentState(owner) || state.Select(child => child.Name).Distinct(StringComparer.Ordinal).Count() != state.Length)
            {
                throw Ambiguous(owner, "duplicate or mixed legacy/current production state");
            }

            int first = Array.IndexOf(children, state[0]);
            int last = Array.IndexOf(children, state[^1]);
            if (last - first + 1 != state.Length)
            {
                throw Ambiguous(owner, "private production fields are not one contiguous block");
            }

            // 旧 PostExposeData 先写奶量，紧接满值通知、结算计时及阈值；默认值可省略。
            // 因而私有状态之前紧邻的奶量属于本组件；没有时，本组件保存的是默认零，不能拿另一块的奶量顶替。
            if (first > 0 && children[first - 1].Name == OldFullness)
            {
                plan.Rename(children[first - 1], NewFullness);
            }
            else
            {
                plan.Append(owner, NewFullness, "0");
            }

            foreach (var field in Fields.Skip(1))
            {
                if (owner[field.Old] is { } element)
                {
                    plan.Rename(element, field.New);
                }
            }
            // 绝对结算时刻由紧随其后的 Milk.ResourceAccrual 保留为一次性续接。
        }
    }

    private static bool IsLegacyState(string name) => name == MilkResourceAccrualStep.OldLabel
        || Fields.Skip(1).Any(field => field.Old == name);

    private static bool HasCurrentState(XmlElement owner) => owner[MilkResourceAccrualStep.NewLabel] is not null
        || Fields.Any(field => owner[field.New] is not null);

    private static bool HasForeignMilkControls(XmlElement owner) => owner["CanBeFedBy"] is not null
        || owner["AllowMilking"] is not null || owner["AllowMilkingSelf"] is not null || owner["AllowBreastFeeding"] is not null;

    private static InvalidOperationException Ambiguous(XmlElement owner, string reason) =>
        new("Cannot identify legacy milk production data for " + (owner["id"]?.InnerText ?? owner.Name) + ": " + reason + ".");
}
