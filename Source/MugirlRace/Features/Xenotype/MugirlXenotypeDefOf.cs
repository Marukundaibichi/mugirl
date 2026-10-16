namespace Mugirl.Features.Xenotype;

// 异种功能的 Def（类名带 Mugirl：RimWorld.XenotypeDefOf 已存在）。
[DefOf]
internal static class MugirlXenotypeDefOf
{
    [MayRequire("Ludeon.RimWorld.Biotech")]
    public static XenotypeDef? Mugirl_Xenotype;

    static MugirlXenotypeDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(MugirlXenotypeDefOf));
}
