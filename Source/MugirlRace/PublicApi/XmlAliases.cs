namespace Mugirl;

// 公共 API：其他 Mod 的 XML 以这些旧类名引用本 Mod 的类（KindSeal.LOL 的商人库存、Mugirl Race Subspecies 与白绒物语的种族组件）。
// 实现位于各功能的命名空间；这里只保留名字，字段名由基类保持不变。新增或改名前先看 tools/RenameApply/specs/public-api.json。

/// <summary>旧类名 <c>Mugirl.StockGenerator_Mugirl_Slaves</c>；字段 <c>slaveCountRange</c>、<c>slaveKindDef</c> 不变。</summary>
public class StockGenerator_Mugirl_Slaves : Features.Trade.StockGenerator_MugirlSlaves
{
}

/// <summary>旧类名 <c>Mugirl.CompProperties_MugirlMount</c>。</summary>
public class CompProperties_MugirlMount : Features.Mounting.CompProperties_Mount
{
}

/// <summary>旧类名 <c>Mugirl.CompProperties_MooMilkable</c>；字段 <c>displayString</c>、<c>milkDef</c>、<c>milkIntervalDays</c>、<c>milkAmount</c> 不变。</summary>
public class CompProperties_MooMilkable : Features.Milk.CompProperties_MilkProduction
{
}
