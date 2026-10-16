namespace Mugirl.Features.Restraints;

// 束具功能的 Def。
[DefOf]
internal static class RestraintsDefOf
{
    // 高科技束具引用。
    public static ThingDef Mugirl_ShockCollar = null!;

    // 快递事件战利品使用既有钥匙 Def，不改 defName 以保护数值与掉落配置。
    public static ThingDef Mugirl_SlaveApparelKey_Medieval = null!;

    public static ThingDef Mugirl_SlaveApparelKey_Industrial = null!;

    // 强制解锁奴隶束具的 Job（1.6 中经 MugirlOptionalDefs 按名查找）。
    public static JobDef Mugirl_ForceUnlockRestraint = null!;

    public static TraitDef Mugirl_BrainwashObey = null!;

    static RestraintsDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(RestraintsDefOf));
}
