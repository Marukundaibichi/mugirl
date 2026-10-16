using NativeMaterial = Waylith.Rendering.Material;
using NativeShader = Waylith.Rendering.Shader;
using ShaderPropertyId = Waylith.Rendering.ShaderPropertyId;

namespace Mugirl.Platform.Rendering;

/// <summary>
/// Verse 材质背后引擎材质的只读视图：着色器名、纹理、纹理缩放/偏移与颜色。替代 UnityStubs 的
/// <c>material.shader/mainTexture/mainTextureScale/mainTextureOffset/color/GetColor/HasProperty</c>，读到的就是 GPU 实际使用的数据，
/// 对 DamageFlasher、隐身等由 <c>new Material(source)</c> 复制出的材质同样有效（<c>MaterialPool.TryGetRequestForMat</c> 对它们无效）。
/// </summary>
/// <remarks>只在主线程使用。视图不复制数据，每次读取都是材质的当前值。</remarks>
public readonly struct EngineMaterialView
{
    // StaticCacheLifecycle: 进程级不变的属性 id，首次使用时注册，永不清理。
    private static readonly int ColorId = ShaderPropertyId.GetOrCreate("_Color");

    private readonly NativeMaterial? material;

    private EngineMaterialView(NativeMaterial? material) => this.material = material;

    /// <summary>引擎着色器名，即 ShaderDef 的 defName（例如 "Cutout"）；无材质时为空字符串。</summary>
    public string ShaderName => material?.Shader.Name ?? string.Empty;

    /// <summary><c>_MainTex</c>。</summary>
    public EngineTexture MainTexture => new(material?.MainTexture);

    /// <summary>
    /// <c>_MainTex</c> 的缩放（xy）与偏移（zw），遵循 Unity 的 <c>mainTextureScale</c>、<c>mainTextureOffset</c> 顺序。
    /// </summary>
    public Vector4 MainTextureScaleOffset
    {
        get
        {
            if (material is null)
            {
                return new Vector4(1f, 1f, 0f, 0f);
            }

            Vector2 scale = material.MainTextureScale;
            Vector2 offset = material.MainTextureOffset;
            return new Vector4(scale.X, scale.Y, offset.X, offset.Y);
        }
    }

    /// <summary>
    /// 主色 <c>_Color</c>（rgba），与 UnityStubs/Unity 的 <c>material.color</c> 相同：材质上的值，否则着色器声明的默认值，否则白色。
    /// </summary>
    public Vector4 MainColor
    {
        get
        {
            if (material is null)
            {
                return Vector4.One;
            }

            if (TryGetSetValue(material, ColorId, out Vector4 value))
            {
                return value;
            }

            return TryGetAuthoredDefault(material.Shader, ColorId, out value) ? value : Vector4.One;
        }
    }

    /// <summary>取 Verse 材质背后引擎材质的视图；<paramref name="material"/> 为 null 时返回无效视图。</summary>
    public static EngineMaterialView Of(Material? material) => new(material is null ? null : NativeBridge.Of(material));

    /// <summary>
    /// 着色器声明的纹理：声明且材质设置了时返回它，否则返回空句柄。同 Verse <c>MaterialUtility.GetMaskTexture</c> 的判断。
    /// </summary>
    public EngineTexture GetDeclaredTexture(int propertyId)
        => new(material is not null && material.Shader.HasProperty(propertyId) ? material.GetTexture(propertyId) : null);

    /// <summary>
    /// 着色器声明的颜色：返回 GPU 绘制源图层时使用的值（材质上的值，否则着色器声明的默认值，否则 0）。着色器没有声明时返回
    /// <see langword="false"/>，调用方自行回退（Verse <c>MaterialUtility.GetColorTwo</c> 回退为白色）。
    /// </summary>
    public bool TryGetDeclaredColor(int propertyId, out Vector4 value)
    {
        if (material is null || !material.Shader.HasProperty(propertyId))
        {
            value = default;
            return false;
        }

        if (!TryGetSetValue(material, propertyId, out value) && !TryGetAuthoredDefault(material.Shader, propertyId, out value))
        {
            value = Vector4.Zero;
        }

        return true;
    }

    // UnityStubs Material.TryGetVectorOrColor 的顺序：先向量，后颜色。
    private static bool TryGetSetValue(NativeMaterial material, int propertyId, out Vector4 value)
        => material.TryGetVector(propertyId, out value) || material.TryGetColor(propertyId, out value);

    private static bool TryGetAuthoredDefault(NativeShader shader, int propertyId, out Vector4 value)
    {
        foreach (Waylith.Rendering.MaterialUniformSlot slot in shader.MaterialLayout.UniformSlots)
        {
            if (slot.PropertyId == propertyId && slot.HasAuthoredDefault)
            {
                value = slot.DefaultValue.Vector4;
                return true;
            }
        }

        value = default;
        return false;
    }
}
