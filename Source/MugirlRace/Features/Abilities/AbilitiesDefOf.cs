namespace Mugirl.Features.Abilities;

// 冲撞、投掷、扣篮等能力的 Def。
[DefOf]
internal static class AbilitiesDefOf
{
    // 原版 Core 的跳跃起跳与落地音效（Data/Core/Defs/SoundDefs/World_Oneshots_Misc.xml）。
    public static SoundDef Longjump_Jump = null!;

    public static SoundDef Longjump_Land = null!;

    [MayRequire("Ludeon.RimWorld.Biotech")]
    public static HediffDef? Mugirl_Stun;

    [MayRequire("Ludeon.RimWorld.Biotech")]
    public static HediffDef? Mugirl_Charge;

    [MayRequire("Ludeon.RimWorld.Biotech")]
    public static ThingDef? Mugirl_ThrowController;

    [MayRequire("Ludeon.RimWorld.Biotech")]
    public static DamageDef? Mugirl_ThrownImpact;

    [MayRequire("Ludeon.RimWorld.Biotech")]
    public static ThingDef? Mugirl_DunkProp;

    [MayRequire("Ludeon.RimWorld.Biotech")]
    public static ThingDef? Mugirl_DunkHead;

    [MayRequire("Ludeon.RimWorld.Biotech")]
    public static ThingDef? Mugirl_DunkFlyer;

    [MayRequire("Ludeon.RimWorld.Biotech")]
    public static FleckDef? Mugirl_DunkBloodSplash;

    public static SoundDef Pawn_Melee_BigBash_HitPawn = null!;

    static AbilitiesDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(AbilitiesDefOf));
}
