using System.Runtime.CompilerServices;

namespace Mugirl.Mathematics;

/// <summary>
/// Unity 语义的颜色工具，只通过字段和构造函数操作边界类型 <c>UnityEngine.Color</c>。模组内颜色保持 <c>UnityEngine.Color</c>，
/// 因为几乎所有使用方（<c>Graphic.color</c>、<c>GraphicDatabase.Get</c>、<c>MaterialPropertyBlock.SetColor</c>、<c>Widgets.DrawBoxSolid</c>）都要这个类型。
/// </summary>
/// <remarks>
/// 常量取 Unity 2022.3 的精确值。UnityStubs 的 <c>Color.yellow</c> 是 (1, 0.922, 0.016, 1)，与 Unity 的
/// (1, 0.92156863, 0.015686275, 1) 不同，所以不能用 UnityStubs 的静态属性。常量是属性而非静态字段：<c>Color</c> 是结构体，
/// 每次构造开销可以忽略，也不需要 <c>[StaticConstructorOnStartup]</c>。
/// </remarks>
public static class UnityColor
{
    /// <summary>不透明白色 (1, 1, 1, 1)，替代 <c>Color.white</c>。</summary>
    public static Color White
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(1f, 1f, 1f, 1f);
    }

    /// <summary>不透明黑色 (0, 0, 0, 1)。</summary>
    public static Color Black
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(0f, 0f, 0f, 1f);
    }

    /// <summary>全透明黑色 (0, 0, 0, 0)。</summary>
    public static Color Clear
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(0f, 0f, 0f, 0f);
    }

    /// <summary>红色 (1, 0, 0, 1)。</summary>
    public static Color Red
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(1f, 0f, 0f, 1f);
    }

    /// <summary>绿色 (0, 1, 0, 1)。</summary>
    public static Color Green
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(0f, 1f, 0f, 1f);
    }

    /// <summary>蓝色 (0, 0, 1, 1)。</summary>
    public static Color Blue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(0f, 0f, 1f, 1f);
    }

    /// <summary>Unity 的黄色 (1, 47/51 = 0.92156863, 0.015686275, 1)；UnityStubs 的值不同，见类注释。</summary>
    public static Color Yellow
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(1f, 47f / 51f, 0.015686275f, 1f);
    }

    /// <summary>青色 (0, 1, 1, 1)。</summary>
    public static Color Cyan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(0f, 1f, 1f, 1f);
    }

    /// <summary>品红 (1, 0, 1, 1)。</summary>
    public static Color Magenta
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(1f, 0f, 1f, 1f);
    }

    /// <summary>灰色 (0.5, 0.5, 0.5, 1)，Unity 的 <c>gray</c>。</summary>
    public static Color Gray
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(0.5f, 0.5f, 0.5f, 1f);
    }

    /// <summary>与 <see cref="Gray"/> 相同，对应 Unity 的 <c>grey</c>。</summary>
    public static Color Grey
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(0.5f, 0.5f, 0.5f, 1f);
    }

    /// <summary>逐分量 <c>a + (b - a)·t</c>，<paramref name="t"/> 钳制到 [0, 1]（Unity <c>Color.Lerp</c>）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color Lerp(Color a, Color b, float t) => LerpUnclamped(a, b, UnityMathf.Clamp01(t));

    /// <summary>不钳制的逐分量插值（Unity <c>Color.LerpUnclamped</c>）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color LerpUnclamped(Color a, Color b, float t)
        => new(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);

    /// <summary>逐分量乘积，Unity <c>Color * Color</c>（常用于染色叠加）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color Multiply(Color a, Color b) => new(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);

    // 下面两个不是扩展方法：Verse 的 GenColor 已经为 Color 声明了扩展 WithAlpha，两个命名空间到处同时导入，
    // 再声明同名扩展会让所有调用产生二义性（CS0121）。

    /// <summary>替换透明度后的副本。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color WithAlpha(Color c, float alpha) => new(c.r, c.g, c.b, alpha);

    /// <summary>四个通道同乘一个系数（Unity <c>Color * float</c>）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color Scaled(Color c, float factor) => new(c.r * factor, c.g * factor, c.b * factor, c.a * factor);

    /// <summary>
    /// Unity 的 <c>Color ==</c>：转成 Vector4 后差向量的平方长度小于 <see cref="UnityVector.EqualityToleranceSquared"/>。
    /// 含 NaN 时为 false。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ApproximatelyEqual(Color a, Color b)
    {
        float dr = a.r - b.r;
        float dg = a.g - b.g;
        float db = a.b - b.b;
        float da = a.a - b.a;
        return dr * dr + dg * dg + db * db + da * da < UnityVector.EqualityToleranceSquared;
    }

    /// <summary>
    /// 1.7 原版（UnityStubs）的 <c>Color ==</c>，即 <c>Color.Equals</c>：逐分量 <see cref="float.Equals(float)"/>，NaN 等于 NaN，+0 等于 -0。
    /// 用于需要与 1.7 原版结果一致、或确实要判断“颜色是否被改过”的精确比较。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ExactlyEqual(Color a, Color b)
        => a.r.Equals(b.r) && a.g.Equals(b.g) && a.b.Equals(b.b) && a.a.Equals(b.a);

    /// <summary>字节颜色转浮点颜色，<c>channel / 255f</c>（Unity 的隐式 <c>Color32 → Color</c>）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color FromBytes(Color32 c) => new(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);

    /// <summary>
    /// 浮点颜色转字节颜色，与 Unity 2022.3 相同：各通道钳制到 [0, 1]、乘 255、银行家舍入。
    /// NaN 通道在 1.7 运行时上得 0（饱和转换）。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 ToBytes(Color c) => new(ToByte(c.r), ToByte(c.g), ToByte(c.b), ToByte(c.a));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ToByte(float channel) => (byte)UnityMathf.Round(UnityMathf.Clamp01(channel) * 255f);
}
