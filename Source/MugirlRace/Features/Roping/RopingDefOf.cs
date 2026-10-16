namespace Mugirl.Features.Roping;

// 牵绳功能的 Def。
[DefOf]
internal static class RopingDefOf
{
    public static ThingDef Mugirl_WallRopeHitch = null!;

    public static JobDef Mugirl_Rope = null!;

    public static JobDef Mugirl_Unrope = null!;

    public static JobDef Mugirl_RopeToHitch = null!;

    public static JobDef Mugirl_FollowRoper = null!;

    static RopingDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(RopingDefOf));
}
