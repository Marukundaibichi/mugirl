namespace Mugirl.Features.Social;

// 社交（蹭蹭、进食心情）功能的 Def。
[DefOf]
internal static class SocialDefOf
{
    public static JobDef Mugirl_Nuzzle = null!;

    [DefAlias("Mugirl_Nuzzle")]
    public static InteractionDef Mugirl_NuzzleInteraction = null!;

    public static ThoughtDef Mugirl_Nuzzled = null!;

    static SocialDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(SocialDefOf));
}
