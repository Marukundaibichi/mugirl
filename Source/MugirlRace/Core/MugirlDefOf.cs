namespace Mugirl.Core;

// 种族本体与内容共享目录（特性、生长阶段、污物等）中的雪牛娘 Def。
[DefOf]
internal static class MugirlDefOf
{
    // 雪牛娘的青少年生命阶段（1.7/Defs/LifeStageDefs/LifeStages.xml），身形按成年女性处理。
    public static LifeStageDef Mugirl_Teenager = null!;

    public static ThingDef Mugirl = null!;

    public static BodyDef MugirlBody = null!;

    public static RimWorld.BackstoryDef Mugirl_ExperimentalChild = null!;

    static MugirlDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(MugirlDefOf));
}
