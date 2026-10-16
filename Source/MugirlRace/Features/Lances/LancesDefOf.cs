namespace Mugirl.Features.Lances;

// 骑枪冲锋功能的 Def。
[DefOf]
internal static class LancesDefOf
{
    public static JobDef Mugirl_LancePointCharge = null!;

    public static JobDef Mugirl_LanceLineCharge = null!;

    public static ThingDef Mugirl_LanceChargeFlyer = null!;

    public static FleckDef ShockwaveFast = null!;

    static LancesDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(LancesDefOf));
}
