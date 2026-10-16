using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 类型表：改写每个 <c>Class</c> 属性（含泛型参数）与登记的类型值上下文；删除的类型按策略移除或置空其元素。
/// </summary>
/// <remarks>
/// 删除的 LordJob 类型不能置空：<c>Lord.ExposeData_StateGraph</c> 在 PostLoadInit 直接解引用 <c>lordJob</c>，置空会抛出空引用，
/// Lord 留在管理器中继续出错。所以 <c>lords/li/lordJob</c> 的类型被删除时删除整个 Lord（对它的引用由加载 ID 步骤置空）。
/// </remarks>
internal sealed class TypeTableStep : DomUpgradeStep
{
    internal const string StepId = "Framework.Types";

    internal TypeTableStep()
        : base(StepId, "Rename or drop legacy type names in Class attributes and type-valued contexts.")
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        UpgradeTables tables = context.Tables;
        if (tables.TypeRows.Count == 0)
        {
            return;
        }

        foreach ((string original, List<XmlElement> elements) in context.Index.ClassGroups)
        {
            string? rewritten = SaveTypeNames.Rewrite(original, name => tables.FindType(name)?.NewSaveName);
            TypeRow? dropped = rewritten is null && tables.FindType(original) is { New: null } row ? row : null;
            if (rewritten is null && dropped is null)
            {
                continue;
            }

            foreach (XmlElement element in elements)
            {
                if (!context.IsLive(element))
                {
                    continue;
                }

                if (rewritten is not null)
                {
                    plan.LegacyClasses[element] = element.GetAttribute("Class");
                    plan.SetAttribute(element, "Class", rewritten);
                }
                else if (element.Name == "lordJob" && element.ParentNode is XmlElement lord && context.Index.IsLord(lord))
                {
                    plan.RemoveItem(lord, NullForm.Value);
                }
                else if (dropped!.Policy == TypeDropPolicy.NullOut)
                {
                    plan.NullOut(element, NullForm.Value);
                }
                else
                {
                    plan.RemoveItem(element, NullForm.Value);
                }
            }
        }

        foreach (ValueContext valueContext in tables.ValueContexts)
        {
            if (valueContext.Kind != ValueContextKind.Type)
            {
                continue;
            }

            foreach (XmlElement label in context.Index.ElementsNamed(valueContext.Label))
            {
                if (!context.IsLive(label) || label.ParentNode is not XmlElement owner || !valueContext.Owner.Matches(owner, context))
                {
                    continue;
                }

                foreach (XmlElement leaf in ValueLeaves.Of(label, valueContext.Part))
                {
                    if (SaveTypeNames.Rewrite(leaf.InnerText, name => tables.FindType(name)?.NewSaveName) is { } renamed)
                    {
                        plan.SetText(leaf, renamed);
                    }
                    else if (tables.FindType(leaf.InnerText) is { New: null })
                    {
                        ValueLeaves.Remove(plan, label, leaf, NullForm.Value);
                    }
                }
            }
        }
    }
}
