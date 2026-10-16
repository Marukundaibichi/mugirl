namespace Mugirl.Features.Mounting;

// 骑乘功能的 Def。
[DefOf]
internal static class MountingDefOf
{
    public static JobDef Mugirl_Mount = null!;

    public static JobDef Mugirl_Dismount = null!;

    static MountingDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(MountingDefOf));
}
