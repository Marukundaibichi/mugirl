namespace Mugirl.SaveUpgrade;

/// <summary>未完成物品使用自己的 ThingDef；按明确的旧配方识别未完成肩甲，保持其他未完成工作。</summary>
internal sealed class CoreRetiredShoulderWorkStep() : DomUpgradeStep(
    "Core.RetiredShoulderWork", "Remove unfinished products of the retired shoulder recipe with their references.",
    "def:RecipeDef/Make_Mugirl_NeuralCombatShoulders")
{
    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (IndexedThing thing in context.Index.Things)
        {
            if (context.IsLive(thing.Element) && thing.Element["recipe"]?.InnerText == "Make_Mugirl_NeuralCombatShoulders")
            {
                plan.RemoveItem(thing.Element, NullForm.Value);
            }
        }
    }
}
