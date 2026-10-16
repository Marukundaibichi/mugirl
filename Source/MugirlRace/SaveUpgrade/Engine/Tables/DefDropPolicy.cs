namespace Mugirl.SaveUpgrade;

/// <summary>已删除 Def 的依赖数据如何处理。存档中的 <c>&lt;def&gt;</c> 文本类型不明确，只处理按结构能确定类型的位置。</summary>
internal enum DefDropPolicy
{
    /// <summary>ThingDef：删除该 def 的物品元素（含嵌套），并把对其 ID 的引用置空或从可移除容器中删除。</summary>
    RemoveThings,

    /// <summary>RecipeDef：删除使用该配方的账单，并把 <c>Bill_&lt;recipe&gt;_&lt;n&gt;</c> 引用置空或删除。</summary>
    RemoveBills,

    /// <summary>HediffDef：删除 <c>hediffSet/hediffs</c> 中该 def 的健康状态。</summary>
    RemoveHediffs,

    /// <summary>其他 Def：只处理登记为该根类型的 Def 值上下文与原版过滤器中的引用。</summary>
    RemoveReferences,
}
