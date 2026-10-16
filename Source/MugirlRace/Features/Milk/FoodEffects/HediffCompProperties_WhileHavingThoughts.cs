namespace Mugirl.Features.Milk;

// 根据记忆 Thought 维持或移除自身 Hediff 的配置。
internal class HediffCompProperties_WhileHavingThoughts : HediffCompProperties
{
    public List<ThoughtDef?> thoughtDefs = [];

    public List<ThoughtDef?> removeThoughtDefs = [];

    // 创建时削弱的另一个 Hediff（XML 写 defName，由原版交叉引用解析）；当前 XML 未使用。
    public HediffDef? hediffReduction;

    public float reductionAmount;

    public HediffCompProperties_WhileHavingThoughts()
    {
        compClass = typeof(HediffComp_WhileHavingThoughts);
    }
}
