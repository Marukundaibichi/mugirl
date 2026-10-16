namespace Mugirl.Rendering;

/// <summary>
/// 经 Verse 的地图空间绘制。凡是使用 Verse 网格（<see cref="MeshPool"/>、<see cref="Graphic"/> 的网格）与 Verse 材质
/// （<see cref="MaterialPool"/>、<see cref="SolidColorMaterials"/>、<c>MaterialDef.Material</c>、<c>ShaderDef.Shader</c>）的绘制都默认走这里。
/// 替代 <c>UnityEngine.Graphics.DrawMesh</c>：<see cref="GenDraw.DrawMeshNowOrLater(Mesh, UMatrix4x4, Material, bool, MaterialPropertyBlock)"/>
/// 排入的正是同一个绘制。
/// </summary>
/// <remarks>
/// <para>
/// 在主线程的绘制阶段调用（<c>DrawAt</c>、<c>PostDraw</c>、<c>DrawPhase.Draw</c> 的 <c>DynamicDrawPhaseAt</c>、<c>Graphic_Fleck.DrawFleck</c>、
/// <c>MapComponentUpdate</c>）。Pawn 渲染节点的 worker 运行在工作线程上，必须使用节点自己的属性块，不能用 <see cref="SharedBlock"/>。
/// </para>
/// <para>
/// 镜像：不要传负缩放。Waylith 以固定绕序剔除背面，镜像矩阵可能使四边形不可见；改画 <see cref="MeshPool.plane10Flip"/>
/// （或 <c>MeshPool.GridPlaneFlip</c>）。
/// </para>
/// </remarks>
[StaticConstructorOnStartup]
public static class VfxDraw
{
    // StaticCacheLifecycle: 进程级复用的属性块，每次取用前 Clear，不保存任何跨帧或跨存档状态。
    private static readonly MaterialPropertyBlock block = new();

    /// <summary>
    /// 清空后的共享属性块，供一次绘制使用。提交绘制时会复制其中的值，所以可以立即复用。只能在主线程使用。
    /// </summary>
    public static MaterialPropertyBlock SharedBlock()
    {
        block.Clear();
        return block;
    }

    /// <summary>把 <paramref name="mesh"/> 排入本帧绘制。替代 <c>Graphics.DrawMesh(mesh, matrix, material, 0, null, 0, properties)</c>。</summary>
    public static void Mesh(Mesh mesh, in UMatrix4x4 matrix, Material material, MaterialPropertyBlock? properties = null)
        => GenDraw.DrawMeshNowOrLater(mesh, matrix, material, drawNow: false, properties);

    /// <summary>
    /// 以位置与旋转排入绘制（缩放为 1），替代 <c>Graphics.DrawMesh(mesh, position, rotation, material, 0)</c>。
    /// 走 Verse 的同名重载，矩阵由 1.7 原版自行构造（与原版绘制逐位一致）。不支持属性块；需要时用 <see cref="UnityMatrix.TRS"/> 加
    /// <see cref="Mesh(Mesh, in UMatrix4x4, Material, MaterialPropertyBlock?)"/>。
    /// </summary>
    public static void Mesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material)
        => GenDraw.DrawMeshNowOrLater(mesh, position.ToUnity(), rotation.ToUnity(), material, drawNow: false);

    /// <summary>
    /// 排入一个平放的四边形（<see cref="MeshPool.plane10"/>，1×1 格）：中心在 <paramref name="center"/>，俯视顺时针旋转
    /// <paramref name="angleDegrees"/>，拉伸到 <paramref name="size"/> 格（x 为宽，y 为纵深）。高度取 <paramref name="center"/>.Y。
    /// </summary>
    public static void Plane(Vector3 center, float angleDegrees, Vector2 size, Material material, MaterialPropertyBlock? properties = null, bool mirrored = false)
        => Mesh(mirrored ? MeshPool.plane10Flip : MeshPool.plane10, PlaneMatrix(center, angleDegrees, size), material, properties);

    /// <summary><see cref="Plane"/> 使用的变换：平移、绕 +Y 旋转、缩放 x/z。</summary>
    public static UMatrix4x4 PlaneMatrix(Vector3 center, float angleDegrees, Vector2 size)
        => UnityMatrix.TRS(center, UnityQuaternion.AngleAxisY(angleDegrees), new Vector3(size.X, 1f, size.Y));
}
