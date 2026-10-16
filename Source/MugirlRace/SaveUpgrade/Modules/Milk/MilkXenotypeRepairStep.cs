using System.Globalization;
using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>把旧四个完成标志收敛为同义的 pending 状态，完整保留剩余倒计时。</summary>
internal sealed class MilkXenotypeRepairStep : DomUpgradeStep
{
    internal const string StepId = "Milk.XenotypeRepair";
    private static readonly string[] Flags = ["xenotypeFixed", "throwGeneFixed", "dunkGeneFixed", "throwSystemGeneFixed"];
    private static readonly OwnerSelector Owner = OwnerSelector.GameComponent(typeof(GameComponent_MilkUpgrade));

    internal MilkXenotypeRepairStep()
        : base(StepId, "Preserve delayed xenotype initialization across save/load and disabled Biotech.",
            "label:GameComponent(Mugirl.SaveUpgrade.GameComponent_MilkUpgrade)/xenotypeFixed",
            "label:GameComponent(Mugirl.SaveUpgrade.GameComponent_MilkUpgrade)/throwGeneFixed",
            "label:GameComponent(Mugirl.SaveUpgrade.GameComponent_MilkUpgrade)/dunkGeneFixed",
            "label:GameComponent(Mugirl.SaveUpgrade.GameComponent_MilkUpgrade)/throwSystemGeneFixed",
            "label:GameComponent(Mugirl.SaveUpgrade.GameComponent_MilkUpgrade)/checkTimer")
    {
    }

    internal override IEnumerable<string> IndexNames => ["li"];

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (XmlElement element in Owner.Candidates(context))
        {
            if (!context.IsLive(element) || !Owner.Matches(element, context))
            {
                continue;
            }

            if (element["xenotypeRepairPending"] is not null || element["xenotypeRepairTicks"] is not null)
            {
                if (element["xenotypeRepairPending"] is null || element["xenotypeRepairTicks"] is null
                    || element["checkTimer"] is not null || Array.Exists(Flags, label => element[label] is not null))
                {
                    throw new InvalidOperationException("Ambiguous xenotype repair schedule.");
                }

                continue;
            }

            bool allFixed = true;
            foreach (string label in Flags)
            {
                XmlElement? flag = element[label];
                bool fixedValue = false;
                if (flag is not null && !bool.TryParse(flag.InnerText, out fixedValue))
                {
                    throw new InvalidOperationException("Invalid xenotype completion flag " + label + ".");
                }

                allFixed &= fixedValue;
                if (flag is not null)
                {
                    plan.Remove(flag);
                }
            }

            int timer = 300;
            if (element["checkTimer"] is { } oldTimer)
            {
                if (!int.TryParse(oldTimer.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out timer))
                {
                    throw new InvalidOperationException("Invalid xenotype repair timer.");
                }

                plan.Remove(oldTimer);
            }

            plan.Append(element, "xenotypeRepairPending", allFixed ? "False" : "True");
            plan.Append(element, "xenotypeRepairTicks", timer.ToString(CultureInfo.InvariantCulture));
        }
    }
}
