using Mugirl.Platform.Rendering;

namespace Mugirl.Rendering;

/// <summary>
/// 用原生着色器 Mugirl_LanceMotionBlur 为一个人物轮廓图层画运动拖尾。只在地图上延迟绘制。
/// </summary>
internal sealed class LanceMotionBlurPass
{
    private const string ShaderName = "Mugirl_LanceMotionBlur";

    // 将绘制矩阵的 world.y 平移降低 0.005；透明排序键随之降低，拖尾排在本图层之后。
    private const float TrailDepthOffset = 0.005f;

    // StaticCacheLifecycle: 进程级不变的属性 id，首次绘制时注册（开发者模式下核对着色器声明），永不清理。
    private static readonly int MainTex = ShaderIds.For(ShaderName, "_MainTex");
    private static readonly int MaskTex = ShaderIds.For(ShaderName, "_MaskTex");
    private static readonly int ColorId = ShaderIds.For(ShaderName, "_Color");
    private static readonly int ColorTwo = ShaderIds.For(ShaderName, "_ColorTwo");
    private static readonly int SrcScaleOffset = ShaderIds.For(ShaderName, "_MugirlSrcScaleOffset");
    private static readonly int UvRect = ShaderIds.For(ShaderName, "_MugirlUvRect");
    private static readonly int BlurUv = ShaderIds.For(ShaderName, "_MugirlBlurUv");
    private static readonly int Travel = ShaderIds.For(ShaderName, "_MugirlTravel");
    private static readonly int PaddingU = ShaderIds.For(ShaderName, "_MugirlPaddingU");
    private static readonly int PaddingV = ShaderIds.For(ShaderName, "_MugirlPaddingV");
    private static readonly int HasMask = ShaderIds.For(ShaderName, "_MugirlHasMask");
    private static readonly int Opacity = ShaderIds.For(ShaderName, "_MugirlOpacity");

    // StaticCacheLifecycle: 进程级单例，首次绘制时在主线程创建；着色器不可用时保持 null 并警告一次。
    private static LanceMotionBlurPass? instance;
    private static bool resolved;

    private readonly NativeShaderPass pass;

    private LanceMotionBlurPass(NativeShaderPass pass) => this.pass = pass;

    /// <summary>拖尾绘制器；着色器不可用时为 null（已警告一次）。</summary>
    internal static LanceMotionBlurPass? Instance
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
    /// 为一个图层画拖尾，画了返回 true。非轮廓图层、没有贴图、不是平面网格或在地面上的投影退化时不画。
    /// </summary>
    /// <param name="mesh">图层网格。</param>
    /// <param name="matrix">图层本帧的绘制矩阵。</param>
    /// <param name="material">图层材质。</param>
    /// <param name="travel">本帧世界位移。</param>
    /// <param name="flash">受伤闪烁色（乘到主色上）。</param>
    /// <param name="opacity">整体不透明度。</param>
    internal bool Draw(Mesh? mesh, UMatrix4x4 matrix, Material? material, Vector3 travel, Vector4 flash, float opacity)
    {
        EngineMaterialView source = EngineMaterialView.Of(material);
        EngineTexture texture = source.MainTexture;
        if (mesh is null || texture.IsNull || !PawnSilhouetteShaders.Contains(source.ShaderName)
            || !PawnLayerQuadCache.TryGet(mesh, out PawnLayerQuad quad)
            || !LanceMotionBlurGeometry.TryCompute(matrix, quad, source.MainTextureScaleOffset, travel, out BlurLayerParameters parameters))
        {
            return false;
        }

        EngineTexture mask = source.GetDeclaredTexture(MaskTex);
        Vector4 colorTwo = source.TryGetDeclaredColor(ColorTwo, out Vector4 declared) ? declared : Vector4.One;
        matrix.m13 -= TrailDepthOffset;

        // 没有遮罩时 _MaskTex 用着色器默认的黑色纹理，_MugirlHasMask 为 0 时也不会采样。
        pass.Begin(drawNow: false)
            .SetTexture(MainTex, texture)
            .SetTexture(MaskTex, mask)
            .SetFloat(HasMask, mask.IsNull ? 0f : 1f)
            .SetVector(ColorId, source.MainColor * flash)
            .SetVector(ColorTwo, colorTwo)
            .SetVector(SrcScaleOffset, parameters.SrcScaleOffset)
            .SetVector(UvRect, parameters.UvRect)
            .SetVector(BlurUv, parameters.BlurUv)
            .SetVector(Travel, parameters.Travel)
            .SetVector(PaddingU, parameters.PaddingU)
            .SetVector(PaddingV, parameters.PaddingV)
            .SetFloat(Opacity, opacity)
            .Draw(mesh, matrix);
        return true;
    }

    private static LanceMotionBlurPass? Create()
    {
        if (!NativeShaderPass.TryCreate(RenderingDefOf.Mugirl_LanceMotionBlur, out NativeShaderPass? pass, out string? problem))
        {
            MugirlLog.WarningOnce("Lance.MotionBlur", () => "Mugirl.Lance.MotionBlur.Unavailable".Translate(problem).ToString());
            return null;
        }

        return new LanceMotionBlurPass(pass);
    }
}
