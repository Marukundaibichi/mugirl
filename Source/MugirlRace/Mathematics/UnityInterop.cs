using System.Runtime.CompilerServices;

namespace Mugirl.Mathematics;

/// <summary>
/// RimWorld API 边界类型（<c>UnityEngine.*</c>，1.7 中由 UnityStubs 提供）与模组内部 <c>System.Numerics</c> 类型之间的转换。
/// 每个转换只读取公开字段或调用边界类型的字段初始化构造函数，不会执行 UnityStubs 的逻辑。
/// </summary>
/// <remarks>
/// 值进入模组时（游戏属性如 <c>Thing.DrawPos</c>、重写方法的参数）转换一次，离开时（交给 <c>FleckMaker</c>、<c>GenDraw</c> 的参数、
/// 重写方法的返回值）再转换一次。用 <c>Scribe_Values</c> 保存的字段保持 <c>UnityEngine.Vector3</c>：Verse 只会读写这个类型。
/// </remarks>
public static class UnityInterop
{
    /// <summary>读取 <c>x, y, z</c>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 ToNumerics(this UVector3 v) => new(v.x, v.y, v.z);

    /// <summary>用 <c>X, Y, Z</c> 构造边界向量。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UVector3 ToUnity(this Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>读取 <c>x, y</c>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 ToNumerics(this UVector2 v) => new(v.x, v.y);

    /// <summary>用 <c>X, Y</c> 构造边界向量。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UVector2 ToUnity(this Vector2 v) => new(v.X, v.Y);

    /// <summary>读取 <c>x, y, z, w</c>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 ToNumerics(this UVector4 v) => new(v.x, v.y, v.z, v.w);

    /// <summary>用 <c>X, Y, Z, W</c> 构造边界向量。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UVector4 ToUnity(this Vector4 v) => new(v.X, v.Y, v.Z, v.W);

    /// <summary>读取 <c>x, y, z, w</c>（两个库的分量顺序相同）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion ToNumerics(this UQuaternion q) => new(q.x, q.y, q.z, q.w);

    /// <summary>用 <c>X, Y, Z, W</c> 构造边界四元数。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UQuaternion ToUnity(this Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    /// <summary>把 <c>r, g, b, a</c> 读入 <c>X, Y, Z, W</c>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 ToNumerics(this Color c) => new(c.r, c.g, c.b, c.a);

    /// <summary>以 <c>X, Y, Z, W</c> 作为 <c>r, g, b, a</c> 构造边界颜色。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color ToUnityColor(this Vector4 v) => new(v.X, v.Y, v.Z, v.W);

    /// <summary>边界向量在 XZ 平面上的投影（丢弃 Y），RimWorld 平面几何常用。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 ToNumericsXZ(this UVector3 v) => new(v.x, v.z);

    /// <summary>
    /// 三维向量补 <c>w</c> 后的四维边界向量，替代 Unity 的隐式 <c>Vector3 → Vector4</c>（<c>w</c> 默认 0，与 Unity 相同）。
    /// 常用于 <c>MaterialPropertyBlock.SetVector</c>。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UVector4 ToUnity4(this Vector3 v, float w = 0f) => new(v.X, v.Y, v.Z, w);
}
