using System.Linq;
using System.Xml;
using Mugirl.Features.Corporate;

namespace Mugirl.SaveUpgrade;

// 重挂原元素，不解析金额、文本或引用，也不把容器中的加载 ID 当作已删除对象。
internal sealed class CorporateNetworkStructure : DomUpgradeStep
{
    internal const string StepId = "Corporate.NetworkStructure";
    internal static OwnerSelector Owner => OwnerSelector.GameComponent(typeof(CorporateNetwork));

    // StaticCacheLifecycle: 进程级不可变结构描述，仅包含旧档标签与目标位置。
    internal static readonly (string Old, string Group, string New)[] Fields =
    [
        ("corporateVault", "", "vault"),
        ("corporateNextRefresh", "", "nextRefreshTick"),
        ("corporateSequence", "", "sequence"),
        ("corporateRecords", "history", "entries"),
        ("corporateLoanPrincipal", "loan", "principal"),
        ("corporateLoanInterest", "loan", "interest"),
        ("corporateLoanOverdueInterestTotal", "loan", "overdueInterestTotal"),
        ("corporateLoanOriginalPrincipal", "loan", "originalPrincipal"),
        ("corporateLoanLastAccrualTick", "loan", "lastAccrualTick"),
        ("corporateLoanDueTick", "loan", "dueTick"),
        ("corporateLoanNextRaidTick", "loan", "nextRaidTick"),
        ("corporateLoanDefaulted", "loan", "defaulted"),
        ("corporateLoanThreeDayReminder", "loan", "threeDayReminder"),
        ("corporateLoanOneDayReminder", "loan", "oneDayReminder"),
        ("corporateLoanCollateralValue", "loan", "collateralValue"),
        ("corporateLoanCollateralLiquidated", "loan", "collateralLiquidated"),
        ("corporateLoanCollateral", "loan", "collateral"),
        ("corporateFinancePendingSilver", "loan", "pendingSilver"),
        ("corporateLoanFaction", "loan", "creditor"),
        ("corporateLoanSignedDailyRate", "loan", "dailyRate"),
        ("corporateLoanSignedOverdueRate", "loan", "overdueDailyRate"),
        ("corporateLoanSignedLiquidationRatio", "loan", "liquidationRatio"),
        ("corporateStock", "trade", "stock"),
        ("corporateOrders", "trade", "orders"),
        ("corporatePeopleOffers", "people", "offers"),
        ("corporatePeopleAwaitingWorld", "people", "awaitingWorld"),
        ("corporatePeoplePendingSilver", "people", "pendingSilver"),
        ("corporateMissions", "missionBoard", "missions"),
        ("nextMissionId", "missionBoard", "nextId"),
        ("fusionQualificationSeen", "missionBoard", "fusionQualified"),
        ("fusionInvitationCreated", "missionBoard", "fusionInvited"),
        ("fusionInvitationTick", "missionBoard", "fusionDueTick"),
        ("retainedFusionInvestor", "missionBoard", "retainedInvestor"),
        ("retainedFusionInvestorName", "missionBoard", "retainedInvestorName"),
        ("corporateTradeTurnover", "services", "turnover"),
        ("corporateCreditedPersonIds", "services", "creditedPawnIds"),
        ("corporateGiftWeek", "services", "giftWeek"),
        ("corporateFreePersonWeek", "services", "freePersonWeek"),
        ("corporateSupportWeek", "services", "supportWeek"),
        ("corporateSupportCallsUsed", "services", "supportCallsUsed"),
    ];

    internal CorporateNetworkStructure() : base(StepId, "Regroup corporate state without changing values, ordering or asset ownership.",
        [.. Fields.Select(field => "label:" + Owner.Text + "/" + field.Old)])
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (XmlElement owner in Owner.Candidates(context).Where(context.IsLive))
        {
            bool hasOld = Fields.Any(field => owner[field.Old] is not null);
            bool hasNew = owner["history"] is not null || owner["loan"] is not null || owner["trade"] is not null || owner["people"] is not null || owner["missionBoard"] is not null || owner["services"] is not null;
            if (hasNew)
            {
                if (hasOld)
                {
                    throw new InvalidOperationException("Mixed flat and grouped corporate state cannot be merged without losing data.");
                }

                continue;
            }

            var groups = new Dictionary<string, XmlElement>(StringComparer.Ordinal);
            groups.Add("history", plan.AppendElement(owner, "history"));
            groups.Add("loan", plan.AppendElement(owner, "loan"));
            groups.Add("trade", plan.AppendElement(owner, "trade"));
            groups.Add("people", plan.AppendElement(owner, "people"));
            groups.Add("missionBoard", plan.AppendElement(owner, "missionBoard"));
            groups.Add("services", plan.AppendElement(owner, "services"));
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
