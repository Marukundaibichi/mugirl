using System.Linq;
using System.Xml;
using Mugirl.Features.Incidents;

namespace Mugirl.SaveUpgrade;

internal sealed class StorylineStateUpgrade : DomUpgradeStep
{
    internal const string StepId = "Incidents.StorylineState";
    internal static OwnerSelector Owner => OwnerSelector.GameComponent(typeof(GameComponent_Storyline));
    // StaticCacheLifecycle: 不可变的旧标签与新值聚合位置。
    internal static readonly (string Old, string Group, string New)[] Fields =
    [
        ("openingCrashStarted", "opening", "started"),
        ("openingCrashCheckTimer", "opening", "checkRemaining"),
        ("openingCrashPawns", "opening", "pawns"),
        ("courierRaidTriggered", "courier", "triggered"),
        ("courierRaidTimer", "courier", "delayRemaining"),
        ("courierRaidQuestStarted", "courier", "questStarted"),
        ("fusionInvestmentAccepted", "investment", "accepted"),
        ("fusionInvestmentPending", "investment", "pending"),
        ("fusionInvestmentAmount", "investment", "amount"),
        ("fusionInvestmentTimer", "investment", "resultRemaining"),
        ("fusionInvestmentNextOfferTick", "investment", "nextOfferTick"),
        ("fusionInvestmentInvestorActive", "investment", "investorActive"),
        ("fusionInvestmentInvestor", "investment", "investor"),
    ];

    internal StorylineStateUpgrade() : base(StepId, "Keep every flag, countdown and Pawn reference while grouping storyline state.",
        [.. Fields.Select(field => "label:" + Owner.Text + "/" + field.Old)])
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (XmlElement owner in Owner.Candidates(context).Where(context.IsLive))
        {
            if (owner["opening"] is not null || owner["courier"] is not null || owner["investment"] is not null)
            {
                if (Fields.Any(field => owner[field.Old] is not null))
                {
                    throw new InvalidOperationException("Mixed flat and grouped storyline data.");
                }

                continue;
            }

            var groups = new Dictionary<string, XmlElement>(StringComparer.Ordinal)
            {
                ["opening"] = plan.AppendElement(owner, "opening"),
                ["courier"] = plan.AppendElement(owner, "courier"),
                ["investment"] = plan.AppendElement(owner, "investment"),
            };
            foreach ((string oldName, string group, string newName) in Fields)
            {
                if (owner[oldName] is { } node)
                {
                    plan.Move(node, groups[group], newName);
                }
            }
        }
    }
}
