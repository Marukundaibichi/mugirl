using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Mugirl;

// 公共 API：其他 Mod（如白绒物语）用 Harmony 补丁 IsMugirl 扩展雪牛娘身份判断；类名、方法名与参数名 pawn 都不能改。
public static class MountedPawnUtility
{
    // 公开 API（docs/architecture/ADR-008）：骑乘代码以它判断雪牛娘身份，依赖模组（白绒物语）用 Harmony 补丁它，
    // 让自己的种族也能被骑乘；因此禁止内联，否则已内联的调用方看不到补丁。语义与 MugirlIdentity.IsMugirlPawn 相同。
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool IsMugirl([NotNullWhen(true)] Pawn? pawn)
    {
        return MugirlIdentity.IsMugirlPawn(pawn);
    }
}
