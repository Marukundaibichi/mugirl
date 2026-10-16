using System.Runtime.CompilerServices;

namespace Mugirl.Mathematics;

/// <summary>
/// 托管的二维梯度噪声，与 RimWorld 1.7 的 <c>UnityStubs.Mathf.PerlinNoise</c> 逐位一致。
/// 通过 <see cref="UnityMathf.PerlinNoise(float, float)"/> 使用。
/// </summary>
internal static class UnityPerlinNoise
{
    // Ken Perlin 的参考置换表（UnityStubs 写入 PerlinPerm[0..255] 的同一张表）。
    // UnityStubs 把它复制成 512 项；这里对下标做 & 0xFF 等价：它用到的下标都小于 512，且双份表第 i 项就是本表第 (i & 0xFF) 项。
    private static ReadOnlySpan<byte> Permutation =>
    [
        151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225,
        140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10, 23, 190, 6, 148,
        247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32,
        57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175,
        74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122,
        60, 211, 133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54,
        65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169,
        200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64,
        52, 217, 226, 250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212,
        207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213,
        119, 248, 152, 2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9,
        129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104,
        218, 246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241,
        81, 51, 145, 235, 249, 14, 239, 107, 49, 192, 214, 31, 181, 199, 106, 157,
        184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93,
        222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180,
    ];

    public static float Sample(float x, float y)
    {
        ReadOnlySpan<byte> p = Permutation;

        float floorX = MathF.Floor(x);
        float floorY = MathF.Floor(y);
        // 与 UnityStubs 相同，依赖 1.7 运行时的饱和转换；超大坐标与 NaN 都落在固定格点上，不会越界。
        int xi = (int)floorX & 0xFF;
        int yi = (int)floorY & 0xFF;
        float xf = x - floorX;
        float yf = y - floorY;
        float u = Fade(xf);
        float v = Fade(yf);

        int a = p[xi];
        int b = p[(xi + 1) & 0xFF];
        int aa = p[(a + yi) & 0xFF];
        int ab = p[(a + yi + 1) & 0xFF];
        int ba = p[(b + yi) & 0xFF];
        int bb = p[(b + yi + 1) & 0xFF];

        float x1 = UnityMathf.Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1f, yf), u);
        float x2 = UnityMathf.Lerp(Grad(ab, xf, yf - 1f), Grad(bb, xf - 1f, yf - 1f), u);
        return UnityMathf.Clamp01(UnityMathf.Lerp(x1, x2, v) * 0.5f + 0.5f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    // UnityStubs 用 IL neg 取负（ILSpy 显示为 "0f - x"）；写成 "-x" 才能逐位保留有符号零。
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Grad(int hash, float x, float y) => (hash & 3) switch
    {
        0 => x + y,
        1 => -x + y,
        2 => x - y,
        _ => -x - y,
    };
}
