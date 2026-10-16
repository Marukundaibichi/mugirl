using Mugirl.Platform.Rendering;

namespace Mugirl.Rendering;

/// <summary>
/// 用原生着色器 Mugirl_ExtraOutline 为一个人物轮廓图层画额外描边。地图上延迟绘制，人物图集与肖像烘焙时立即绘制
/// （与原版图层同一条 GenDraw 路径，见 <see cref="NativeShaderPass"/>）。
/// </summary>
internal sealed class ExtraOutlinePass
{
    private const string ShaderName = "Mugirl_ExtraOutline";

    // StaticCacheLifecycle: 进程级不变的属性 id，首次绘制时注册（开发者模式下核对着色器声明），永不清理。
    private static readonly int MainTex = ShaderIds.For(ShaderName, "_MainTex");
    private static readonly int SrcScaleOffset = ShaderIds.For(ShaderName, "_MugirlSrcScaleOffset");
    private static readonly int PaddingU = ShaderIds.For(ShaderName, "_MugirlPaddingU");
    private static readonly int PaddingV = ShaderIds.For(ShaderName, "_MugirlPaddingV");
    private static readonly int UvRect = ShaderIds.For(ShaderName, "_MugirlUvRect");
    private static readonly int UvRadius = ShaderIds.For(ShaderName, "_MugirlUvRadius");
    private static readonly int Opacity = ShaderIds.For(ShaderName, "_MugirlOpacity");

    // StaticCacheLifecycle: 进程级单例，首次绘制时在主线程创建（材质只能在主线程创建）；着色器不可用时保持 null 并警告一次。
    private static ExtraOutlinePass? instance;
    private static bool resolved;

    private readonly NativeShaderPass pass;

    private ExtraOutlinePass(NativeShaderPass pass) => this.pass = pass;

    /// <summary>描边绘制器；着色器不可用时为 null（已警告一次，正常人物绘制不受影响）。</summary>
    internal static ExtraOutlinePass? Instance
    {
        get
        {
            if (!resolved)
            {
                resolved = true;
                instance = Create();
            }

            return instance;
        }
    }

    /// <summary>
    /// 为一个轮廓图层画描边。<paramref name="matrix"/> 已由调用方降到全部轮廓图层之下；<paramref name="tintAlpha"/> 是人物着色 alpha；
    /// <paramref name="width"/> 是世界单位的描边宽度。
    /// </summary>
    internal void Draw(Mesh? mesh, in UMatrix4x4 matrix, EngineMaterialView source, bool drawNow, float tintAlpha, float width)
    {
        EngineTexture texture = source.MainTexture;
        if (mesh is null || texture.IsNull || tintAlpha <= 0f
            || !PawnLayerQuadCache.TryGet(mesh, out PawnLayerQuad quad)
            || !ExtraOutlineGeometry.TryCompute(matrix, quad, source.MainTextureScaleOffset, width, out OutlineLayerParameters parameters))
        {
            return;
        }

        float opacity = UnityMathf.Clamp01(tintAlpha * source.MainColor.W);

        // 完全透明时着色器丢弃整层像素，不提交绘制。
        if (!(opacity > 0f))
        {
            return;
        }

        pass.Begin(drawNow)
            .SetTexture(MainTex, texture)
            .SetVector(SrcScaleOffset, parameters.SrcScaleOffset)
            .SetVector(PaddingU, parameters.PaddingU)
            .SetVector(PaddingV, parameters.PaddingV)
            .SetVector(UvRect, parameters.UvRect)
            .SetVector(UvRadius, parameters.UvRadius)
            .SetFloat(Opacity, opacity)
            .Draw(mesh, matrix);
    }

    private static ExtraOutlinePass? Create()
    {
        if (!NativeShaderPass.TryCreate(RenderingDefOf.Mugirl_ExtraOutline, out NativeShaderPass? pass, out string? problem))
        {
            MugirlLog.WarningOnce("ExtraOutline.Shader", () => "Mugirl.Outline.Unavailable".Translate(problem).ToString());
            return null;
        }

        return new ExtraOutlinePass(pass);
    }
}
