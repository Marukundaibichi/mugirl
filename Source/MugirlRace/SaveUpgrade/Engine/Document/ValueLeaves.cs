using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>标签元素中保存值的叶元素（见 <see cref="ValuePart"/>）；置空的项（<c>IsNull="True"</c>）与深层项不算值。</summary>
internal static class ValueLeaves
{
    internal static IEnumerable<XmlElement> Of(XmlElement label, ValuePart part)
    {
        ArgumentNullException.ThrowIfNull(label);
        XmlElement? list = part switch
        {
            ValuePart.Keys => label["keys"],
            ValuePart.Values => label["values"],
            _ => label,
        };

        if (list is null)
        {
            yield break;
        }

        if (part == ValuePart.Items && !DomEditor.HasElementChild(list))
        {
            if (list.GetAttribute("IsNull") != "True")
            {
                yield return list;
            }

            yield break;
        }

        for (XmlNode? node = list.FirstChild; node is not null; node = node.NextSibling)
        {
            if (node is XmlElement { Name: "li" } item && !DomEditor.HasElementChild(item) && item.GetAttribute("IsNull") != "True")
            {
                yield return item;
            }
        }
    }

    /// <summary>删除标签中的一个值：单值标签整个删除（读档时回到默认值），列表项与字典项按容器规则删除或置空。</summary>
    /// <param name="leafForm">列表项只能置空时的写法（见 <see cref="NullForm"/>）。</param>
    internal static void Remove(EditPlan plan, XmlElement label, XmlElement leaf, NullForm leafForm)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (leaf == label)
        {
            plan.Remove(label);
        }
        else
        {
            plan.RemoveItem(leaf, leafForm);
        }
    }
}
