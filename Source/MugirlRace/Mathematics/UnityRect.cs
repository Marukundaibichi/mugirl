using System.Runtime.CompilerServices;

namespace Mugirl.Mathematics;

/// <summary>
/// Unity 语义的 <c>UnityEngine.Rect</c> 判断，只读取 <c>x, y, width, height</c> 数据属性，不调用 UnityStubs 的方法。
/// </summary>
/// <remarks>
/// 边界与 Unity 2022.3 及 UnityStubs 相同：左、上边界包含，右、下边界不包含（半开区间）。宽或高为负的矩形不包含任何点，
/// 对应不带 <c>allowInverse</c> 的重载。<c>Rect ==</c> 在 Unity 与 UnityStubs 中都是逐分量精确比较，这里不另行提供。
/// </remarks>
public static class UnityRect
{
    /// <summary>
    /// Unity <c>Rect.Contains(Vector2)</c>：<c>x &gt;= xMin &amp;&amp; x &lt; xMax &amp;&amp; y &gt;= yMin &amp;&amp; y &lt; yMax</c>。
    /// 含 NaN 的点得 false。<paramref name="point"/> 必须与 <paramref name="rect"/> 处于同一坐标系（GUI 组内为组的局部坐标）。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Contains(Rect rect, Vector2 point) => ContainsXY(rect, point.X, point.Y);

    /// <summary>
    /// 与 <see cref="Contains(Rect, Vector2)"/> 相同，直接接受游戏 API 返回的边界向量
    /// （如 <c>UI.MousePositionOnUIInverted</c>），省去一次转换。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Contains(Rect rect, UVector2 point) => ContainsXY(rect, point.x, point.y);

    /// <summary>
    /// Unity <c>Rect.Overlaps(Rect)</c>：<c>other.xMax &gt; xMin &amp;&amp; other.xMin &lt; xMax &amp;&amp; other.yMax &gt; yMin &amp;&amp; other.yMin &lt; yMax</c>。
    /// 只共享一条边的矩形不重叠。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Overlaps(Rect rect, Rect other)
        => other.x + other.width > rect.x
            && other.x < rect.x + rect.width
            && other.y + other.height > rect.y
            && other.y < rect.y + rect.height;

    /// <summary>中心点 <c>(x + width·0.5, y + height·0.5)</c>，与 Unity <c>Rect.center</c> 逐位一致，返回模组内部的向量类型。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Center(Rect rect) => new(rect.x + rect.width * 0.5f, rect.y + rect.height * 0.5f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ContainsXY(Rect rect, float x, float y)
    {
        float xMin = rect.x;
        float yMin = rect.y;
        return x >= xMin && x < xMin + rect.width && y >= yMin && y < yMin + rect.height;
    }
}
