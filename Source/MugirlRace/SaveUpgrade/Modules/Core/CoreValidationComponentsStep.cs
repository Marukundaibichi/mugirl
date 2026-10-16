using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>删除验证构建误写到游戏组件列表中的驱动；不扫描或修改其他类型位置。</summary>
internal sealed class CoreValidationComponentsStep() : DomUpgradeStep(
    "Core.ValidationComponents", "Remove the two validation-only game components before deserialization.",
    "type:Mugirl.CorporateRuntimeValidation", "type:Mugirl.CorporateVisualValidation")
{
    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        if (context.Document.Game["components"] is not { } components)
        {
            return;
        }

        foreach (XmlNode node in components.ChildNodes)
        {
            if (node is XmlElement { Name: "li" } component
                && component.GetAttribute("Class") is "Mugirl.CorporateRuntimeValidation" or "Mugirl.CorporateVisualValidation")
            {
                plan.Remove(component);
            }
        }
    }
}
