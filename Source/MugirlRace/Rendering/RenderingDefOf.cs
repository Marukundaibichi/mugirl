namespace Mugirl.Rendering;

// 渲染层使用的 Def：原生着色器材质与需要补刷图形的 DLC Hediff。
[DefOf]
internal static class RenderingDefOf
{
    [MayRequireAnomaly]
    public static HediffDef? Ghoul;

    public static MaterialDef Mugirl_ExtraOutline = null!;

    public static MaterialDef Mugirl_LanceMotionBlur = null!;

    static RenderingDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(RenderingDefOf));
}
