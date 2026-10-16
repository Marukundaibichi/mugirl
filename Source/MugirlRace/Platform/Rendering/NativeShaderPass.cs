using System.Diagnostics.CodeAnalysis;
using NativeMaterial = Waylith.Rendering.Material;
using NativePropertyBlock = Waylith.Rendering.MaterialPropertyBlock;
using NativeShader = Waylith.Rendering.Shader;
using ShaderRegistry = Waylith.Rendering.ShaderRegistry;
using WaylithGraphics = Waylith.Rendering.Graphics;

namespace Mugirl.Platform.Rendering;

/// <summary>
/// 用本模组 MaterialDef 的原生着色器逐次绘制 Verse 网格（MeshPool、Graphic 的网格），参数来自 <see cref="EngineMaterialView"/>。
/// 两条路径与 <see cref="GenDraw.DrawMeshNowOrLater(Mesh, UMatrix4x4, Material, bool, MaterialPropertyBlock)"/> 的两个分支对应：
/// <list type="bullet">
/// <item>延迟绘制（地图）：MaterialDef 的共享材质只读，逐次参数写进原生属性块，经 Waylith 提交队列排入地图通道（layer 0）。
/// 走原生而不是 VfxDraw：参数里的纹理是另一个材质背后的引擎纹理，交给 UnityStubs 属性块需要先经 <c>Material.mainTexture</c> 取回
/// <see cref="Texture"/>，那是兼容层逻辑（ADR-006）。</item>
/// <item>立即绘制（人物图集与肖像烘焙）：只能经 Verse GenDraw 的 DrawNow 分支进入 PawnCacheCamera 的立即捕获，而它不接受属性块。
/// 参数写进本对象私有的材质副本（<see cref="MaterialDef.CreateMaterialInstance"/>），UnityStubs 的 DrawMeshNow 在每次调用时快照材质属性，
/// 所以逐次改写是安全的；共享材质从不修改。</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>用法：<c>Begin(drawNow).SetX(...)...Draw(mesh, matrix)</c>。只在主线程使用（绘制阶段与 PawnCacheRenderer 的烘焙都在主线程）。</para>
/// <para>立即绘制的材质会保留上一次的值：每次绘制都必须设置同一组完整参数（纹理可以设为空句柄，表示使用着色器默认值）。</para>
/// </remarks>
public sealed class NativeShaderPass
{
    private const int MapLayer = 0;

    private readonly MaterialDef materialDef;
    private readonly NativeMaterial deferredMaterial;
    private readonly NativePropertyBlock properties = new();
    private BakeMaterial? bake;
    private bool bakeUnavailable;
    private BakeMaterial? active;
    private bool drawNow;

    private NativeShaderPass(MaterialDef materialDef, NativeMaterial deferredMaterial)
    {
        this.materialDef = materialDef;
        this.deferredMaterial = deferredMaterial;
    }

    /// <summary>
    /// 为 <paramref name="materialDef"/> 创建绘制器。着色器未注册、有布局或阶段接口错误（游戏会用回退着色器绘制）或材质无法创建时
    /// 返回 false，并给出原因。必须在主线程、Def 加载之后调用（MaterialAllocator 拒绝其他线程）。
    /// </summary>
    public static bool TryCreate(MaterialDef? materialDef, [NotNullWhen(true)] out NativeShaderPass? pass, [NotNullWhen(false)] out string? problem)
    {
        pass = null;
        string? shaderName = materialDef?.shader?.defName;
        if (materialDef is null || shaderName is null)
        {
            problem = $"MaterialDef {materialDef?.defName ?? "(null)"} has no ShaderDef.";
            return false;
        }

        if (!ShaderRegistry.TryResolve(shaderName, out NativeShader? shader))
        {
            problem = $"ShaderDef {shaderName} is not registered with the engine.";
            return false;
        }

        if (shader.HasErrors)
        {
            problem = $"ShaderDef {shaderName} has layout or stage-interface errors; the engine would draw the fallback shader.";
            return false;
        }

        Material? shared = materialDef.Material;
        if (shared is null)
        {
            problem = $"MaterialDef {materialDef.defName} could not create its material.";
            return false;
        }

        pass = new NativeShaderPass(materialDef, NativeBridge.Of(shared));
        problem = null;
        return true;
    }

    /// <summary>开始一次绘制。<paramref name="drawNow"/> 取自 <c>PawnDrawParms.DrawNow</c>（烘焙时为 true）。</summary>
    public NativeShaderPass Begin(bool drawNow)
    {
        this.drawNow = drawNow;
        properties.Clear();
        active = drawNow ? GetBakeMaterial() : null;
        return this;
    }

    /// <summary>设置 float 参数。</summary>
    public NativeShaderPass SetFloat(int propertyId, float value)
    {
        if (active is { } target)
        {
            target.Engine.SetFloat(propertyId, value);
        }
        else
        {
            properties.SetFloat(propertyId, value);
        }

        return this;
    }

    /// <summary>设置向量或颜色参数（原样传给着色器，不做 *_ST 重排）。</summary>
    public NativeShaderPass SetVector(int propertyId, Vector4 value)
    {
        if (active is { } target)
        {
            target.Engine.SetVector(propertyId, value);
        }
        else
        {
            properties.SetVector(propertyId, value);
        }

        return this;
    }

    /// <summary>设置纹理参数；空句柄表示使用着色器声明的默认纹理。</summary>
    public NativeShaderPass SetTexture(int propertyId, EngineTexture texture)
    {
        if (active is { } target)
        {
            target.Engine.SetTexture(propertyId, texture.Native);
        }
        else
        {
            properties.SetTexture(propertyId, texture.Native);
        }

        return this;
    }

    /// <summary>
    /// 按 <see cref="Begin"/> 选定的路径绘制 <paramref name="mesh"/>。延迟绘制在提交时复制参数，绘制器可以立即用于下一次绘制。
    /// </summary>
    /// <param name="mesh">Verse 网格。</param>
    /// <param name="matrix">物体到世界（或烘焙相机）的变换，Unity 布局。</param>
    public void Draw(Mesh mesh, in UMatrix4x4 matrix)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (!drawNow)
        {
            WaylithGraphics.DrawMesh(NativeBridge.Of(mesh), matrix.ToWaylithLayout(), deferredMaterial, properties, MapLayer);
        }
        else if (active is { } target)
        {
            GenDraw.DrawMeshNowOrLater(mesh, matrix, target.Material, drawNow: true);
        }
    }

    // 首次烘焙时创建私有副本；创建失败（不在主线程时 MaterialAllocator 返回 null）后不再重试，烘焙中不画这一层。
    private BakeMaterial? GetBakeMaterial()
    {
        if (bake is null && !bakeUnavailable)
        {
            Material? instance = materialDef.CreateMaterialInstance();
            if (instance is null)
            {
                bakeUnavailable = true;
            }
            else
            {
                bake = new BakeMaterial(instance, NativeBridge.Of(instance));
            }
        }

        return bake;
    }

    // 烘焙用的私有材质副本及其引擎材质。
    private sealed class BakeMaterial(Material material, NativeMaterial engine)
    {
        public Material Material { get; } = material;

        public NativeMaterial Engine { get; } = engine;
    }
}
