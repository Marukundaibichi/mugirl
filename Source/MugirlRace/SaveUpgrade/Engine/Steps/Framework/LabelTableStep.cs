using System.Linq;
using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 标签表、枚举表、值表与旧默认值补写：只作用于所属元素被选择器选中的标签，从不按标签名全局改名。
/// </summary>
/// <remarks>
/// 同一个旧标签在同一所属元素上只能命中一行（否则计划失败）；改名目标已存在于所属元素中也算计划失败，
/// 因为那说明存档里同时有新旧两种结构，任何自动选择都可能丢数据（同一选择器内的改名链与互换在表格构建时已被拒绝）。
/// 补写旧默认值要求所属元素带有登记的旧版证据。
/// </remarks>
internal sealed class LabelTableStep : DomUpgradeStep
{
    internal const string StepId = "Framework.Labels";

    internal LabelTableStep()
        : base(StepId, "Rename, drop and remap owner-scoped labels, enum members and saved values; materialise changed defaults.")
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        Dictionary<XmlElement, HashSet<string>> created = [];
        PlanLabels(context, plan, created);
        PlanEnums(context, plan);
        PlanValues(context, plan);
        PlanDefaults(context, plan, created);
    }

    private static void PlanLabels(UpgradeContext context, EditPlan plan, Dictionary<XmlElement, HashSet<string>> created)
    {
        foreach (string oldLabel in context.Tables.LabelRows.Select(row => row.Old).Distinct(StringComparer.Ordinal))
        {
            IReadOnlyList<LabelRow> rows = context.Tables.LabelRowsFor(oldLabel);
            foreach (XmlElement element in context.Index.ElementsNamed(oldLabel))
            {
                if (!context.IsLive(element) || element.ParentNode is not XmlElement owner)
                {
                    continue;
                }

                LabelRow? match = null;
                foreach (LabelRow row in rows)
                {
                    if (!row.Owner.Matches(owner, context))
                    {
                        continue;
                    }

                    if (match is not null)
                    {
                        // 多个组件曾共用一个物理字段；同一显式结构步骤可一次读取后按组件复制。
                        // 不同消费者，或消费与改名/删除混用，仍然是无法自动选择的歧义。
                        if (match.Disposition == LabelDisposition.Consumed && row.Disposition == LabelDisposition.Consumed && match.Step == row.Step)
                        {
                            continue;
                        }

                        throw new InvalidOperationException("Label <" + oldLabel + "> under <" + owner.Name + "> matches both '" + match.Owner.Text + "' and '" + row.Owner.Text + "'.");
                    }

                    match = row;
                }

                switch (match?.Disposition)
                {
                    case LabelDisposition.Rename:
                        string newLabel = match.New!;
                        if (owner[newLabel] is not null || (created.TryGetValue(owner, out HashSet<string>? names) && names.Contains(newLabel)))
                        {
                            throw new InvalidOperationException("Cannot rename <" + oldLabel + "> to <" + newLabel + "> under '" + match.Owner.Text + "': the owner already has <" + newLabel + ">.");
                        }

                        plan.Rename(element, newLabel);
                        Created(created, owner).Add(newLabel);
                        break;
                    case LabelDisposition.Drop:
                        plan.Remove(element);
                        break;
                    default:
                        break;
                }
            }
        }
    }

    private static void PlanEnums(UpgradeContext context, EditPlan plan)
    {
        if (context.Tables.EnumRows.Count == 0)
        {
            return;
        }

        foreach (EnumSite site in context.Tables.EnumSites)
        {
            foreach ((XmlElement _, XmlElement leaf) in Leaves(context, site.Owner, site.Label, site.Part))
            {
                string text = leaf.InnerText;
                string mapped = site.Flags
                    ? string.Join(", ", text.Split(',').Select(member => Member(context, site.Enum, member.Trim())))
                    : Member(context, site.Enum, text.Trim());
                if (mapped != text.Trim())
                {
                    plan.SetText(leaf, mapped);
                }
            }
        }
    }

    private static void PlanValues(UpgradeContext context, EditPlan plan)
    {
        foreach (ValueContext valueContext in context.Tables.ValueContexts)
        {
            if (valueContext.Kind == ValueContextKind.Text)
            {
                IReadOnlyDictionary<string, ValueRow> rows = context.Tables.ValuesFor(valueContext.Name);
                if (rows.Count == 0)
                {
                    continue;
                }

                foreach ((XmlElement label, XmlElement leaf) in Leaves(context, valueContext.Owner, valueContext.Label, valueContext.Part))
                {
                    if (!rows.TryGetValue(leaf.InnerText, out ValueRow? row))
                    {
                        continue;
                    }

                    if (row.New is not null)
                    {
                        plan.SetText(leaf, row.New);
                    }
                    else
                    {
                        // 单值标签删除后读为默认值；列表项按容器规则删除（可移除容器、字典成对、并行组）或置空。
                        ValueLeaves.Remove(plan, label, leaf, NullForm.Value);
                    }
                }
            }
            else if (valueContext.Kind == ValueContextKind.Def)
            {
                // Scribe_Defs 读取的 Def 引用（DefReference）由 BackCompatibleDefName 后缀改名，这里只改字符串保存的 defName。
                foreach ((XmlElement _, XmlElement leaf) in Leaves(context, valueContext.Owner, valueContext.Label, valueContext.Part))
                {
                    if (context.Tables.RenamedDef(valueContext.DefRoot!, leaf.InnerText) is { } renamed)
                    {
                        plan.SetText(leaf, renamed);
                    }
                }
            }
        }
    }

    private static void PlanDefaults(UpgradeContext context, EditPlan plan, Dictionary<XmlElement, HashSet<string>> created)
    {
        foreach (DefaultRow row in context.Tables.DefaultRows)
        {
            foreach (XmlElement owner in row.Owner.Candidates(context))
            {
                if (!context.IsLive(owner) || owner[row.Label] is not null
                    || (created.TryGetValue(owner, out HashSet<string>? names) && names.Contains(row.Label))
                    || !HasEvidence(context, owner, row))
                {
                    continue;
                }

                plan.Append(owner, row.Label, row.OldDefault);
                Created(created, owner).Add(row.Label);
            }
        }
    }

    private static bool HasEvidence(UpgradeContext context, XmlElement owner, DefaultRow row)
    {
        if (row.Evidence.HasFlag(LegacyEvidence.LegacyClass) && context.OriginalClassOf(owner) is { } original && context.Tables.FindType(original) is not null)
        {
            return true;
        }

        if (row.Evidence.HasFlag(LegacyEvidence.LegacyDef))
        {
            if (context.Index.TryGetThing(owner, out IndexedThing thing) && context.Tables.FindDef("ThingDef", thing.Def) is not null)
            {
                return true;
            }

            if (context.Index.TryGetHediffDef(owner, out string? hediffDef) && context.Tables.FindDef("HediffDef", hediffDef) is not null)
            {
                return true;
            }
        }

        return row.Evidence.HasFlag(LegacyEvidence.LegacyLabel) && row.EvidenceLabels.Any(label => owner[label] is not null);
    }

    private static IEnumerable<(XmlElement Label, XmlElement Leaf)> Leaves(UpgradeContext context, OwnerSelector owner, string label, ValuePart part)
    {
        foreach (XmlElement element in context.Index.ElementsNamed(label))
        {
            if (!context.IsLive(element) || element.ParentNode is not XmlElement parent || !owner.Matches(parent, context))
            {
                continue;
            }

            foreach (XmlElement leaf in ValueLeaves.Of(element, part))
            {
                yield return (element, leaf);
            }
        }
    }

    private static string Member(UpgradeContext context, string enumName, string member) =>
        context.Tables.RenamedEnumMember(enumName, member) ?? member;

    private static HashSet<string> Created(Dictionary<XmlElement, HashSet<string>> created, XmlElement owner)
    {
        if (!created.TryGetValue(owner, out HashSet<string>? names))
        {
            names = new HashSet<string>(StringComparer.Ordinal);
            created.Add(owner, names);
        }

        return names;
    }
}
