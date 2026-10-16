using System.Globalization;
using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 旧版 Mugirl LordJob 的状态图：Lord 在 PostLoadInit 按下标恢复 LordToil 与 Trigger 数据，早于任何读档后阶段，
/// 所以状态图改动只能在 DOM 中处理（重置到第一个 LordToil，或删除整个 Lord；对它的 <c>Lord_&lt;id&gt;</c> 引用由加载 ID 步骤置空）。
/// </summary>
/// <remarks>
/// 重置只删除状态图数据并把当前 LordToil 下标写成 0；<c>Lord.SetJob(loading: true)</c> 不进入 LordToil，
/// 所以记录这些 Lord 的 <c>loadID</c>，阶段 B 再让它们重新进入起始 LordToil（初始化并更新成员的职责）。
/// </remarks>
internal sealed class LordPolicyStep : DomUpgradeStep
{
    internal const string StepId = "Framework.Lords";

    private static readonly string[] GraphLabels = ["lordToilData", "triggerData", "ticksInToil"];

    internal LordPolicyStep()
        : base(StepId, "Reset or remove lords whose legacy LordJob graph changed.")
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        if (context.Tables.LordPolicies.Count == 0)
        {
            return;
        }

        foreach (XmlElement lord in context.Index.Lords)
        {
            if (!context.IsLive(lord) || lord["lordJob"] is not { } lordJob || context.OriginalClassOf(lordJob) is not { } lordJobClass)
            {
                continue;
            }

            switch (context.Tables.LordPolicyFor(lordJobClass)?.Policy)
            {
                case LordJobPolicy.ResetGraph:
                    Reset(lord, plan);
                    break;
                case LordJobPolicy.RemoveLord:
                    plan.RemoveItem(lord, NullForm.Value);
                    break;
                default:
                    break;
            }
        }
    }

    private static void Reset(XmlElement lord, EditPlan plan)
    {
        foreach (string label in GraphLabels)
        {
            if (lord[label] is { } data)
            {
                plan.Remove(data);
            }
        }

        // 缺省值 -1 会让 Lord 报“当前 LordToil 下标越界”；0 是状态图的起始 LordToil。
        if (lord["curLordToilIdx"] is { } index)
        {
            plan.SetText(index, "0");
        }
        else
        {
            plan.Append(lord, "curLordToilIdx", "0");
        }

        // loadID 为默认值 0 时 Scribe 不写这个标签。
        string loadId = lord["loadID"]?.InnerText.Trim() is { Length: > 0 } text ? text : "0";
        if (int.TryParse(loadId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
        {
            plan.ResetLordIds.Add(id);
        }
    }
}
