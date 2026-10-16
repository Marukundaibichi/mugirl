using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>只补原版锁列表缺失的有效束具；原列表顺序、额外条目和独立锁状态均不重写。</summary>
internal sealed class RestraintWornLockListStep : DomUpgradeStep
{
    internal const string StepId = "Restraints.WornLockList";

    internal RestraintWornLockListStep()
        : base(StepId, "Materialize missing worn restraint references while retaining the normal post-load lock invariant.",
            "type:Mugirl.SlaveApparel", "type:Mugirl.AdvancedSlaveApparel", "type:Mugirl.BrainWashSlaveApparel")
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (var pawn in context.Index.Things)
        {
            if (!context.IsLive(pawn.Element) || pawn.Element["apparel"] is not { } tracker
                || tracker["wornApparel"]?["innerList"] is not { } worn)
            {
                continue;
            }

            XmlElement? locks = tracker["lockedApparel"];
            bool ignored = locks is not null && bool.TryParse(locks.GetAttribute("IsNull"), out bool isNull) && isNull;
            HashSet<string> existing = new(StringComparer.Ordinal);
            if (locks is not null && !ignored)
            {
                foreach (XmlNode node in locks.ChildNodes)
                {
                    if (node is XmlElement reference)
                    {
                        existing.Add(reference.InnerText);
                    }
                }
            }

            foreach (XmlNode node in worn.ChildNodes)
            {
                if (node is not XmlElement apparel || !RestraintLockStateStep.Owner.Matches(apparel, context)
                    || !ReadBool(apparel, "mugirlRestraintLocked", true)
                    || (RestraintLockStateStep.Advanced.Matches(apparel, context) && ReadBool(apparel, "mugirlRestraintCracked", false))
                    || !context.Index.TryGetThing(apparel, out IndexedThing thing))
                {
                    continue;
                }

                string reference = "Thing_" + thing.Id;
                string? renamedDef = context.Tables.RenamedDef("ThingDef", thing.Def);
                string canonicalReference = renamedDef is null ? reference : "Thing_" + LoadIdTokens.RenamedThingId(thing.Id, renamedDef);
                string? originalId = renamedDef is null ? null : LoadIdTokens.OriginalThingId(thing.Def, renamedDef, thing.Id);
                if (existing.Contains(reference) || existing.Contains(canonicalReference) || (originalId is not null && existing.Contains("Thing_" + originalId)))
                {
                    continue;
                }

                if (ignored)
                {
                    plan.Remove(locks!);
                    locks = null;
                    ignored = false;
                }

                locks ??= plan.AppendElement(tracker, "lockedApparel");
                plan.Append(locks, "li", reference);
                existing.Add(reference);
                existing.Add(canonicalReference);
            }
        }
    }

    private static bool ReadBool(XmlElement owner, string label, bool fallback)
    {
        if (owner[label] is not { } value)
        {
            return fallback;
        }

        return bool.TryParse(value.InnerText, out bool result)
            ? result
            : throw new InvalidOperationException("Invalid restraint flag " + label + ".");
    }
}
