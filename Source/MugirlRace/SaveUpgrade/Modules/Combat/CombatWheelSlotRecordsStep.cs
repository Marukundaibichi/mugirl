using System.Xml;
using Mugirl.Features.WeaponWheel;

namespace Mugirl.SaveUpgrade;

/// <summary>将旧并行列表逐索引搬入独立槽位记录；引用文本不变，之后由真实 Scribe 引用读取边界解析。</summary>
internal sealed class CombatWheelSlotRecordsStep : DomUpgradeStep
{
    internal const string StepId = "Combat.WheelSlotRecords";
    internal const string OldWeapons = "weaponWheelSlots";
    internal const string OldUnlocks = "weaponWheelUnlockedSlots";
    internal const string NewRecords = "mugirlWheelSlotRecords";
    private static readonly OwnerSelector Owner = OwnerSelector.ThingComp(typeof(CompWeaponWheel));

    internal CombatWheelSlotRecordsStep()
        : base(StepId, "Preserve slot positions, weapon references and unlock values in one record per index.",
            "label:" + Owner.Text + "/" + OldWeapons, "label:" + Owner.Text + "/" + OldUnlocks)
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (XmlElement pawn in Owner.Candidates(context))
        {
            if (!context.IsLive(pawn))
            {
                continue;
            }
            XmlElement? weapons = pawn[OldWeapons];
            XmlElement? unlocks = pawn[OldUnlocks];
            if (weapons is null && unlocks is null)
            {
                continue;
            }
            if (pawn[NewRecords] is not null)
            {
                throw new InvalidOperationException("Weapon-wheel save contains both parallel lists and slot records.");
            }

            List<XmlElement> weaponItems = Entries(weapons);
            List<XmlElement> unlockItems = Entries(unlocks);
            XmlElement records = plan.AppendElement(pawn, NewRecords);
            for (int i = 0; i < Math.Max(weaponItems.Count, unlockItems.Count); i++)
            {
                XmlElement slot = plan.AppendElement(records, "li");
                if (i < weaponItems.Count)
                {
                    plan.Move(weaponItems[i], slot, "weapon");
                }
                if (i < unlockItems.Count)
                {
                    plan.Move(unlockItems[i], slot, "unlocked");
                }
            }
            if (weapons is not null)
            {
                plan.Remove(weapons);
            }
            if (unlocks is not null)
            {
                plan.Remove(unlocks);
            }
        }
    }

    private static List<XmlElement> Entries(XmlElement? list)
    {
        List<XmlElement> items = [];
        if (list is null || string.Equals(list.GetAttribute("IsNull"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return items;
        }
        for (XmlNode? node = list.FirstChild; node is not null; node = node.NextSibling)
        {
            if (IsFormattingWhitespace(node))
            {
                continue;
            }
            if (node is not XmlElement { Name: "li" } item || DomEditor.HasElementChild(item))
            {
                throw new InvalidOperationException("Legacy weapon-wheel list is not a flat value/reference list.");
            }
            items.Add(item);
        }
        return items;
    }

    private static bool IsFormattingWhitespace(XmlNode node)
    {
        // Canonical previews preserve indentation; only the list's separators are formatting.
        // Never trim a li value or discard non-whitespace text, CDATA or unknown nodes.
        if (node.NodeType is not (XmlNodeType.Text or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
            || node.Value is not { } text)
        {
            return false;
        }
        foreach (char value in text)
        {
            if (value is not (' ' or '\t' or '\r' or '\n'))
            {
                return false;
            }
        }
        return true;
    }
}
