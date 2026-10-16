using System.Globalization;
using System.Linq;
using System.Xml;
using Mugirl.Features.Corporate;

namespace Mugirl.SaveUpgrade;

internal sealed class CorporateReceiptDefaults : DomUpgradeStep
{
    internal const string StepId = "Corporate.ReceiptDefaults";

    internal CorporateReceiptDefaults() : base(StepId, "Materialize signed receipts, distribution targets and deferred pricing before domain loading.",
        "label:Class(Mugirl.Features.Corporate.CorporatePersonOffer)/priceVersion")
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (XmlElement owner in CorporateNetworkStructure.Owner.Candidates(context).Where(context.IsLive))
        {
            // 已分组的规范数据不重复进入这些一次性变换。
            foreach (XmlElement order in Items(owner["corporateOrders"]))
            {
                CompleteOrder(plan, order);
            }

            foreach (XmlElement offer in Items(owner["corporatePeopleOffers"]))
            {
                CompleteOffer(plan, offer);
            }

            foreach (XmlElement mission in Items(owner["corporateMissions"]))
            {
                if (mission["kind"]?.InnerText == "Distribution" && ReadInt(mission, "salesGoal") <= 0)
                {
                    SetInt(plan, mission, "salesGoal", CorporateLegacyDefaults.DistributionTarget(ReadInt(mission, "count")));
                }
            }

            // 历史哨兵会从已保存的周次推导一次已用呼叫，不能一概恢复为零。
            if (owner["services"] is null && ReadInt(owner, "corporateSupportCallsUsed", -1) < 0)
            {
                SetInt(plan, owner, "corporateSupportCallsUsed", ReadInt(owner, "corporateSupportWeek", -1) >= 0 ? 1 : 0);
            }
        }

        // 显式 Class 对象也可能保存在标准组件列表之外。
        foreach (XmlElement order in OwnerSelector.Class(typeof(CorporateOrder)).Candidates(context).Where(context.IsLive))
        {
            if (order.ParentNode?.Name != "corporateOrders")
            {
                CompleteOrder(plan, order);
            }
        }

        foreach (XmlElement offer in OwnerSelector.Class(typeof(CorporatePersonOffer)).Candidates(context).Where(context.IsLive))
        {
            if (offer.ParentNode?.Name != "corporatePeopleOffers")
            {
                CompleteOffer(plan, offer);
            }
        }
    }

    private static void CompleteOrder(EditPlan plan, XmlElement order)
    {
        if (ReadInt(order, "creditedTurnover", -1) < 0)
        {
            int quantity = ReadInt(order, "quantity");
            int amount = quantity <= 0 ? 0 : (int)((long)ReadInt(order, "paid") * Math.Min(quantity, ReadInt(order, "deliveredQuantity")) / quantity);
            SetInt(plan, order, "creditedTurnover", amount);
        }
    }

    private static void CompleteOffer(EditPlan plan, XmlElement offer)
    {
        if (ReadBool(offer, "paid") && ReadInt(offer, "paidAmount", -1) < 0)
        {
            SetInt(plan, offer, "paidAmount", ReadInt(offer, "price"));
        }

        XmlElement? version = offer["priceVersion"];
        if (offer["continuation"] is not null)
        {
            if (version is not null)
            {
                throw new InvalidOperationException("Mixed corporate offer continuation and priceVersion.");
            }

            return;
        }

        XmlElement continuation = plan.AppendElement(offer, "continuation");
        if (ReadInt(offer, "priceVersion") == 0)
        {
            plan.SetAttribute(continuation, "Class", SaveTypeNames.Of(typeof(CorporateOfferPriceCompletion)));
        }
        else
        {
            plan.SetAttribute(continuation, "IsNull", "True");
        }

        if (version is not null)
        {
            plan.Remove(version);
        }
    }
    private static IEnumerable<XmlElement> Items(XmlElement? list)
    {
        if (list is null) { yield break; }
        bool isNull = list.GetAttribute("IsNull").Equals("true", StringComparison.OrdinalIgnoreCase);
        foreach (XmlNode node in list.ChildNodes)
        {
            // 仅列表直属排版空白不是条目。记录内的玩家文本绝不 Trim 或重新解析。
            if (node.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace or XmlNodeType.Text
                && (node.Value ?? string.Empty).All(character => character is ' ' or '\t' or '\r' or '\n'))
            {
                continue;
            }
            if (isNull || node is not XmlElement { Name: "li" } item)
            {
                throw new InvalidOperationException("Legacy corporate list " + list.Name + " contains unsupported data.");
            }
            if (!item.GetAttribute("IsNull").Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                yield return item;
            }
        }
    }

    internal static int ReadInt(XmlElement owner, string label, int fallback = 0) => owner[label] is { } value
        ? int.Parse(value.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture) : fallback;

    private static bool ReadBool(XmlElement owner, string label) => owner[label] is { } value && bool.Parse(value.InnerText);

    private static void SetInt(EditPlan plan, XmlElement owner, string label, int value)
    {
        string text = value.ToString(CultureInfo.InvariantCulture);
        if (owner[label] is { } existing)
        {
            plan.SetText(existing, text);
        }
        else
        {
            plan.Append(owner, label, text);
        }
    }
}
