using System.Xml.Linq;

namespace Mugirl.GameValidation.Core;

/// <summary>K1 中保存字符串或枚举的位置；字典键和值必须分别匹配，不能把同名文本跨字段解释。</summary>
internal sealed record SaveValueSite(string Name, SaveOwnerSelector Owner, string Label, string Kind, string? DefRoot, string Part, bool Flags)
{
    public bool Matches(XElement leaf, ISaveOwnerContext context, SaveNameMap map)
    {
        if (leaf.HasElements || leaf.Attribute("IsNull")?.Value == "True")
        {
            return false;
        }

        XElement? label = Part switch
        {
            "keys" when leaf.Name.LocalName == "li" && leaf.Parent?.Name.LocalName == "keys" => leaf.Parent.Parent,
            "values" when leaf.Name.LocalName == "li" && leaf.Parent?.Name.LocalName == "values" => leaf.Parent.Parent,
            "items" => leaf.Name.LocalName == "li" ? leaf.Parent : leaf,
            _ => null,
        };
        if (label?.Parent is not { } owner || !Owner.Matches(owner, context))
        {
            return false;
        }

        return label.Name.LocalName == Label || map.LabelFate(owner, Label, context).NewLabel == label.Name.LocalName;
    }
}
