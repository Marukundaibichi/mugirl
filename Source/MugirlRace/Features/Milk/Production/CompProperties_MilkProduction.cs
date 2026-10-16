namespace Mugirl.Features.Milk;

// 可产奶组件的 XML 配置。
public class CompProperties_MilkProduction : CompProperties
{
    // 显示名可能来自 XML 实际文本，也可能使用默认 Keyed。
    public string displayString = "Mugirl.Milk.FullnessDisplay";

    public float milkAmount = 1f;

    public bool milkFemaleOnly = true;

    public ThingDef? milkDef;

    public float milkIntervalDays;

    public CompProperties_MilkProduction()
    {
        compClass = typeof(CompMilkProduction);
    }
}
